namespace Flowgent.Module.Sdk;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Tool names registered with <see cref="ModuleSdkExtensions.AddModuleTool{TTool}"/>.</summary>
public sealed class ModuleToolRegistry
{
    private readonly Dictionary<string, Type> _tools = new(StringComparer.Ordinal);

    internal void Add(string name, Type type)
    {
        if (!_tools.TryAdd(name, type))
        {
            throw new InvalidOperationException($"Tool '{name}' is registered more than once.");
        }
    }

    public bool Contains(string name) => _tools.ContainsKey(name);

    public IReadOnlyCollection<string> Names => _tools.Keys;

    internal IModuleTool? Resolve(IServiceProvider services, string name) =>
        _tools.TryGetValue(name, out var type) ? (IModuleTool)services.GetRequiredService(type) : null;
}
