namespace Flowgent.Runtime.Configuration;

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

    /// <summary>Definition distribution settings (mounted volume primary, HTTP secondary).</summary>
    public DefinitionsOptions Definitions { get; set; } = new();
}

/// <summary>Where module definitions are read from (section <c>Flowgent:Definitions</c>).</summary>
public sealed class DefinitionsOptions
{
    /// <summary>
    /// Root of a mounted definitions volume. When set and a module's folder exists under it, definitions are read from
    /// the volume (primary) instead of the module service over HTTP (secondary). The module folder is <c>&lt;Path&gt;/&lt;moduleId&gt;</c>.
    /// </summary>
    public string? Path { get; set; }
}

/// <summary>A module service the engine pulls definitions from.</summary>
public sealed class ModuleEndpointOptions
{
    public string Id { get; set; } = "";

    /// <summary>Base address, e.g. <c>http://localhost:5082</c> or <c>http://support</c> (service discovery).</summary>
    public string Url { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Interval between package checks (also the retry interval while the module is down).</summary>
    public double RefreshSeconds { get; set; } = 5;

    /// <summary>Timeout of a single call to the module (package fetch; default for tool calls).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Retries for transient failures (connection errors, 5xx, 408, 429).</summary>
    public int Retries { get; set; } = 2;

    public int RetryDelayMilliseconds { get; set; } = 500;

    public static string HttpClientName(string moduleId) => $"flowgent-module-{moduleId}";
}
