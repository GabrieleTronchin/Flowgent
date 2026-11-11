namespace Flowgent.Definitions;

using System.Text.Json.Nodes;
using Json.Schema;
using Flowgent.Contracts.Api;
using Flowgent.Definitions.Model;

/// <summary>
/// A loaded and validated module: manifest, workflows, samples and the resources they reference
/// (prompt texts and compiled JSON Schemas). Immutable, so runs can keep the version they started with.
/// </summary>
public sealed class ModuleDefinition
{
    private readonly IReadOnlyDictionary<string, (JsonObject Node, JsonSchema Schema)> _schemas;

    internal ModuleDefinition(
        ModuleManifest manifest,
        string version,
        IReadOnlyList<WorkflowDescriptor> workflows,
        IReadOnlyList<SampleDefinition> samples,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, (JsonObject Node, JsonSchema Schema)> schemas,
        IReadOnlyList<DefinitionErrorInfo> warnings)
    {
        Manifest = manifest;
        Version = version;
        Workflows = workflows;
        Samples = samples;
        Files = files;
        _schemas = schemas;
        Warnings = warnings;
    }

    public string Id => Manifest.Id;

    public ModuleManifest Manifest { get; }

    public string Version { get; }

    public IReadOnlyList<WorkflowDescriptor> Workflows { get; }

    public IReadOnlyList<SampleDefinition> Samples { get; }

    public IReadOnlyDictionary<string, string> Files { get; }

    public IReadOnlyList<DefinitionErrorInfo> Warnings { get; }

    /// <summary>Module configuration exposed to the workflow as <c>config.*</c>.</summary>
    public JsonObject Config => Manifest.Config ?? [];

    public WorkflowDescriptor? FindWorkflow(string id) => Workflows.FirstOrDefault(w => w.Id == id);

    public AgentDefinition? FindAgent(string id) => Manifest.Agents.FirstOrDefault(a => a.Id == id);

    public ToolDefinition? FindTool(string name) => Manifest.Tools.FirstOrDefault(t => t.Name == name);

    /// <summary>Text of an inline or file source.</summary>
    public string ResolveText(TextSource source) =>
        source.Inline ?? (Files.TryGetValue(DefinitionPaths.Normalize(source.File!), out var text)
            ? text
            : throw new InvalidOperationException($"File '{source.File}' not found in module '{Id}'."));

    public JsonSchema GetSchema(SchemaSource source) => GetEntry(source).Schema;

    public JsonObject GetSchemaNode(SchemaSource source) => GetEntry(source).Node;

    private (JsonObject Node, JsonSchema Schema) GetEntry(SchemaSource source) =>
        _schemas.TryGetValue(source.Key, out var entry)
            ? entry
            : throw new InvalidOperationException($"Schema '{source.File ?? "inline"}' was not loaded for module '{Id}'.");
}

/// <summary>Result of loading a module: the definition when valid, plus errors and warnings.</summary>
public sealed record LoadResult(ModuleDefinition? Module, IReadOnlyList<DefinitionErrorInfo> Errors)
{
    public bool IsValid => Module is not null;

    public IEnumerable<DefinitionErrorInfo> BlockingErrors => Errors.Where(e => e.Severity == DefinitionSeverity.Error);
}

/// <summary>Path rules for files inside a definition folder.</summary>
public static class DefinitionPaths
{
    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/').Trim();

    /// <summary>True for relative paths that stay inside the definition folder.</summary>
    public static bool IsSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':'))
        {
            return false;
        }

        return path.Replace('\\', '/').Split('/').All(segment => segment != "..");
    }
}
