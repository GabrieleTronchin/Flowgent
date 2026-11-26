namespace Flowgent.Module.Sdk;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flowgent.Contracts.Api;

/// <summary>Result of submitting an input to the engine intake.</summary>
public abstract record EngineSubmitResult
{
    public sealed record Accepted(IntakeAccepted Ids) : EngineSubmitResult;

    public sealed record Invalid(string Message) : EngineSubmitResult;

    public sealed record NotFound(string Message) : EngineSubmitResult;

    public sealed record Unavailable(string Message) : EngineSubmitResult;
}

/// <summary>Submits inputs to the engine's generic intake (<c>POST /api/engine/workflows/{id}/runs</c>).</summary>
public interface IEngineClient
{
    Task<EngineSubmitResult> SubmitAsync(string workflowId, object input, CancellationToken ct = default);
}

internal sealed class EngineClient(HttpClient httpClient) : IEngineClient
{
    public const string HttpClientName = "flowgent-engine";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EngineSubmitResult> SubmitAsync(string workflowId, object input, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(EngineRoutes.SubmitRun(workflowId), input, JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            return new EngineSubmitResult.Unavailable($"The engine is unavailable: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new EngineSubmitResult.Unavailable("The engine did not answer in time.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var ids = await response.Content.ReadFromJsonAsync<IntakeAccepted>(JsonOptions, ct);
                return ids is null ? new EngineSubmitResult.Unavailable("The engine returned an empty response.") : new EngineSubmitResult.Accepted(ids);
            }

            var detail = await ReadDetailAsync(response, ct);
            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => new EngineSubmitResult.Invalid(detail ?? "The input is invalid."),
                HttpStatusCode.NotFound => new EngineSubmitResult.NotFound(detail ?? $"Workflow '{workflowId}' not found."),
                _ => new EngineSubmitResult.Unavailable(detail ?? $"The engine returned {(int)response.StatusCode}."),
            };
        }
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
