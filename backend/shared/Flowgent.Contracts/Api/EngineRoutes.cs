namespace Flowgent.Contracts.Api;
/// <summary>Routes of the engine API (all under <see cref="Base"/>).</summary>
public static class EngineRoutes
{
    public const string Base = "/api/engine";

    public static string SubmitRun(string workflowId) => $"{Base}/workflows/{Uri.EscapeDataString(workflowId)}/runs";

    public static string Run(Guid runId) => $"{Base}/runs/{runId}";
}
