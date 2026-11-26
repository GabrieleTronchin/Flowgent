namespace Flowgent.Definitions;

using Flowgent.Contracts.Api;

/// <summary>Result of loading a module: the definition when valid, plus errors and warnings.</summary>
public sealed record LoadResult(ModuleDefinition? Module, IReadOnlyList<DefinitionErrorInfo> Errors)
{
    public bool IsValid => Module is not null;

    public IEnumerable<DefinitionErrorInfo> BlockingErrors => Errors.Where(e => e.Severity == DefinitionSeverity.Error);
}
