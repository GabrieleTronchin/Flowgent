namespace Flowgent.Integration.Tests;

using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Fixtures.Module;
using Flowgent.Testing;

/// <summary>
/// The fixture service restarts with a new definition version: the engine reloads it, new runs use the new
/// version and the run already in progress completes with the version it started with.
/// </summary>
public sealed class VersionReloadTests : IAsyncLifetime
{
    private readonly string _definition = Path.Combine(Path.GetTempPath(), $"flowgent-fixture-{Guid.NewGuid():N}", "definition");

    public ValueTask InitializeAsync()
    {
        CopyDirectory(IntegrationPlatform.FixtureDefinitionFolder, _definition);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Path.GetDirectoryName(_definition)!, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task NewVersion_IsLoadedAndTheInFlightRunKeepsItsVersion()
    {
        await using var platform = await IntegrationPlatform.StartAsync(o => o.ConfigureModule(FixtureHost.ModuleId, "Flowgent:Module:DefinitionPath", _definition));
        var oldVersion = await FixtureVersionAsync(platform);
        var inFlight = await platform.SubmitFixtureFlowAsync("old run");
        await platform.WaitForHumanInputAsync(inFlight);

        // Redeploy the module with fixture-flow at version 2 and a "v2 " success-message prefix. The workflow version
        // now lives in the manifest (module.yaml) descriptor; the message prefix is the fixture-flow done_prefix action.
        var manifest = Path.Combine(_definition, "module.yaml");
        File.WriteAllText(manifest, Replace(ReadLf(manifest), "id: fixture-flow\n    file: workflows/fixture-flow.yaml\n    version: 1", "id: fixture-flow\n    file: workflows/fixture-flow.yaml\n    version: 2"));

        var file = Path.Combine(_definition, "workflows", "fixture-flow.yaml");
        File.WriteAllText(file, Replace(ReadLf(file), "variable: Local.donePrefix\n      value: =\"\"", "variable: Local.donePrefix\n      value: =\"v2 \""));
        await platform.RestartModuleAsync(FixtureHost.ModuleId);

        var newVersion = await FixtureVersionAsync(platform);
        Assert.NotEqual(oldVersion, newVersion);
        Assert.Equal(2, (await platform.GetAsync<WorkflowInfo>($"/api/engine/workflows/{FixtureHost.FlowWorkflowId}")).Version);

        // The in-flight run resumes after the reload and finishes with version 1.
        await platform.ApproveAsync(inFlight);
        var oldRun = await platform.WaitForTerminalAsync(inFlight);
        Assert.Equal((RunStatus.Completed, 1), (oldRun.Status, oldRun.WorkflowVersion));
        Assert.Equal("nur dlo tfard", oldRun.Detail);

        // A new run uses version 2.
        var newRunId = await platform.SubmitFixtureFlowAsync("new run");
        await platform.WaitForHumanInputAsync(newRunId);
        await platform.ApproveAsync(newRunId);
        var newRun = await platform.WaitForTerminalAsync(newRunId);
        Assert.Equal((RunStatus.Completed, 2), (newRun.Status, newRun.WorkflowVersion));
        Assert.Equal("v2 nur wen tfard", newRun.Detail);
    }

    private static async Task<string?> FixtureVersionAsync(PlatformHarness platform) =>
        (await platform.GetAsync<List<ModuleInfo>>("/api/engine/modules")).Single(m => m.Id == FixtureHost.ModuleId).Version;

    private static string ReadLf(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Replace(string text, string oldValue, string newValue) =>
        text.Contains(oldValue, StringComparison.Ordinal)
            ? text.Replace(oldValue, newValue, StringComparison.Ordinal)
            : throw new InvalidOperationException($"Text not found: {oldValue}");

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
