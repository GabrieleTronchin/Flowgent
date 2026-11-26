namespace Flowgent.Engine.Core.Configuration;

/// <summary>An MCP server the engine reaches domain tools on (section <c>Flowgent:McpServers</c>).</summary>
public sealed class McpServerOptions
{
    /// <summary>The workflow <c>serverLabel</c> this server answers for (e.g. <c>support</c>).</summary>
    public string Label { get; set; } = "";

    /// <summary>Base URL of the MCP server's Streamable-HTTP endpoint.</summary>
    public string Url { get; set; } = "";
}
