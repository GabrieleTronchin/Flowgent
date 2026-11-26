namespace Flowgent.Module.Sdk;
/// <summary>Submits inputs to the engine's generic intake (<c>POST /api/engine/workflows/{id}/runs</c>).</summary>
public interface IEngineClient
{
    Task<EngineSubmitResult> SubmitAsync(string workflowId, object input, CancellationToken ct = default);
}
