namespace Flowgent.Runtime.Execution;

using System.Diagnostics;
using Flowgent.Runtime.Steps;
using Microsoft.Agents.AI.Workflows;

/// <summary>
/// FEAT-002 stub. The custom-format compiler built an Agent Framework workflow from the parsed step model,
/// which no longer exists on <see cref="RunContext.Workflow"/> (now a raw-YAML <c>WorkflowDescriptor</c>).
/// The real run path is rebuilt on the MAF declarative builder in FEAT-003 (<c>MafWorkflowFactory</c>), after
/// which this type is deleted (FEAT-005). Kept only so DI and <see cref="RunOrchestrator"/> compile in the
/// interim; calling <see cref="Build"/> throws.
/// </summary>
public sealed class WorkflowCompiler(IEnumerable<IStepHandler> handlers, RunRecorder recorder)
{
    internal static readonly ActivitySource ActivitySource = new("Flowgent.Runtime");

    // Dependencies are retained so the DI registration and the handler set stay wired until FEAT-003.
    private readonly IEnumerable<IStepHandler> _handlers = handlers;
    private readonly RunRecorder _recorder = recorder;

    public Workflow Build(RunContext run)
    {
        _ = _handlers;
        _ = _recorder;
        throw new NotSupportedException(
            "The custom-format WorkflowCompiler is disabled during the MAF declarative migration (FEAT-002). " +
            "Workflow execution is rebuilt on the declarative builder in FEAT-003.");
    }
}
