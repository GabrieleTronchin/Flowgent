namespace Flowgent.Module.Sdk;

using Flowgent.Contracts.Api;

/// <summary>Result of submitting an input to the engine intake.</summary>
public abstract record EngineSubmitResult
{
    public sealed record Accepted(IntakeAccepted Ids) : EngineSubmitResult;

    public sealed record Invalid(string Message) : EngineSubmitResult;

    public sealed record NotFound(string Message) : EngineSubmitResult;

    public sealed record Unavailable(string Message) : EngineSubmitResult;
}
