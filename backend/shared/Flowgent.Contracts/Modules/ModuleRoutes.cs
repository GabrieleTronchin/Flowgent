namespace Flowgent.Contracts.Modules;

/// <summary>Routes every module service exposes through the Module SDK.</summary>
public static class ModuleRoutes
{
    public const string Package = "/_module/package";
    public const string Tools = "/_module/tools";

    public static string Tool(string name) => $"{Tools}/{Uri.EscapeDataString(name)}";
}
