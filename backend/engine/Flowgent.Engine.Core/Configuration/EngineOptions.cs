namespace Flowgent.Engine.Core.Configuration;

/// <summary>Engine configuration (section <c>Flowgent</c>).</summary>
public sealed class EngineOptions
{
    public const string SectionName = "Flowgent";

    /// <summary>Start a run only when no other run is running or awaiting input.</summary>
    public bool SequentialProcessing { get; set; }

    /// <summary>Enables the gRPC run stream and the agents endpoint.</summary>
    public bool EnableVisualization { get; set; } = true;

    public double InboxPollingIntervalSeconds { get; set; } = 5;

    /// <summary>Default timeout of tool calls when neither the tool nor the step sets one.</summary>
    public int DefaultToolTimeoutSeconds { get; set; } = 30;

    public List<ModuleEndpointOptions> Modules { get; set; } = [];

    /// <summary>
    /// MCP servers the engine reaches domain tools on over Streamable HTTP (section <c>Flowgent:McpServers</c>).
    /// Each entry maps a workflow <c>serverLabel</c> to a server URL; a tool whose label is mapped here is routed
    /// over HTTP, an unmapped label falls back to the legacy in-process invoker.
    /// The reserved label <c>flowgent</c> (ordinal-ignore-case) is never HTTP-routable and such entries are skipped.
    /// </summary>
    public List<McpServerOptions> McpServers { get; set; } = [];

    /// <summary>Definition distribution settings (mounted volume primary, HTTP secondary).</summary>
    public DefinitionsOptions Definitions { get; set; } = new();
}
