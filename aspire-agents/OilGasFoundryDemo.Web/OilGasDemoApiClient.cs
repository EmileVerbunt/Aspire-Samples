namespace OilGasFoundryDemo.Web;

public class OilGasDemoApiClient(HttpClient httpClient)
{
    public async Task<IQueryable<OilGasAgentProfile>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        var agents = await httpClient.GetFromJsonAsync<List<OilGasAgentProfile>>("/agents", cancellationToken);
        return (agents ?? []).AsQueryable();
    }

    public async Task<IQueryable<DemoScenario>> GetScenariosAsync(CancellationToken cancellationToken = default)
    {
        var scenarios = await httpClient.GetFromJsonAsync<List<DemoScenario>>("/demo-scenarios", cancellationToken);
        return (scenarios ?? []).AsQueryable();
    }

    public async Task<PromptAgentResponse?> InvokeAgentAsync(string agentName, string prompt, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/agents/{agentName}/invoke",
            new PromptAgentRequest(prompt),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PromptAgentResponse>(cancellationToken);
    }
}

public record OilGasAgentProfile(string Name, string DisplayName, string Description, string SamplePrompt);

public record DemoScenario(string Title, string Objective, string AgentName);

public record PromptAgentRequest(string Prompt);

public record PromptAgentResponse(
    string AgentName,
    string AgentId,
    string? ConversationId,
    string? ResponseId,
    string Text);
