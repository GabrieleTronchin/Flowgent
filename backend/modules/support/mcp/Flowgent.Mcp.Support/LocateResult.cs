namespace Flowgent.Mcp.Support;
/// <summary>The result of locating a component: the module-relative <see cref="Path"/> and its <see cref="Snippet"/>.</summary>
/// <param name="Path">Relative to the support module folder, e.g. <c>dummy-apps/application-a/src/Controllers/OrderController.cs</c>.</param>
public sealed record LocateResult(bool Found, string Component, string Path, string Language, string? Snippet);
