namespace Flowgent.Gateway.Tests;

public class GatewayRoutesTests
{
    [Fact]
    public void Build_CreatesEngineRoutesAndOneClusterPerModule()
    {
        var options = new GatewayOptions
        {
            Engine = new() { Address = "http://engine" },
            Modules =
            [
                new() { Id = "support", Address = "http://support", ExtraRoutes = ["/api/support/{**catch-all}"] },
                new() { Id = "food", Address = "http://food" },
            ],
        };

        var (routes, clusters) = GatewayRoutes.Build(options);

        Assert.Contains(routes, r => r.Match.Path == GatewayRoutes.EngineApiPattern && r.ClusterId == "engine");
        Assert.Contains(routes, r => r.Match.Path == GatewayRoutes.RunMonitorPattern && r.ClusterId == "engine-stream");
        Assert.Contains(routes, r => r.Match.Path == "/api/modules/support/{**catch-all}" && r.ClusterId == "module-support");
        Assert.Contains(routes, r => r.Match.Path == "/api/support/{**catch-all}" && r.ClusterId == "module-support");
        Assert.Contains(routes, r => r.Match.Path == "/api/modules/food/{**catch-all}" && r.ClusterId == "module-food");
        Assert.Equal(["engine", "engine-stream", "module-support", "module-food"], clusters.Select(c => c.ClusterId));
        Assert.Equal("http://support", clusters.Single(c => c.ClusterId == "module-support").Destinations!.Single().Value.Address);
    }

    [Fact]
    public void Build_StreamClusterHasLongActivityTimeout()
    {
        var (_, clusters) = GatewayRoutes.Build(new GatewayOptions { StreamActivityTimeout = TimeSpan.FromHours(2) });

        Assert.Equal(TimeSpan.FromHours(2), clusters.Single(c => c.ClusterId == "engine-stream").HttpRequest!.ActivityTimeout);
    }

    [Theory]
    [InlineData("Support")]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("a/b")]
    public void Build_RejectsInvalidModuleIds(string id)
    {
        var options = new GatewayOptions { Modules = [new() { Id = id, Address = "http://x" }] };

        Assert.Throws<InvalidOperationException>(() => GatewayRoutes.Build(options));
    }

    [Fact]
    public void Build_RejectsDuplicateModules()
    {
        var options = new GatewayOptions
        {
            Modules = [new() { Id = "a", Address = "http://a" }, new() { Id = "a", Address = "http://b" }],
        };

        var ex = Assert.Throws<InvalidOperationException>(() => GatewayRoutes.Build(options));
        Assert.Contains("more than once", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost:5082")]
    [InlineData("ftp://support")]
    public void Build_RejectsInvalidAddresses(string address)
    {
        var options = new GatewayOptions { Modules = [new() { Id = "a", Address = address }] };

        Assert.Throws<InvalidOperationException>(() => GatewayRoutes.Build(options));
    }
}
