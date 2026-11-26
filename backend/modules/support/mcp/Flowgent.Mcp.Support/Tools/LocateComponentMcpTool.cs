namespace Flowgent.Mcp.Support.Tools;

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// The <c>locateComponent</c> MCP tool (bare wire name): a thin adapter over the pure <see cref="LocateComponentCore"/>.
/// It reads the dummy-apps root from <see cref="SupportMcpOptions"/> (bound from <c>SUPPORT_DUMMYAPPS_ROOT</c>),
/// delegates to the core algorithm, and returns the <see cref="LocateResult"/> as a single camelCase JSON object
/// string (<c>{found,component,path,language,snippet}</c>, NIT N1). A <see cref="LocateComponentException"/> maps to an
/// MCP error result (<see cref="CallToolResult.IsError"/>) carrying the verbatim message, so the engine's HTTP branch
/// surfaces the same failure reason the SDK adapter used to raise.
/// </summary>
[McpServerToolType]
public sealed class LocateComponentMcpTool(IOptions<SupportMcpOptions> options)
{
    // The returned LocateResult.Path is module-relative and built from the final segment of the dummy-apps root (e.g.
    // "dummy-apps"), so the output path stays identical to the pre-MCP SDK tool regardless of where the root is mounted.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SupportMcpOptions _options = options.Value;

    [McpServerTool(Name = "locateComponent")]
    [Description("Locates the dummy-app source file mentioned in a support email and returns its module-relative path and content.")]
    public async Task<CallToolResult> LocateComponentAsync(
        [Description("The dummy application folder, e.g. application-a.")] string applicationFolder,
        [Description("The support email text to scan for a file or type mention.")] string? text,
        CancellationToken cancellationToken)
    {
        var dummyAppsRoot = Path.GetFullPath(_options.DummyAppsRoot);
        var relativeRootPrefix = Path.GetFileName(dummyAppsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        try
        {
            var result = await LocateComponentCore.LocateAsync(applicationFolder, text, dummyAppsRoot, relativeRootPrefix, cancellationToken);
            return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result, Json) }] };
        }
        catch (LocateComponentException ex)
        {
            return new CallToolResult { Content = [new TextContentBlock { Text = ex.Message }], IsError = true };
        }
    }
}
