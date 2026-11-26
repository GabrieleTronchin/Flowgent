namespace Flowgent.Contracts.Api;

public sealed record DefinitionErrorInfo(
    string ModuleId,
    string? File,
    string? StepId,
    int? Line,
    string Message,
    DefinitionSeverity Severity);
