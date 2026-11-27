namespace Flowgent.Mcp.Support;
/// <summary>Raised when the application folder is malformed or does not resolve to a known dummy app.</summary>
public sealed class LocateComponentException(string message) : Exception(message);
