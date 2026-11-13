namespace Flowgent.Runtime.Execution;

using System.Text.Json.Nodes;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;

/// <summary>
/// The state of one run: input, outputs of completed steps, assigned agent and the descriptor version it runs.
/// Each run has its own context, so concurrent runs never share state.
/// </summary>
public sealed class RunContext
{
    private readonly Lock _lock = new();
    private readonly JsonObject _stepOutputs;

    public RunContext(Guid runId, ModuleDefinition module, WorkflowDescriptor workflow, JsonObject input, string? title, CancellationToken runToken)
    {
        RunId = runId;
        Module = module;
        Workflow = workflow;
        Input = input;
        Title = title;
        RunToken = runToken;
        _stepOutputs = [];
    }

    public Guid RunId { get; }

    public ModuleDefinition Module { get; }

    public WorkflowDescriptor Workflow { get; }

    public JsonObject Input { get; }

    public string? Title { get; }

    /// <summary>Cancelled on abort or host shutdown.</summary>
    public CancellationToken RunToken { get; }

    public string? AssignedAgentId { get; private set; }

    public string? CurrentStepId { get; private set; }

    /// <summary>Records the step the run is currently on (for diagnostics and failure attribution).</summary>
    public void EnterStep(string stepId)
    {
        lock (_lock)
        {
            CurrentStepId = stepId;
        }
    }

    public void SetOutput(string stepId, JsonNode? output)
    {
        lock (_lock)
        {
            _stepOutputs[stepId] = output?.DeepClone();
        }
    }

    public JsonNode? GetOutput(string stepId)
    {
        lock (_lock)
        {
            return _stepOutputs[stepId]?.DeepClone();
        }
    }

    public JsonObject SnapshotOutputs()
    {
        lock (_lock)
        {
            return _stepOutputs.DeepClone().AsObject();
        }
    }

    public void AssignAgent(string agentId)
    {
        lock (_lock)
        {
            AssignedAgentId = agentId;
        }
    }
}

/// <summary>Output yielded by terminal steps.</summary>
public sealed record RunOutcome(string StepId, WorkflowOutcome Outcome, string? Message, JsonNode? Output);

/// <summary>A step failed for an expected reason (validation, rule, tool or LLM error); the run ends as Failed.</summary>
public sealed class StepFailedException(string stepId, string message, Exception? inner = null) : Exception(message, inner)
{
    public string StepId { get; } = stepId;
}
