#pragma warning disable OPENAI001 // Experimental OpenAI Responses APIs

using Aspire.Hosting.Foundry;
using OilGasFoundryDemo.Agents;
using OpenAI.Responses;

var builder = DistributedApplication.CreateBuilder(args);

var foundry = builder.AddFoundry("foundry");
var oilGasProject = foundry.AddProject(OilGasAgentCatalog.ProjectName);
var chat = oilGasProject.AddModelDeployment(OilGasAgentCatalog.ModelDeploymentName, FoundryModel.OpenAI.Gpt5Mini)
    .WithProperties(deployment =>
    {
        deployment.SkuName = "GlobalStandard";
        deployment.SkuCapacity = 10;
    });

var webSearch = oilGasProject.AddWebSearchTool("web-search");

var webResearcher = oilGasProject.AddPromptAgent(chat,
    name: OilGasAgentCatalog.WebResearcher.Name,
    instructions: OilGasAgentCatalog.WebResearcher.Instructions)
    .WithTool(webSearch);

var reservoirEngineer = oilGasProject.AddPromptAgent(chat,
    name: OilGasAgentCatalog.ReservoirEngineer.Name,
    instructions: OilGasAgentCatalog.ReservoirEngineer.Instructions);

var drillingOps = oilGasProject.AddPromptAgent(chat,
    name: OilGasAgentCatalog.DrillingOps.Name,
    instructions: OilGasAgentCatalog.DrillingOps.Instructions);

var hseCompliance = oilGasProject.AddPromptAgent(chat,
    name: OilGasAgentCatalog.HseCompliance.Name,
    instructions: OilGasAgentCatalog.HseCompliance.Instructions);

var microsoftDocs = oilGasProject.AddPromptAgent(chat,
    name: OilGasAgentCatalog.MicrosoftDocs.Name,
    instructions: OilGasAgentCatalog.MicrosoftDocs.Instructions)
    .WithCustomTool(new MicrosoftLearnMcpTool());

var apiService = builder.AddProject<Projects.OilGasFoundryDemo_ApiService>("apiservice")
    .WithReference(oilGasProject)
    .WithReference(chat)
    .WithReference(webResearcher)
    .WithReference(reservoirEngineer)
    .WithReference(drillingOps)
    .WithReference(hseCompliance)
    .WithReference(microsoftDocs)
    // Enable Azure AI Projects SDK GenAI tracing — emits spans to Aspire dashboard + Foundry portal
    .WithEnvironment("AZURE_EXPERIMENTAL_ENABLE_GENAI_TRACING", "true")
    // Capture message content in traces (prompts + responses visible in GenAI visualizer)
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "true")
    .WaitFor(webResearcher)
    .WaitFor(reservoirEngineer)
    .WaitFor(drillingOps)
    .WaitFor(hseCompliance)
    .WaitFor(microsoftDocs)
    .WaitFor(chat);

builder.AddProject<Projects.OilGasFoundryDemo_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();

sealed class MicrosoftLearnMcpTool : IFoundryTool
{
    public Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ResponseTool>(ResponseTool.CreateMcpTool(
            serverLabel: "microsoft-learn",
            serverUri: new Uri("https://learn.microsoft.com/api/mcp"),
            toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval)));
}
