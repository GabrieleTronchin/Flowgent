namespace Flowgent.Definitions.Model;

/// <summary>
/// The per-run definition of a workflow: the raw declarative YAML fed to the MAF
/// <c>DeclarativeWorkflowBuilder</c>, plus the manifest-side metadata the dashboard and intake need
/// (extracted once at load time, not re-parsed per request). Replaces <c>WorkflowDefinition</c> as the
/// element of <see cref="ModuleDefinition.Workflows"/>.
/// </summary>
public sealed class WorkflowDescriptor
{
    /// <summary>Workflow id from the manifest <c>workflows[].id</c> (authoritative), matched to the YAML trigger id.</summary>
    public required string Id { get; init; }

    /// <summary>Workflow version from the manifest (integer).</summary>
    public required int Version { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Optional simple <c>{field}</c> run-title template (no Scriban).</summary>
    public string? TitleTemplate { get; init; }

    /// <summary>Input JSON Schema (inline or file reference), reused for intake validation.</summary>
    public required SchemaSource InputSchema { get; init; }

    /// <summary>Raw declarative YAML text, fed to <c>DeclarativeWorkflowBuilder</c> per run.</summary>
    public required string Yaml { get; init; }

    /// <summary>Source path of the workflow file, relative to the definition folder (diagnostics).</summary>
    public required string File { get; init; }

    /// <summary>Nodes/edges projected from the YAML + manifest outcome metadata at load time.</summary>
    public required WorkflowGraphModel Graph { get; init; }
}
