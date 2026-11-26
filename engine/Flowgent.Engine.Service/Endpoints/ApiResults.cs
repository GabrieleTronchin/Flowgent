namespace Flowgent.Engine.Service.Endpoints;

/// <summary>Problem-details responses shared by the engine endpoints.</summary>
internal static class ApiResults
{
    public static IResult NotFound(string message) =>
        Results.Problem(title: "Not found", detail: message, statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string message) =>
        Results.Problem(title: "Conflict", detail: message, statusCode: StatusCodes.Status409Conflict);
}
