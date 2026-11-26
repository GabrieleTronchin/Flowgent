namespace Flowgent.Contracts.Runs;

public static class RunStatusExtensions
{
    public static IReadOnlyList<RunStatus> TerminalStatuses { get; } = [RunStatus.Completed, RunStatus.Failed, RunStatus.Cancelled];

    public static bool IsTerminal(this RunStatus status) =>
        status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled;
}
