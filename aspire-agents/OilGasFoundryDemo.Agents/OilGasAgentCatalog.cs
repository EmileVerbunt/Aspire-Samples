namespace OilGasFoundryDemo.Agents;

public enum OilGasAgentTool
{
    None,
    WebSearch,
    MicrosoftLearnMcp
}

public sealed record OilGasAgentDefinition(
    string Name,
    string DisplayName,
    string Description,
    string Instructions,
    string SamplePrompt,
    string ScenarioTitle,
    string ScenarioObjective,
    OilGasAgentTool Tool = OilGasAgentTool.None);

public static class OilGasAgentCatalog
{
    public const string ProjectName = "oil-gas-project";
    public const string ModelDeploymentName = "chat";

    public static OilGasAgentDefinition ReservoirEngineer { get; } = new(
        "reservoir-engineer",
        "Reservoir Engineer",
        "Analyzes production trends, reservoir behavior, and recovery opportunities.",
        """
        You are the Reservoir Engineer Agent for an oil and gas demo.
        Focus on production optimization, reservoir surveillance, decline trends, pressure support,
        water cut, recovery factor, and uncertainty. Ask for missing field data when needed.
        Provide concise recommendations with assumptions, risks, and measurements needed to validate them.
        """,
        "Water cut is rising in producer P-17 while nearby injector pressure is stable. What surveillance and optimization steps should we prioritize?",
        "Production optimization",
        "Show reservoir surveillance reasoning and recommendations.");

    public static OilGasAgentDefinition DrillingOps { get; } = new(
        "drilling-ops",
        "Drilling Operations",
        "Supports well planning, hazard mitigation, and non-productive-time reduction.",
        """
        You are the Drilling Operations Agent for an oil and gas demo.
        Focus on well trajectory, drilling hazards, mud window, stuck-pipe prevention, BHA considerations,
        casing points, lessons learned, and non-productive-time reduction.
        Return practical operational guidance with safety caveats and decision checkpoints.
        """,
        "We are planning a 12.25 inch section through a depleted interval with narrow pore-pressure/fracture-gradient margin. What drilling risks and mitigations should the morning call cover?",
        "Well delivery morning call",
        "Show drilling risk review and decision checkpoints.");

    public static OilGasAgentDefinition HseCompliance { get; } = new(
        "hse-compliance",
        "HSE Compliance",
        "Reviews process safety, environmental, and regulatory controls for field operations.",
        """
        You are the HSE Compliance Agent for an oil and gas demo.
        Focus on process safety, permit-to-work, SIMOPS, emissions, spill prevention, incident response,
        barrier management, and regulatory evidence.
        Be conservative: call out uncertainties, required approvals, and stop-work triggers.
        """,
        "A compressor maintenance job overlaps with hot work and tank gauging near the same pad. What SIMOPS controls and stop-work triggers should be in the permit pack?",
        "SIMOPS safety review",
        "Show HSE compliance checks, approvals, and barrier thinking.");

    public static OilGasAgentDefinition WebResearcher { get; } = new(
        "web-researcher",
        "Web Researcher",
        "Uses Foundry web search to research current oil and gas market, regulatory, and technology topics.",
        """
        You are the Web Researcher for an oil and gas operations demo.
        Use web search for current information, cite sources, summarize tradeoffs,
        and keep answers concise and practical for field, engineering, and HSE stakeholders.
        """,
        "Find current sources on methane emissions reduction technologies for upstream oil and gas operations. Summarize practical tradeoffs and cite sources.",
        "Market and regulatory research",
        "Show frontend-to-Foundry tracing with web search.",
        OilGasAgentTool.WebSearch);

    public static OilGasAgentDefinition MicrosoftDocs { get; } = new(
        "microsoft-docs",
        "Microsoft Documentation",
        "Uses a Foundry MCP tool connected to the Microsoft Learn MCP server.",
        """
        You are the Microsoft Documentation Agent for an Aspire and Foundry demo.
        Use the Microsoft Learn MCP tool to find official documentation.
        Keep answers concise, practical, and cite the Microsoft Learn URLs you used.
        """,
        "How do I configure Aspire OpenTelemetry so Azure AI Foundry agent traces show prompts, responses, and tool calls?",
        "Microsoft Docs MCP",
        "Show a Foundry agent MCP tool call and approval flow.",
        OilGasAgentTool.MicrosoftLearnMcp);

    public static IReadOnlyList<OilGasAgentDefinition> All { get; } =
    [
        ReservoirEngineer,
        DrillingOps,
        HseCompliance,
        WebResearcher,
        MicrosoftDocs
    ];
}
