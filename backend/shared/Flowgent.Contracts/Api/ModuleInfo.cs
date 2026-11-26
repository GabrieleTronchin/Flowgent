namespace Flowgent.Contracts.Api;

public sealed record ModuleInfo(
    string Id,
    string Name,
    string? Description,
    ModuleStatus Status,
    string? Version,
    IReadOnlyList<string> Workflows,
    IReadOnlyList<DefinitionErrorInfo> Errors,
    DateTimeOffset? LastLoadedAt,
    string? LastError);
