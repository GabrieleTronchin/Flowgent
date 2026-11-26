namespace Flowgent.Contracts.Modules;

/// <summary>
/// All files of a module definition folder (paths relative to <c>definition/</c>, '/' separated) and the
/// version of the package (SHA-256 of the files). Served by <c>GET /_module/package</c>.
/// </summary>
public sealed record ModulePackage(string ModuleId, string Version, IReadOnlyDictionary<string, string> Files);
