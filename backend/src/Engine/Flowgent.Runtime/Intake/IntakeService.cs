namespace Flowgent.Runtime.Intake;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flowgent.Definitions.Schemas;
using Flowgent.Runtime.Modules;
using Flowgent.Runtime.Runs;

/// <summary>Outcome of an intake request.</summary>
public abstract record IntakeResult
{
    public sealed record Accepted(Guid MessageId, Guid RunId) : IntakeResult;

    public sealed record Invalid(string Message, IReadOnlyList<SchemaError> Errors) : IntakeResult;

    public sealed record NotFound(string Message) : IntakeResult;

    public sealed record Unavailable(string Message) : IntakeResult;
}

/// <summary>
/// Generic intake: validates the input against the workflow input schema, then stores the inbox message and the
/// queued run together. The inbox processor starts the run asynchronously.
/// </summary>
public sealed partial class IntakeService(IModuleCatalog catalog, IRunStore store)
{
    public const string DefaultInvalidMessage = "The input does not match the workflow input schema.";

    public async Task<IntakeResult> SubmitAsync(string workflowId, JsonNode? input, CancellationToken ct = default)
    {
        var handle = catalog.FindWorkflow(workflowId);
        if (handle is null)
        {
            return new IntakeResult.NotFound($"Workflow '{workflowId}' not found.");
        }

        if (!handle.IsAvailable)
        {
            return new IntakeResult.Unavailable($"Module '{handle.Module.Id}' is unavailable; workflow '{workflowId}' cannot accept new inputs.");
        }

        var message = handle.Workflow.InputErrorMessage ?? DefaultInvalidMessage;
        if (input is not JsonObject inputObject)
        {
            return new IntakeResult.Invalid(message, [new SchemaError("", "The input must be a JSON object.")]);
        }

        var errors = JsonSchemaValidator.Validate(handle.Definition.GetSchema(handle.Workflow.InputSchema), inputObject);
        if (errors.Count > 0)
        {
            return new IntakeResult.Invalid(message, errors);
        }

        var runId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var title = RenderTitle(handle.Workflow.TitleTemplate, inputObject);

        await store.CreateQueuedAsync(new NewRun(runId, messageId, handle.Module.Id, workflowId, handle.Workflow.Version, title, inputObject.DeepClone().AsObject()), ct);
        return new IntakeResult.Accepted(messageId, runId);
    }

    /// <summary>
    /// Renders the run title from a simple <c>{field}</c> template over the top-level input object (no Scriban). An
    /// unknown placeholder becomes empty; the title is null when no template is configured.
    /// </summary>
    internal static string? RenderTitle(string? template, JsonObject input)
    {
        if (template is null)
        {
            return null;
        }

        return FieldPlaceholder().Replace(template, match =>
        {
            var field = match.Groups[1].Value.Trim();
            return input.TryGetPropertyValue(field, out var value) && value is not null
                ? (value is JsonValue v && v.TryGetValue<string>(out var text) ? text : value.ToJsonString())
                : "";
        });
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex FieldPlaceholder();
}
