#pragma warning disable OPENAI001 // Experimental OpenAI APIs

using System.Diagnostics;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using OilGasFoundryDemo.Agents;
using OpenAI.Responses;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Credential — exclude ManagedIdentity in dev to avoid noisy IMDS timeout spans
builder.Services.AddSingleton<TokenCredential>(_ =>
    builder.Environment.IsDevelopment()
        ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true })
        : new DefaultAzureCredential());

// AIProjectClient — single instance, SDK handles auth + connection pooling
builder.Services.AddSingleton<AIProjectClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var credential = sp.GetRequiredService<TokenCredential>();
    var endpoint = DemoHelpers.GetProjectEndpoint(config);
    return new AIProjectClient(new Uri(endpoint), credential);
});
builder.Services.AddSingleton<IReadOnlyList<OilGasAgentDefinition>>(sp =>
    DemoHelpers.GetConfiguredAgents(sp.GetRequiredService<IConfiguration>()));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/agents", (IReadOnlyList<OilGasAgentDefinition> agents) =>
        agents.Select(OilGasAgentProfile.FromDefinition))
    .WithName("GetOilGasAgents");

app.MapGet("/demo-scenarios", (IReadOnlyList<OilGasAgentDefinition> agents) =>
        agents.Select(DemoScenario.FromDefinition))
    .WithName("GetDemoScenarios");

app.MapPost("/agents/{agentName}/invoke", async (
    string agentName,
    PromptAgentRequest request,
    IReadOnlyList<OilGasAgentDefinition> agents,
    AIProjectClient projectClient,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
        return Results.BadRequest(new { error = "Prompt is required." });

    var agent = agents.FirstOrDefault(candidate =>
        string.Equals(candidate.Name, agentName, StringComparison.OrdinalIgnoreCase));

    if (agent is null)
        return Results.NotFound(new { error = $"Foundry prompt agent '{agentName}' is not configured for this service." });

    logger.LogInformation("Invoking Foundry prompt agent {AgentName}", agent.Name);

    var isMicrosoftDocsAgent = agent.Tool == OilGasAgentTool.MicrosoftLearnMcp;
    var openAiClient = projectClient.ProjectOpenAIClient;
    string? conversationId = null;

    // The MCP approval flow uses previous_response_id, which can't be combined with a default conversation.
    var responsesClient = isMicrosoftDocsAgent
        ? openAiClient.GetProjectResponsesClientForAgent(agent.Name)
        : await CreateConversationScopedResponsesClientAsync(openAiClient, agent.Name, cancellationToken);

    var response = isMicrosoftDocsAgent
        ? await DemoHelpers.CreateResponseWithMcpApprovalAsync(responsesClient, request.Prompt, logger, cancellationToken)
        : (await responsesClient.CreateResponseAsync(request.Prompt, previousResponseId: null, cancellationToken)).Value;

    var text = response.GetOutputText() ?? string.Empty;

    return Results.Ok(new PromptAgentResponse(agent.Name, agent.Name, conversationId, response.Id, text));

    async Task<ProjectResponsesClient> CreateConversationScopedResponsesClientAsync(
        ProjectOpenAIClient client,
        string name,
        CancellationToken token)
    {
        var conversation = await client.GetProjectConversationsClient()
            .CreateProjectConversationAsync(new ProjectConversationCreationOptions(), token);
        conversationId = conversation.Value.Id;

        return client.GetProjectResponsesClientForAgent(
            defaultAgent: new AgentReference(name, null),
            defaultConversationId: conversationId);
    }
})
.WithName("InvokeAgent");

app.MapDefaultEndpoints();

app.Run();

record OilGasAgentProfile(string Name, string DisplayName, string Description, string SamplePrompt)
{
    public static OilGasAgentProfile FromDefinition(OilGasAgentDefinition definition) =>
        new(definition.Name, definition.DisplayName, definition.Description, definition.SamplePrompt);
}

record DemoScenario(string Title, string Objective, string AgentName)
{
    public static DemoScenario FromDefinition(OilGasAgentDefinition definition) =>
        new(definition.ScenarioTitle, definition.ScenarioObjective, definition.Name);
}

record PromptAgentRequest(string Prompt);

record PromptAgentResponse(
    string AgentName,
    string AgentId,
    string? ConversationId,
    string? ResponseId,
    string Text);

static class DemoHelpers
{
    public static IReadOnlyList<OilGasAgentDefinition> GetConfiguredAgents(IConfiguration configuration)
    {
        var configuredAgents = OilGasAgentCatalog.All
            .Where(agent => HasAgentReference(configuration, agent))
            .ToArray();

        return configuredAgents.Length > 0 ? configuredAgents : OilGasAgentCatalog.All;
    }

    public static string GetProjectEndpoint(IConfiguration configuration)
    {
        var raw = configuration["ConnectionStrings:oil-gas-project"]
            ?? configuration["OIL_GAS_PROJECT_ENDPOINT"]
            ?? configuration["AZURE_AI_PROJECT_ENDPOINT"]
            ?? throw new InvalidOperationException("Missing Foundry project connection string.");

        if (Uri.TryCreate(raw, UriKind.Absolute, out _))
            return raw;

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = part.IndexOf('=');
            if (sep <= 0) continue;
            var key = part[..sep];
            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase)
                || key.Equals("ProjectEndpoint", StringComparison.OrdinalIgnoreCase))
                return part[(sep + 1)..];
        }

        throw new InvalidOperationException("Foundry project connection string does not contain an endpoint.");
    }

    private static bool HasAgentReference(IConfiguration configuration, OilGasAgentDefinition agent)
    {
        if (!string.IsNullOrWhiteSpace(configuration[$"ConnectionStrings:{agent.Name}"]))
            return true;

        var prefix = agent.Name.Replace('-', '_').ToUpperInvariant();
        return string.Equals(configuration[$"{prefix}_AGENTNAME"], agent.Name, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(configuration[$"{prefix}_CONNECTIONSTRING"]);
    }

    public static async Task<ResponseResult> CreateResponseWithMcpApprovalAsync(
        ProjectResponsesClient responsesClient,
        string prompt,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var captureContent = string.Equals(
            Environment.GetEnvironmentVariable("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"),
            "true", StringComparison.OrdinalIgnoreCase);

        // One outer span for the full conversation: prompt → tool call(s) → final answer.
        // This becomes the single "agentic step" shown in the Aspire GenAI panel.
        // The SDK creates child invoke_agent spans for each individual Foundry API call.
        using var conversationActivity = DemoDiagnostics.ActivitySource.StartActivity(
            "invoke_agent", ActivityKind.Client);
        conversationActivity?.SetTag("gen_ai.system", "az.ai.inference");
        conversationActivity?.SetTag("gen_ai.operation.name", "invoke_agent");
        conversationActivity?.SetTag("peer.service", "azure-ai-foundry");

        if (captureContent)
        {
            conversationActivity?.AddEvent(new ActivityEvent("gen_ai.user.message",
                tags: new ActivityTagsCollection
                {
                    ["gen_ai.event.content"] = $"{{\"role\":\"user\",\"content\":{System.Text.Json.JsonSerializer.Serialize(prompt)}}}"
                }));
        }

        CreateResponseOptions? nextResponseOptions = new()
        {
            InputItems = { ResponseItem.CreateUserMessageItem(prompt) }
        };

        ResponseResult? latestResponse = null;

        while (nextResponseOptions is not null)
        {
            latestResponse = (await responsesClient.CreateResponseAsync(nextResponseOptions, cancellationToken)).Value;
            nextResponseOptions = null;

            foreach (var responseItem in latestResponse.OutputItems)
            {
                if (responseItem is not McpToolCallApprovalRequestItem mcpToolCall)
                    continue;

                var approved = string.Equals(mcpToolCall.ServerLabel, "microsoft-learn", StringComparison.OrdinalIgnoreCase);
                logger.LogInformation("MCP approval requested for {ServerLabel}/{ToolName}: {Approved}",
                    mcpToolCall.ServerLabel, mcpToolCall.ToolName, approved);

                // Record the tool call on the outer conversation span.
                // ToolArguments contains the search query JSON sent to the MCP server.
                if (captureContent)
                {
                    var argsJson = mcpToolCall.ToolArguments?.ToString() ?? "{}";
                    conversationActivity?.AddEvent(new ActivityEvent("gen_ai.tool.message",
                        tags: new ActivityTagsCollection
                        {
                            ["gen_ai.event.content"] = $"{{\"role\":\"tool\",\"tool_name\":\"{mcpToolCall.ToolName}\",\"server\":\"{mcpToolCall.ServerLabel}\",\"arguments\":{argsJson},\"approved\":{(approved ? "true" : "false")}}}"
                        }));
                }

                var approvalOptions = new CreateResponseOptions { PreviousResponseId = latestResponse.Id };
                approvalOptions.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(
                    approvalRequestId: mcpToolCall.Id,
                    approved: approved));

                // Timing-only child span — no gen_ai.system so it doesn't appear as a
                // separate LLM step in the Aspire GenAI panel, just as a trace child.
                using var mcpTimingSpan = DemoDiagnostics.ActivitySource.StartActivity(
                    $"mcp {mcpToolCall.ToolName}", ActivityKind.Internal);
                mcpTimingSpan?.SetTag("mcp.server.label", mcpToolCall.ServerLabel);
                mcpTimingSpan?.SetTag("mcp.tool.name", mcpToolCall.ToolName);
                mcpTimingSpan?.SetTag("mcp.approved", approved);

                latestResponse = (await responsesClient.CreateResponseAsync(approvalOptions, cancellationToken)).Value;

                foreach (var followItem in latestResponse.OutputItems)
                {
                    if (followItem is McpToolCallApprovalRequestItem)
                    {
                        nextResponseOptions = new CreateResponseOptions { PreviousResponseId = latestResponse.Id };
                        break;
                    }
                }

                break;
            }
        }

        if (latestResponse is null)
            throw new InvalidOperationException("Foundry did not return a response.");

        // Record the final answer on the outer conversation span.
        if (captureContent)
        {
            var finalText = latestResponse.GetOutputText() ?? string.Empty;
            conversationActivity?.AddEvent(new ActivityEvent("gen_ai.choice",
                tags: new ActivityTagsCollection
                {
                    ["gen_ai.event.content"] = $"{{\"finish_reason\":\"stop\",\"message\":{{\"role\":\"assistant\",\"content\":{System.Text.Json.JsonSerializer.Serialize(finalText)}}}}}"
                }));
        }

        return latestResponse;
    }
}

static class DemoDiagnostics
{
    public static readonly ActivitySource ActivitySource = new("OilGasFoundryDemo.ApiService");
}
