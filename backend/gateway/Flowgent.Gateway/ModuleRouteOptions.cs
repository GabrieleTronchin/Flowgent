namespace Flowgent.Gateway;

public sealed class ModuleRouteOptions
{
    /// <summary>Module id; requests to <c>/api/modules/{Id}/**</c> are forwarded to <see cref="Address"/>.</summary>
    public string Id { get; set; } = "";

    public string Address { get; set; } = "";

    /// <summary>Additional route patterns forwarded to the module, e.g. <c>/api/support/{**catch-all}</c>.</summary>
    public List<string> ExtraRoutes { get; set; } = [];
}
