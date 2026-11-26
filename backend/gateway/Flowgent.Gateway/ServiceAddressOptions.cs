namespace Flowgent.Gateway;

public sealed class ServiceAddressOptions
{
    /// <summary>Base address, e.g. <c>http://localhost:5081</c> or a service discovery name such as <c>http://engine</c>.</summary>
    public string Address { get; set; } = "";
}
