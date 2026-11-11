namespace Flowgent.Runtime.Modules;

using Flowgent.Contracts.Api;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Configuration;
using Flowgent.Runtime.Maf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Current state of a configured module.</summary>
public sealed record ModuleState(
    string Id,
    string Url,
    ModuleStatus Status,
    ModuleDefinition? Definition,
    IReadOnlyList<DefinitionErrorInfo> Errors,
    DateTimeOffset? LastLoadedAt,
    string? LastError)
{
    public bool IsAvailable => Status == ModuleStatus.Available && Definition is not null;

    public ModuleInfo ToInfo() => new(
        Id,
        Definition?.Manifest.Name ?? Id,
        Definition?.Manifest.Description,
        Status,
        Definition?.Version,
        Definition?.Workflows.Select(w => w.Id).ToList() ?? [],
        Errors,
        LastLoadedAt,
        LastError);
}

/// <summary>A workflow descriptor and the module definition (version) it belongs to.</summary>
public sealed record MafWorkflowHandle(ModuleState Module, ModuleDefinition Definition, WorkflowDescriptor Workflow)
{
    public bool IsAvailable => Module.IsAvailable;
}

/// <summary>Modules known to the engine and their loaded definitions.</summary>
public interface IModuleCatalog
{
    IReadOnlyList<ModuleState> Modules { get; }

    ModuleState? FindModule(string moduleId);

    MafWorkflowHandle? FindWorkflow(string workflowId);
}

/// <summary>
/// Thread-safe catalog. Definitions are replaced atomically when a module publishes a new version; runs keep
/// the <see cref="ModuleDefinition"/> they started with. Conflicting modules are marked <see cref="ModuleStatus.Invalid"/>.
/// </summary>
public sealed class ModuleCatalog : IModuleCatalog
{
    private readonly Lock _lock = new();
    private readonly ILogger<ModuleCatalog> _logger;
    private readonly IWorkflowGraphProjector _projector;
    private readonly IDeclarativeDefinitionValidator _validator;
    private readonly List<string> _order = [];
    private Dictionary<string, ModuleState> _states = new(StringComparer.Ordinal);

    public ModuleCatalog(
        IOptions<EngineOptions> options,
        IWorkflowGraphProjector projector,
        IDeclarativeDefinitionValidator validator,
        ILogger<ModuleCatalog> logger)
    {
        _logger = logger;
        _projector = projector;
        _validator = validator;
        foreach (var module in options.Value.Modules.Where(m => m.Enabled))
        {
            if (_states.ContainsKey(module.Id))
            {
                _logger.LogError("Module {ModuleId} is configured more than once; the duplicate entry is ignored.", module.Id);
                continue;
            }

            _order.Add(module.Id);
            _states[module.Id] = new ModuleState(module.Id, module.Url, ModuleStatus.Loading, null, [], null, null);
        }
    }

    public IReadOnlyList<ModuleState> Modules
    {
        get
        {
            var states = _states;
            return _order.Select(id => states[id]).ToList();
        }
    }

    public ModuleState? FindModule(string moduleId) => _states.GetValueOrDefault(moduleId);

    public MafWorkflowHandle? FindWorkflow(string workflowId)
    {
        foreach (var state in Modules)
        {
            if (state.Definition?.FindWorkflow(workflowId) is { } workflow && state.Status is ModuleStatus.Available or ModuleStatus.Unavailable)
            {
                return new MafWorkflowHandle(state, state.Definition, workflow);
            }
        }

        return null;
    }

    /// <summary>Applies a fetched package: loads, validates and checks conflicts with other modules.</summary>
    public ModuleState ApplyPackage(string moduleId, ModulePackage package)
    {
        var result = ModuleLoader.Load(package, _projector);

        lock (_lock)
        {
            var current = Require(moduleId);
            var now = DateTimeOffset.UtcNow;

            if (!result.IsValid)
            {
                return Set(current with { Status = ModuleStatus.Invalid, Definition = null, Errors = result.Errors, LastLoadedAt = now, LastError = "The module definition is invalid." });
            }

            var definition = result.Module!;

            // Trial-build each declarative workflow (design §4.3); a malformed document marks the module Invalid.
            var buildErrors = _validator.Validate(moduleId, definition.Workflows);
            if (buildErrors.Count > 0)
            {
                var merged = result.Errors.Concat(buildErrors).ToList();
                return Set(current with { Status = ModuleStatus.Invalid, Definition = null, Errors = merged, LastLoadedAt = now, LastError = "The module definition is invalid." });
            }

            var conflict = FindConflict(moduleId, definition);
            if (conflict is not null)
            {
                var error = new DefinitionErrorInfo(moduleId, ModuleManifest.FileName, null, null, conflict, DefinitionSeverity.Error);
                return Set(current with { Status = ModuleStatus.Invalid, Definition = null, Errors = [error], LastLoadedAt = now, LastError = conflict });
            }

            if (current.Definition?.Version != definition.Version)
            {
                _logger.LogInformation("Module {ModuleId} loaded (version {Version}, {Count} workflow(s))", moduleId, definition.Version, definition.Workflows.Count);
            }

            return Set(current with { Status = ModuleStatus.Available, Definition = definition, Errors = result.Errors, LastLoadedAt = now, LastError = null });
        }
    }

    /// <summary>The module answered and its package did not change.</summary>
    public ModuleState ApplyNotModified(string moduleId)
    {
        lock (_lock)
        {
            var current = Require(moduleId);
            return current.Definition is null ? current : Set(current with { Status = ModuleStatus.Available, LastError = null });
        }
    }

    /// <summary>The module could not be reached; loaded definitions are kept for in-flight runs.</summary>
    public ModuleState ApplyFailure(string moduleId, string error)
    {
        lock (_lock)
        {
            var current = Require(moduleId);
            if (current.Status != ModuleStatus.Unavailable)
            {
                _logger.LogWarning("Module {ModuleId} is unavailable: {Error}", moduleId, error);
            }

            return Set(current with { Status = ModuleStatus.Unavailable, LastError = error });
        }
    }

    private string? FindConflict(string moduleId, ModuleDefinition definition)
    {
        if (definition.Id != moduleId)
        {
            return $"The module id '{definition.Id}' in {ModuleManifest.FileName} does not match the configured id '{moduleId}'.";
        }

        foreach (var other in _states.Values.Where(s => s.Id != moduleId && s.Definition is not null))
        {
            var shared = definition.Workflows.Select(w => w.Id).Intersect(other.Definition!.Workflows.Select(w => w.Id)).FirstOrDefault();
            if (shared is not null)
            {
                return $"Workflow '{shared}' is already provided by module '{other.Id}'.";
            }
        }

        return null;
    }

    private ModuleState Require(string moduleId) =>
        _states.TryGetValue(moduleId, out var state) ? state : throw new InvalidOperationException($"Module '{moduleId}' is not configured.");

    private ModuleState Set(ModuleState state)
    {
        var copy = new Dictionary<string, ModuleState>(_states, StringComparer.Ordinal) { [state.Id] = state };
        _states = copy;
        return state;
    }
}
