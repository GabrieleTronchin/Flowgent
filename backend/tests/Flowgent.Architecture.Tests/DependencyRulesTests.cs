namespace Flowgent.Architecture.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using Flowgent.Fixtures.Module;
using Flowgent.Modules.Support;
using Flowgent.Modules.Template;
using NetArchTest.Rules;

/// <summary>
/// Dependency rules of the platform: the engine never depends on modules, modules depend only on the Module SDK
/// and the contracts, and the definition/runtime libraries stay free of ASP.NET Core.
/// Project files are inspected for references; compiled assemblies are inspected with NetArchTest.
/// </summary>
public class DependencyRulesTests
{
    private static readonly string[] EngineNamespaces = ["Flowgent.Runtime", "Flowgent.Persistence", "Flowgent.Engine.Service", "Flowgent.Gateway", "Flowgent.Testing"];

    private static readonly string[] SupportDomainTerms = ["IncomingEmail", "IssueCategory", "TeamRouter", "/emails"];

    public static TheoryData<string> EngineAndGatewayProjects =>
        [.. ProjectGraph.EngineProjects.Append(ProjectGraph.Service("Flowgent.Gateway")).Select(ProjectGraph.Relative)];

    public static TheoryData<string> ModuleServiceProjects => [.. ProjectGraph.ModuleServiceProjects.Select(ProjectGraph.Relative)];

    public static TheoryData<string> EngineAndGatewayAssemblies =>
    [
        "Flowgent.Contracts", "Flowgent.Definitions", "Flowgent.Runtime", "Flowgent.Persistence", "Flowgent.Engine.Service", "Flowgent.Gateway",
    ];

    public static TheoryData<string> ModuleAssemblies => [.. ModuleTypes.Select(t => t.Assembly.GetName().Name!)];

    private static readonly Type[] ModuleTypes = [typeof(SupportHost), typeof(TemplateHost), typeof(FixtureHost)];

    // ── Project references ─────────────────────────────────────────────

    [Fact]
    public void Contracts_HasNoProjectReferences() =>
        Assert.Empty(ProjectGraph.DirectReferences(ProjectGraph.Engine("Flowgent.Contracts")));

    [Fact]
    public void Definitions_ReferencesOnlyContracts() =>
        Assert.Equal(
            [ProjectGraph.Relative(ProjectGraph.Engine("Flowgent.Contracts"))],
            ProjectGraph.DirectReferences(ProjectGraph.Engine("Flowgent.Definitions")).Select(ProjectGraph.Relative));

    [Theory]
    [MemberData(nameof(EngineAndGatewayProjects))]
    public void EngineAndGateway_NeverReferenceModulesOrFixtures(string project)
    {
        var references = ProjectGraph.TransitiveReferences(Path.Combine(ProjectGraph.Root, project)).Select(ProjectGraph.Relative);

        Assert.DoesNotContain(references, r => r.StartsWith("modules/", StringComparison.Ordinal) || r.Contains("/Fixtures/", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryModuleService_IsCovered() =>
        Assert.Superset(
            new HashSet<string>(["modules/support/service/Flowgent.Modules.Support.csproj", "modules/_template/service/Flowgent.Modules.Template.csproj"]),
            ProjectGraph.ModuleServiceProjects.Select(ProjectGraph.Relative).ToHashSet());

    [Theory]
    [MemberData(nameof(ModuleServiceProjects))]
    public void ModuleServices_ReferenceOnlyTheSdkContractsDefinitionsAndServiceDefaults(string project)
    {
        string[] allowed =
        [
            ProjectGraph.Sdk("Flowgent.Module.Sdk"),
            ProjectGraph.Engine("Flowgent.Contracts"),
            ProjectGraph.Engine("Flowgent.Definitions"),
            ProjectGraph.ServiceDefaults,
        ];
        string[] forbidden =
        [
            ProjectGraph.Engine("Flowgent.Runtime"),
            ProjectGraph.Engine("Flowgent.Persistence"),
            ProjectGraph.Service("Flowgent.Engine.Service"),
            ProjectGraph.Service("Flowgent.Gateway"),
            ProjectGraph.Sdk("Flowgent.Testing"),
        ];
        var path = Path.Combine(ProjectGraph.Root, project);

        Assert.All(ProjectGraph.DirectReferences(path), r => Assert.Contains(r, allowed, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(ProjectGraph.TransitiveReferences(path).Intersect(forbidden, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void SupportMcpServer_ReferencesOnlyServiceDefaultsAndTheMcpPackage()
    {
        // FEAT-003: the standalone support MCP server is intentionally NOT a module service and NOT an engine project.
        // Its only project reference is ServiceDefaults, and its only NuGet dependency is the MCP ASP.NET Core package;
        // it must never reach the Module SDK, contracts, definitions, runtime or any engine/service project.
        string[] forbidden =
        [
            ProjectGraph.Sdk("Flowgent.Module.Sdk"),
            ProjectGraph.Engine("Flowgent.Contracts"),
            ProjectGraph.Engine("Flowgent.Definitions"),
            ProjectGraph.Engine("Flowgent.Runtime"),
            ProjectGraph.Engine("Flowgent.Persistence"),
            ProjectGraph.Service("Flowgent.Engine.Service"),
            ProjectGraph.Service("Flowgent.Gateway"),
            ProjectGraph.Sdk("Flowgent.Testing"),
        ];

        Assert.Equal(
            [ProjectGraph.Relative(ProjectGraph.ServiceDefaults)],
            ProjectGraph.DirectReferences(ProjectGraph.McpSupport).Select(ProjectGraph.Relative));
        Assert.Empty(ProjectGraph.TransitiveReferences(ProjectGraph.McpSupport).Intersect(forbidden, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["ModelContextProtocol.AspNetCore"], ProjectGraph.PackageReferences(ProjectGraph.McpSupport));
    }

    // ── Compiled assemblies (NetArchTest) ──────────────────────────────

    [Theory]
    [MemberData(nameof(EngineAndGatewayAssemblies))]
    public void EngineAndGateway_HaveNoModuleTypesOrDependencies(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny("Flowgent.Modules", "Flowgent.Fixtures").GetResult();
        var moduleTypes = assembly.GetTypes().Where(t => t.Namespace is { } ns && (ns == "Flowgent.Modules" || ns.StartsWith("Flowgent.Modules.", StringComparison.Ordinal)));

        Assert.True(result.IsSuccessful, $"Types depending on modules: {string.Join(", ", result.FailingTypeNames ?? [])}");
        Assert.Empty(moduleTypes);
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Modules_DoNotDependOnEngineImplementation(string assemblyName)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName)).ShouldNot().HaveDependencyOnAny(EngineNamespaces).GetResult();

        Assert.True(result.IsSuccessful, $"Types depending on engine implementation: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [InlineData("Flowgent.Definitions")]
    [InlineData("Flowgent.Runtime")]
    public void DefinitionsAndRuntime_DoNotDependOnAspNetCore(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("Microsoft.AspNetCore").GetResult();

        Assert.True(result.IsSuccessful, $"Types depending on ASP.NET Core: {string.Join(", ", result.FailingTypeNames ?? [])}");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), r => r.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Definitions_DoesNotReferenceTheDeclarativePackage()
    {
        var assembly = Assembly.Load("Flowgent.Definitions");

        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("Microsoft.Agents.AI.Workflows.Declarative").GetResult();

        Assert.True(result.IsSuccessful, $"Definitions types depending on the declarative package: {string.Join(", ", result.FailingTypeNames ?? [])}");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), r => r.Name!.StartsWith("Microsoft.Agents.AI.Workflows.Declarative", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyTheMafSeam_ReferencesTheDeclarativePackage()
    {
        var assembly = Assembly.Load("Flowgent.Runtime");

        // Only types in Flowgent.Runtime.Maf may depend on Microsoft.Agents.AI.Workflows.Declarative; every other
        // namespace exchanges plain .NET types with the seam (AC-4, design §13).
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespace("Flowgent.Runtime")
            .And().DoNotResideInNamespace("Flowgent.Runtime.Maf")
            .ShouldNot().HaveDependencyOn("Microsoft.Agents.AI.Workflows.Declarative")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Types outside Flowgent.Runtime.Maf depending on the declarative package: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ── Source files ───────────────────────────────────────────────────

    [Fact]
    public void EngineSources_DoNotMentionSupportDomainTerms()
    {
        var pattern = new Regex(string.Join('|', SupportDomainTerms.Select(Regex.Escape)), RegexOptions.CultureInvariant);
        var offending = ProjectGraph.EngineProjects
            .Select(Path.GetDirectoryName)
            .Append(Path.Combine(ProjectGraph.Root, "backend", "protos"))
            .SelectMany(folder => Directory.EnumerateFiles(folder!, "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".proto", StringComparison.Ordinal))
            .Where(file => !Regex.IsMatch(file, @"[\\/](bin|obj)[\\/]"))
            .Where(file => pattern.IsMatch(File.ReadAllText(file)))
            .Select(ProjectGraph.Relative)
            .ToList();

        Assert.Empty(offending);
    }
}
