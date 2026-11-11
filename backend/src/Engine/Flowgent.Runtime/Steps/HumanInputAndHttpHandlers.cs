namespace Flowgent.Runtime.Steps;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Definitions.Expressions;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.HumanInput;
using Flowgent.Runtime.Modules;
using Microsoft.Extensions.Configuration;

/// <summary>
/// <c>human-input</c> steps: publish the review payload, pause the run in AwaitingInput and continue with
/// <c>{ decision: approved | rejected, reason }</c>. Abort and shutdown release the wait.
/// </summary>
internal sealed class HumanInputStepHandler(HumanInputRegistry registry, RunRecorder recorder) : IStepHandler
{
    public string Kind => StepKinds.HumanInput;

    public bool RecordsStart => true;

    public async Task<JsonNode?> ExecuteAsync(StepDefinition step, RunContext run, CancellationToken ct)
    {
        var human = (HumanInputStep)step;
        var templates = run.Templates;
        var scope = run.CreateScope();

        var review = new ReviewPayload(
            templates.Render(human.Review.Title, scope),
            human.Review.Fields.Select(f => new ReviewField(f.Label, templates.Render(f.Value, scope))).ToList());
        var detail = human.Detail is null ? null : templates.Render(human.Detail, scope);

        await recorder.AwaitingInputAsync(run, step.Id, detail);

        var completion = new TaskCompletionSource<HumanDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register(new PendingHumanInput(run.RunId, run.Module.Id, run.Workflow.Id, step.Id, run.Title, review, DateTimeOffset.UtcNow, completion));

        HumanDecision decision;
        try
        {
            decision = await completion.Task.WaitAsync(ct);
        }
        finally
        {
            registry.Remove(run.RunId);
        }

        await recorder.InputReceivedAsync(run, step.Id, decision.Reason is null ? decision.Value : $"{decision.Value}: {decision.Reason}");
        return new JsonObject { ["decision"] = decision.Value, ["reason"] = decision.Reason };
    }
}

/// <summary><c>http</c> steps: call an external API and map its JSON response to the step output.</summary>
internal sealed class HttpStepHandler(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IStepHandler
{
    public const string HttpClientName = "flowgent-http";

    public string Kind => StepKinds.Http;

    public bool RecordsStart => true;

    public async Task<JsonNode?> ExecuteAsync(StepDefinition step, RunContext run, CancellationToken ct)
    {
        var http = (HttpStep)step;
        var templates = run.Templates;
        var scope = run.CreateScope();

        var url = templates.Render(http.Url, scope);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new StepFailedException(step.Id, $"HTTP step '{step.Id}' has an invalid URL.");
        }

        using var request = new HttpRequestMessage(new HttpMethod(http.Method), uri);
        foreach (var (name, header) in http.Headers ?? [])
        {
            var value = header.Secret is { } secret
                ? configuration[$"Flowgent:Secrets:{run.Module.Id}:{secret}"]
                    ?? throw new StepFailedException(step.Id, $"Secret '{secret}' for HTTP step '{step.Id}' is not configured.")
                : templates.Render(header.Literal ?? "", scope);
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (http.Body is not null)
        {
            var body = templates.EvaluateValue(http.Body, scope);
            request.Content = new StringContent(body?.ToJsonString() ?? "null", Encoding.UTF8, "application/json");
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var timeout = TimeSpan.FromSeconds(http.TimeoutSeconds ?? HttpStep.DefaultTimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        string text;
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, timeoutCts.Token);
            text = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new StepFailedException(step.Id, $"HTTP step '{step.Id}' failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StepFailedException(step.Id, $"HTTP step '{step.Id}' timed out after {timeout.TotalSeconds:0.#}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new StepFailedException(step.Id, $"HTTP step '{step.Id}' failed: {ex.Message}", ex);
        }

        JsonNode? output;
        try
        {
            output = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new StepFailedException(step.Id, $"HTTP step '{step.Id}' returned a response that is not JSON.");
        }

        if (http.Output is not null)
        {
            StepSchemas.Validate(run.Module, http.Output.Schema, output, step.Id, $"Response of HTTP step '{step.Id}'");
        }

        return output;
    }
}

/// <summary>Evaluates step templates and turns template errors into step failures.</summary>
internal static class StepTemplates
{
    public static T Guard<T>(string stepId, Func<T> evaluate)
    {
        try
        {
            return evaluate();
        }
        catch (ExpressionException ex)
        {
            throw new StepFailedException(stepId, $"Template error in step '{stepId}': {ex.Message}", ex);
        }
    }
}
