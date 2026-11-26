namespace Flowgent.Runtime.Agents;

using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Runtime.Modules;
using Flowgent.Runtime.Runs;

/// <summary>Agents declared by the loaded modules, Working while assigned to a non-terminal run.</summary>
public sealed class AgentStatusService(IModuleCatalog catalog, IRunStore store)
{
    public const string Idle = "Idle";
    public const string Working = "Working";

    public async Task<IReadOnlyList<AgentInfo>> GetAgentsAsync(string? moduleId = null, CancellationToken ct = default)
    {
        var activeRuns = (await store.ListAsync(new RunQuery(ModuleId: moduleId, Limit: int.MaxValue), ct))
            .Where(r => !r.Status.IsTerminal() && r.AssignedAgentId is not null)
            .GroupBy(r => (r.ModuleId, r.AssignedAgentId!))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.LastUpdated).First());

        return catalog.Modules
            .Where(m => m.Definition is not null && (moduleId is null || m.Id == moduleId))
            .SelectMany(m => m.Definition!.Manifest.Agents.Select(agent =>
            {
                var working = activeRuns.TryGetValue((m.Id, agent.Id), out var run);
                return new AgentInfo(
                    agent.Id,
                    m.Id,
                    agent.Name,
                    agent.Group,
                    agent.Role,
                    working ? Working : Idle,
                    run?.Id,
                    run?.Title,
                    run?.CurrentStepId);
            }))
            .ToList();
    }
}
