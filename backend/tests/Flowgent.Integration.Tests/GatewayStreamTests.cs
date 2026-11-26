namespace Flowgent.Integration.Tests;

using System.Collections.Concurrent;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Flowgent.Engine.Service.Grpc;
using Flowgent.Fixtures.Module;
using Flowgent.Testing;
using Microsoft.AspNetCore.TestHost;

/// <summary>
/// The dashboard's run stream: a gRPC-Web client subscribes to <c>RunMonitor.Subscribe</c> through the gateway
/// and receives the updates of the modules it asked for, while runs of other modules execute.
/// </summary>
public class GatewayStreamTests
{
    // The engine generates only the server stub; the client call is described here (same proto contract).
    private static readonly Method<SubscribeRequest, RunUpdate> SubscribeMethod = new(
        MethodType.ServerStreaming,
        "flowgent.RunMonitor",
        "Subscribe",
        Marshallers.Create(m => m.ToByteArray(), SubscribeRequest.Parser.ParseFrom),
        Marshallers.Create(m => m.ToByteArray(), RunUpdate.Parser.ParseFrom));

    [Fact]
    public async Task GrpcWebStream_ThroughTheGateway_DeliversTheFilteredModuleUpdates()
    {
        await using var platform = await IntegrationPlatform.StartAsync();
        using var channel = GrpcChannel.ForAddress("http://gateway", new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, platform.Gateway!.GetTestServer().CreateHandler()),
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var call = channel.CreateCallInvoker().AsyncServerStreamingCall(
            SubscribeMethod, null, new CallOptions(cancellationToken: timeout.Token), new SubscribeRequest { ModuleId = FixtureHost.ModuleId });

        // The subscription starts asynchronously: keep submitting runs of both modules until fixture updates arrive.
        var fixtureRuns = new ConcurrentBag<string>();
        using var stopSubmitting = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var submitter = Task.Run(async () =>
        {
            while (!stopSubmitting.IsCancellationRequested)
            {
                await platform.SubmitSupportEmailAsync("edge-out-of-scope");
                fixtureRuns.Add((await platform.SubmitFixtureAsync(FixtureHost.RulesWorkflowId, new { text = "alpha" })).ToString());
                await Task.Delay(100, CancellationToken.None);
            }
        });

        var received = new List<RunUpdate>();
        while (await call.ResponseStream.MoveNext(timeout.Token))
        {
            received.Add(call.ResponseStream.Current);
            if (call.ResponseStream.Current.EventKind == "Completed")
            {
                break;
            }
        }

        await stopSubmitting.CancelAsync();
        await submitter;

        Assert.NotEmpty(received);
        Assert.All(received, u => Assert.Equal(FixtureHost.ModuleId, u.ModuleId));
        var completed = received[^1];
        Assert.Equal(("Completed", "Completed", "routed", FixtureHost.RulesWorkflowId), (completed.EventKind, completed.Status, completed.StepId, completed.WorkflowId));
        Assert.Equal("Alpha", completed.Detail);
        Assert.Contains(completed.RunId, fixtureRuns);
    }
}
