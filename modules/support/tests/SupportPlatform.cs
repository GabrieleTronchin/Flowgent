namespace Flowgent.Modules.Support.Tests;

using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Mcp.Support;
using Flowgent.Module.Sdk;
using Flowgent.Testing;

/// <summary>Shared helpers: the support definition and an in-process platform (gateway + engine + support).</summary>
public static class SupportPlatform
{
    public static string ModuleFolder => Path.Combine(PlatformHarness.RepositoryRoot, "modules", "support");

    public static string DefinitionFolder => Path.Combine(ModuleFolder, "definition");

    private static readonly Lazy<ModuleDefinition> LazyDefinition = new(() => ModuleDefinitionAssert.IsValid(DefinitionFolder));

    public static ModuleDefinition Definition => LazyDefinition.Value;

    public static IReadOnlyList<SampleDefinition> Samples => Definition.Samples;

    public static SampleDefinition Sample(string id) => Samples.Single(s => s.Id == id);

    public static Task<PlatformHarness> StartAsync(Action<PlatformOptions>? configure = null) =>
        PlatformHarness.StartAsync(options =>
        {
            options.AddModule(SupportHost.ModuleId, SupportHost.Create, Path.Combine("modules", "support", "service"), "/api/support/{**catch-all}");

            // The locateComponent tool is now served by the standalone support MCP server (FEAT-003). It carries the
            // workflow's serverLabel "support"; the dummy-apps root is the module's real dummy-apps folder so every
            // located path/component/snippet value is identical to the pre-MCP SDK tool.
            options.AddMcpServer(SupportHost.ModuleId, SupportMcpHost.Create, Path.Combine("backend", "src", "Services", "Flowgent.Mcp.Support"));
            options.ConfigureModule(SupportHost.ModuleId, SupportMcpOptions.EnvironmentVariable, Path.Combine(PlatformHarness.RepositoryRoot, "modules", "support", "dummy-apps"));

            options.WithGateway();
            configure?.Invoke(options);
        });
}
