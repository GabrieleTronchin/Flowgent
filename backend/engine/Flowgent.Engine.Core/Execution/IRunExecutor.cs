namespace Flowgent.Engine.Core.Execution;

using Flowgent.Contracts.Runs;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Core.HumanInput;
using Flowgent.Engine.Core.Maf;
using Flowgent.Engine.Core.Modules;
using Flowgent.Engine.Core.Runs;
using Microsoft.Extensions.Logging;
using RunStatus = Flowgent.Contracts.Runs.RunStatus;

/// <summary>Executes queued runs (implemented by <see cref="RunOrchestrator"/>; faked in inbox tests).</summary>
public interface IRunExecutor
{
    Task<RunExecutionResult> ExecuteAsync(Guid runId, CancellationToken hostToken = default);
}

/// <summary>Result of executing a queued run.</summary>
/// <param name="StartError">Set when the run could not start (unknown or unavailable workflow); recorded on the inbox message.</param>
public sealed record RunExecutionResult(RunStatus Status, string? StartError = null);
