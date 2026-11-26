namespace Flowgent.Engine.Core.Modules.Catalog;

using Flowgent.Contracts.Api;
using Flowgent.Definitions;
using Flowgent.Engine.Core.Configuration;
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
