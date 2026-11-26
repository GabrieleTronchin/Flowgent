namespace Flowgent.Engine.Core.HumanInput;

using System.Collections.Concurrent;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Core.Runs;

/// <summary>Pending human inputs of all running workflows.</summary>
public sealed class HumanInputRegistry
{
    private readonly ConcurrentDictionary<Guid, PendingHumanInput> _pending = new();

    public void Register(PendingHumanInput pending) => _pending[pending.RunId] = pending;

    public void Remove(Guid runId) => _pending.TryRemove(runId, out _);

    public bool IsPending(Guid runId) => _pending.ContainsKey(runId);

    public IReadOnlyList<PendingHumanInput> GetPending(string? moduleId = null, string? workflowId = null) =>
        _pending.Values
            .Where(p => (moduleId is null || p.ModuleId == moduleId) && (workflowId is null || p.WorkflowId == workflowId))
            .OrderBy(p => p.RequestedAt)
            .ToList();

    public bool TryComplete(Guid runId, HumanDecision decision) =>
        _pending.TryGetValue(runId, out var pending) && pending.Completion.TrySetResult(decision);
}
