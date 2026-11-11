namespace Flowgent.Definitions.Model;

using System.Text.Json.Nodes;

/// <summary><c>module.yaml</c>.</summary>
public sealed class ModuleManifest
{
    public const string FileName = "module.yaml";

    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public JsonObject? Config { get; init; }

    public List<AgentDefinition> Agents { get; init; } = [];

    public List<ToolDefinition> Tools { get; init; } = [];

    public List<WorkflowRef> Workflows { get; init; } = [];

    public List<string> Samples { get; init; } = [];
}

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

    /// <summary>Terminal steps with their success/failure outcome, used by the graph projector.</summary>
    public List<TerminalRef> Terminals { get; init; } = [];
}

/// <summary>Outcome of a workflow terminal: success or failure.</summary>
public enum WorkflowOutcome
{
    Success,
    Failure,
}

/// <summary>Manifest-side outcome metadata for one terminal step of a workflow.</summary>
public sealed class TerminalRef
{
    public required string Id { get; init; }

    public required WorkflowOutcome Outcome { get; init; }
}

public sealed class AgentDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Group { get; init; }

    public string? Role { get; init; }

    public string? Persona { get; init; }
}

public sealed class ToolDefinition
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public SchemaRef? Input { get; init; }

    public SchemaRef? Output { get; init; }

    public int? TimeoutSeconds { get; init; }
}

/// <summary>A samples file (<c>samples/*.yaml</c>).</summary>
public sealed class SamplesFile
{
    public List<SampleDefinition> Samples { get; init; } = [];
}

public sealed class SampleDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Group { get; init; }

    public string? Description { get; init; }

    public required string Workflow { get; init; }

    public JsonObject? Input { get; init; }

    public bool ExpectInvalid { get; init; }
}
