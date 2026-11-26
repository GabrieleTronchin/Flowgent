namespace Flowgent.Engine.Service.Endpoints.Primitives;

/// <summary>A group of related engine API routes. Implementations are discovered by <see cref="EndpointExtensions.AddEndpoints"/>.</summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
