namespace Flowgent.Gateway;

/// <summary>
/// Gateway configuration (section <c>Gateway</c>). Routes are generated from it, so adding a module
/// only needs one entry in <see cref="Modules"/>.
/// </summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public ServiceAddressOptions Engine { get; set; } = new() { Address = "http://localhost:5081" };

    public List<ModuleRouteOptions> Modules { get; set; } = [];

    /// <summary>How long the gRPC-Web stream may stay idle before the proxy closes it.</summary>
    public TimeSpan StreamActivityTimeout { get; set; } = TimeSpan.FromHours(12);
}

public sealed class ServiceAddressOptions
{
    /// <summary>Base address, e.g. <c>http://localhost:5081</c> or a service discovery name such as <c>http://engine</c>.</summary>
    public string Address { get; set; } = "";
}

public sealed class ModuleRouteOptions
{
    /// <summary>Module id; requests to <c>/api/modules/{Id}/**</c> are forwarded to <see cref="Address"/>.</summary>
    public string Id { get; set; } = "";

    public string Address { get; set; } = "";

    /// <summary>Additional route patterns forwarded to the module, e.g. <c>/api/support/{**catch-all}</c>.</summary>
    public List<string> ExtraRoutes { get; set; } = [];
}
