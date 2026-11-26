namespace Flowgent.Definitions;

using Flowgent.Contracts.Api;

/// <summary>Collects definition errors and warnings for one module.</summary>
internal sealed class DefinitionErrors(string moduleId)
{
    private readonly List<DefinitionErrorInfo> _items = [];

    public string ModuleId { get; set; } = moduleId;

    public IReadOnlyList<DefinitionErrorInfo> All => _items;

    public bool HasErrors => _items.Exists(e => e.Severity == DefinitionSeverity.Error);

    public void Add(string? file, string? stepId, int? line, string message) =>
        _items.Add(new DefinitionErrorInfo(ModuleId, file, stepId, line, message, DefinitionSeverity.Error));

    public void Warn(string? file, string? stepId, int? line, string message) =>
        _items.Add(new DefinitionErrorInfo(ModuleId, file, stepId, line, message, DefinitionSeverity.Warning));
}
