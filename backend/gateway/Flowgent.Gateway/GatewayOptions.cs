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
