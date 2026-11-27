namespace Flowgent.Modules.Support.Tests;

using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Mcp.Support;
using Flowgent.Module.Sdk;
using Flowgent.Testing;

/// <summary>Shared helpers: the support definition and an in-process platform (gateway + engine + support).</summary>
public static class SupportPlatform
{
    /// <summary>The support module folder (<c>samples/support</c>), found from the test output folder.</summary>
    public static string ModuleFolder { get; } = FindModuleFolder();

    public static string DefinitionFolder => Path.Combine(ModuleFolder, "definition");

    private static readonly Lazy<ModuleDefinition> LazyDefinition = new(() => ModuleDefinitionAssert.IsValid(DefinitionFolder));

    public static ModuleDefinition Definition => LazyDefinition.Value;

    public static IReadOnlyList<SampleDefinition> Samples => Definition.Samples;

    public static SampleDefinition Sample(string id) => Samples.Single(s => s.Id == id);

    public static Task<PlatformHarness> StartAsync(Action<PlatformOptions>? configure = null) =>
        PlatformHarness.StartAsync(options =>
        {
            options.AddModule(SupportHost.ModuleId, SupportHost.Create, Path.Combine(ModuleFolder, "service"), "/api/support/{**catch-all}");

            // The locateComponent tool is served by the standalone support MCP server. It carries the workflow's
            // serverLabel "support"; the dummy-apps root is the module's real dummy-apps folder.
            options.AddMcpServer(SupportHost.ModuleId, SupportMcpHost.Create, Path.Combine(ModuleFolder, "mcp", "Flowgent.Mcp.Support"));
            options.ConfigureModule(SupportHost.ModuleId, SupportMcpOptions.EnvironmentVariable, Path.Combine(ModuleFolder, "dummy-apps"));

            options.WithGateway();
            configure?.Invoke(options);
        });

    // The module folder holds definition/module.yaml; walk up from bin/... until it is found.
    private static string FindModuleFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "definition", "module.yaml")) && Directory.Exists(Path.Combine(directory.FullName, "dummy-apps")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Support module folder (containing definition/module.yaml and dummy-apps) not found.");
    }
}
