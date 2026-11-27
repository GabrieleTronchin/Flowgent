namespace Flowgent.Definitions.Model;

/// <summary>
/// Manifest-side descriptor of a workflow. MAF declarative YAML has a trigger id but no first-class
/// version/name/input-schema/terminal-outcome, so the manifest carries them (authoritative, loader-controlled).
/// </summary>
public sealed class WorkflowRef
{
    public required string Id { get; init; }

    public required string File { get; init; }

    public required int Version { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Optional simple <c>{field}</c> run-title template.</summary>
    public string? Title { get; init; }

    public required SchemaRef Input { get; init; }

    /// <summary>Optional message returned by the intake when the input fails schema validation (ported from the old
    /// workflow <c>input.errorMessage</c>; preserves the module's verbatim validation message).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Terminal steps with their success/failure outcome, used by the graph projector.</summary>
    public List<TerminalRef> Terminals { get; init; } = [];

    /// <summary>Optional demo responses file (e.g. <c>demo/joke.yaml</c>) used in Demo mode.</summary>
    public string? Demo { get; init; }
}
