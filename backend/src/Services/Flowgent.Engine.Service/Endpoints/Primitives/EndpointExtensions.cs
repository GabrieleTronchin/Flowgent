namespace Flowgent.Engine.Service.Endpoints.Primitives;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class EndpointExtensions
{
    /// <summary>Registers every concrete <see cref="IEndpoint"/> found in <paramref name="assembly"/>.</summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var descriptors = assembly.DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IEndpoint)))
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type));

        services.TryAddEnumerable(descriptors);
        return services;
    }

    /// <summary>Maps the routes of every registered <see cref="IEndpoint"/>.</summary>
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var endpoint in app.ServiceProvider.GetRequiredService<IEnumerable<IEndpoint>>())
        {
            endpoint.MapEndpoint(app);
        }

        return app;
    }
}
