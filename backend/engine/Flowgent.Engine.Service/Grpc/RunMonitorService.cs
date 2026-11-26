namespace Flowgent.Engine.Service.Grpc;

using global::Grpc.Core;
using Flowgent.Runtime.Configuration;
using Flowgent.Runtime.Runs;
using Microsoft.Extensions.Options;

/// <summary>gRPC server stream of run transitions, filterable by run, workflow and module.</summary>
public sealed class RunMonitorService(
    RunUpdateChannel updates,
    IOptions<EngineOptions> options,
    ILogger<RunMonitorService> logger) : RunMonitor.RunMonitorBase
{
    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<RunUpdate> responseStream, ServerCallContext context)
    {
        if (!options.Value.EnableVisualization)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Visualization is disabled."));
        }

        Guid? runFilter = null;
        if (request.HasRunId)
        {
            runFilter = Guid.TryParse(request.RunId, out var runId)
                ? runId
                : throw new RpcException(new Status(StatusCode.InvalidArgument, "run_id must be a GUID."));
        }

        var workflowFilter = request.HasWorkflowId ? request.WorkflowId : null;
        var moduleFilter = request.HasModuleId ? request.ModuleId : null;
        var ct = context.CancellationToken;

        using var subscription = updates.Subscribe();
        logger.LogInformation("gRPC client subscribed to run updates (run={Run}, workflow={Workflow}, module={Module})",
            runFilter?.ToString() ?? "*", workflowFilter ?? "*", moduleFilter ?? "*");

        try
        {
            await foreach (var update in subscription.Reader.ReadAllAsync(ct))
            {
                if ((runFilter is { } run && update.RunId != run)
                    || (workflowFilter is not null && update.WorkflowId != workflowFilter)
                    || (moduleFilter is not null && update.ModuleId != moduleFilter))
                {
                    continue;
                }

                await responseStream.WriteAsync(ToMessage(update), ct);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("gRPC client disconnected");
        }
    }

    internal static RunUpdate ToMessage(Runtime.Runs.RunUpdate update)
    {
        var message = new RunUpdate
        {
            RunId = update.RunId.ToString(),
            ModuleId = update.ModuleId,
            WorkflowId = update.WorkflowId,
            StepId = update.StepId ?? "",
            Status = update.Status.ToString(),
            EventKind = update.Kind.ToString(),
            Timestamp = update.Timestamp.ToString("O"),
        };

        if (update.Detail is not null)
        {
            message.Detail = update.Detail;
        }

        if (update.Title is not null)
        {
            message.Title = update.Title;
        }

        return message;
    }
}
