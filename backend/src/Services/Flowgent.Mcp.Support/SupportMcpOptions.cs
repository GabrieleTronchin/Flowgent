namespace Flowgent.Mcp.Support;

/// <summary>
/// Options for the support MCP server. <see cref="DummyAppsRoot"/> is the absolute path to the dummy-apps folder the
/// <c>locateComponent</c> tool searches; it is bound from the <c>SUPPORT_DUMMYAPPS_ROOT</c> environment variable and
/// defaults to the container path <c>/app/dummy-apps</c>.
/// </summary>
public sealed class SupportMcpOptions
{
    public const string EnvironmentVariable = "SUPPORT_DUMMYAPPS_ROOT";

    public const string DefaultDummyAppsRoot = "/app/dummy-apps";

    public string DummyAppsRoot { get; set; } = DefaultDummyAppsRoot;
}
