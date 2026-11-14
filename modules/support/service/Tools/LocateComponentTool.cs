namespace Flowgent.Modules.Support.Tools;

using Flowgent.Module.Sdk;

public sealed record LocateComponentInput(string ApplicationFolder, string Text);

/// <param name="Path">Relative to the support module folder, e.g. <c>dummy-apps/application-a/src/Controllers/OrderController.cs</c>.</param>
public sealed record LocateComponentOutput(bool Found, string Component, string Path, string Language, string? Snippet);

/// <summary>
/// <c>support.locateComponent</c>: thin module-SDK adapter over the pure <see cref="LocateComponentCore"/>. It
/// reads <c>dummyAppsRoot</c> from the module config and the module root from the invocation, delegates to the
/// core algorithm, and translates <see cref="LocateComponentException"/> into the SDK's <c>ToolException</c> so
/// the engine-facing behavior is unchanged.
/// </summary>
public sealed class LocateComponentTool : ModuleTool<LocateComponentInput, LocateComponentOutput>
{
    public const string Name = "support.locateComponent";
    public const string DefaultDummyAppsRoot = "dummy-apps";

    protected override async Task<LocateComponentOutput> InvokeAsync(LocateComponentInput input, ToolInvocation invocation, CancellationToken ct)
    {
        var rootSetting = invocation.Module.Config["dummyAppsRoot"]?.GetValue<string>() ?? DefaultDummyAppsRoot;
        var dummyAppsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(invocation.Module.ModuleRoot, rootSetting));

        try
        {
            var result = await LocateComponentCore.LocateAsync(input.ApplicationFolder, input.Text, dummyAppsRoot, rootSetting, ct);
            return new LocateComponentOutput(result.Found, result.Component, result.Path, result.Language, result.Snippet);
        }
        catch (LocateComponentException ex)
        {
            throw new ToolException(ex.Message);
        }
    }
}
