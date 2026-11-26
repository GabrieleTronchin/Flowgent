namespace Flowgent.Engine.Core.Configuration;

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
