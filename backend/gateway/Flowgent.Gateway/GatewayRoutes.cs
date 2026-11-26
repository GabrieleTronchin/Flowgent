namespace Flowgent.Gateway;

using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

/// <summary>Builds the YARP routes and clusters from <see cref="GatewayOptions"/>.</summary>
public static partial class GatewayRoutes
{
    public const string EngineService = "engine";
    public const string ServiceMetadataKey = "service";
    public const string EngineApiPattern = "/api/engine/{**catch-all}";
    public const string RunMonitorPattern = "/flowgent.RunMonitor/{**catch-all}";

    private const string EngineCluster = "engine";
    private const string EngineStreamCluster = "engine-stream";

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build(GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireAddress(EngineService, options.Engine.Address);

        var routes = new List<RouteConfig>
        {
            Route("engine-api", EngineCluster, EngineApiPattern),
            // gRPC-Web server streaming: dedicated cluster with a long idle timeout (YARP streams without buffering).
            Route("engine-run-monitor", EngineStreamCluster, RunMonitorPattern),
        };

        var clusters = new List<ClusterConfig>
        {
            Cluster(EngineCluster, EngineService, options.Engine.Address),
            Cluster(EngineStreamCluster, EngineService, options.Engine.Address,
                new ForwarderRequestConfig { ActivityTimeout = options.StreamActivityTimeout }),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in options.Modules)
        {
            if (!ModuleIdPattern().IsMatch(module.Id))
            {
                throw new InvalidOperationException($"Invalid gateway module id '{module.Id}'. Expected lowercase letters, digits, '-' or '_'.");
            }

            if (!seen.Add(module.Id))
            {
                throw new InvalidOperationException($"Gateway module '{module.Id}' is configured more than once.");
            }

            RequireAddress(module.Id, module.Address);

            var clusterId = $"module-{module.Id}";
            clusters.Add(Cluster(clusterId, module.Id, module.Address));
            routes.Add(Route($"module-{module.Id}", clusterId, $"/api/modules/{module.Id}/{{**catch-all}}"));

            for (var i = 0; i < module.ExtraRoutes.Count; i++)
            {
                routes.Add(Route($"module-{module.Id}-extra-{i}", clusterId, module.ExtraRoutes[i]));
            }
        }

        return (routes, clusters);
    }

    private static RouteConfig Route(string id, string clusterId, string path) => new()
    {
        RouteId = id,
        ClusterId = clusterId,
        Match = new RouteMatch { Path = path },
    };

    private static ClusterConfig Cluster(string id, string service, string address, ForwarderRequestConfig? request = null) => new()
    {
        ClusterId = id,
        Destinations = new Dictionary<string, DestinationConfig> { [service] = new() { Address = address } },
        Metadata = new Dictionary<string, string> { [ServiceMetadataKey] = service },
        HttpRequest = request,
    };

    private static void RequireAddress(string service, string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Invalid gateway address for '{service}': '{address}'. Expected an absolute http(s) URL.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex ModuleIdPattern();
}
