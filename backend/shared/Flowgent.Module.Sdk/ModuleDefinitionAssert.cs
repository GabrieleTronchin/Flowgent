// RemoveAllResilienceHandlers is experimental: the engine client must not retry intake requests
// (a retried POST could create a duplicate run).
#pragma warning disable EXTEXP0001

namespace Flowgent.Module.Sdk;
/// <summary>Assertions for module unit tests.</summary>
public static class ModuleDefinitionAssert
{
    /// <summary>Loads a definition folder and throws with every error when it is invalid.</summary>
    public static Definitions.ModuleDefinition IsValid(string definitionPath)
    {
        var result = Definitions.ModuleLoader.LoadFromDirectory(definitionPath);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("The module definition is invalid:" + Environment.NewLine +
                string.Join(Environment.NewLine, result.BlockingErrors.Select(e => $"  {e.File}:{e.Line} [{e.StepId}] {e.Message}")));
        }

        return result.Module!;
    }
}
