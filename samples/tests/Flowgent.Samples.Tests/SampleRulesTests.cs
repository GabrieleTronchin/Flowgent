namespace Flowgent.Samples.Tests;

using System.Reflection;
using System.Xml.Linq;
using Flowgent.Modules.Jokes;
using Flowgent.Modules.Support;
using NetArchTest.Rules;

/// <summary>
/// Dependency rules of the samples: a sample module service uses only the Module SDK, the contracts, the definitions
/// and the service defaults of the core; the support MCP server uses only the service defaults and the MCP package; no
/// sample depends on the engine implementation.
/// </summary>
public class SampleRulesTests
{
    private static readonly string Backend = Path.GetFullPath(Path.Combine(SamplesPaths.Root, "..", "backend"));

    private static readonly string[] EngineNamespaces = ["Flowgent.Engine.Core", "Flowgent.Persistence", "Flowgent.Engine.Service", "Flowgent.Gateway", "Flowgent.Testing"];

    private static readonly string[] AllowedForModules =
    [
        Core("shared", "Flowgent.Module.Sdk"),
        Core("shared", "Flowgent.Contracts"),
        Core("shared", "Flowgent.Definitions"),
        Core("shared", "Flowgent.ServiceDefaults"),
    ];

    // Every samples/<sample>/service/*.csproj, relative to the samples folder.
    private static readonly string[] ModuleServiceProjects =
    [
        .. Directory.GetDirectories(SamplesPaths.Root)
            .Select(sample => Path.Combine(sample, "service"))
            .Where(Directory.Exists)
            .SelectMany(service => Directory.GetFiles(service, "*.csproj"))
            .Select(p => Path.GetRelativePath(SamplesPaths.Root, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    public static TheoryData<string> SampleModuleServices => [.. ModuleServiceProjects];

    public static TheoryData<string> SampleAssemblies =>
    [
        typeof(SupportHost).Assembly.GetName().Name!,
        typeof(JokesHost).Assembly.GetName().Name!,
        "Flowgent.Mcp.Support",
    ];

    [Fact]
    public void EverySampleModuleService_IsCovered() =>
        Assert.Superset(
            new HashSet<string>(["jokes/service/Flowgent.Modules.Jokes.csproj", "support/service/Flowgent.Modules.Support.csproj"]),
            ModuleServiceProjects.ToHashSet());

    [Theory]
    [MemberData(nameof(SampleModuleServices))]
    public void SampleModuleServices_ReferenceOnlyTheSdkContractsDefinitionsAndServiceDefaults(string project)
    {
        var references = DirectReferences(Path.Combine(SamplesPaths.Root, project));

        Assert.All(references, r => Assert.Contains(r, AllowedForModules, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void SupportMcpServer_ReferencesOnlyServiceDefaultsAndTheMcpPackage()
    {
        // The standalone support MCP server is not a module service: it never reaches the Module SDK, contracts,
        // definitions or any engine project. Its only NuGet dependency is the MCP ASP.NET Core package.
        var project = SamplesPaths.Sample("support", "mcp", "Flowgent.Mcp.Support", "Flowgent.Mcp.Support.csproj");

        Assert.Equal([Core("shared", "Flowgent.ServiceDefaults")], DirectReferences(project));
        Assert.Equal(["ModelContextProtocol.AspNetCore"], XDocument.Load(project).Descendants("PackageReference").Select(r => r.Attribute("Include")!.Value));
    }

    [Theory]
    [MemberData(nameof(SampleAssemblies))]
    public void Samples_DoNotDependOnEngineImplementation(string assemblyName)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName)).ShouldNot().HaveDependencyOnAny(EngineNamespaces).GetResult();

        Assert.True(result.IsSuccessful, $"Types depending on engine implementation: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    private static string Core(string folder, string name) => Path.Combine(Backend, folder, name, $"{name}.csproj");

    private static List<string> DirectReferences(string project)
    {
        var directory = Path.GetDirectoryName(project)!;
        return
        [
            .. XDocument.Load(project).Descendants("ProjectReference")
                .Select(r => Path.GetFullPath(Path.Combine(directory, r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))),
        ];
    }
}
