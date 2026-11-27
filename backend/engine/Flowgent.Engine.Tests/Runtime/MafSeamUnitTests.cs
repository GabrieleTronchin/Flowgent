namespace Flowgent.Engine.Tests.Runtime;

using System.Text.Json.Nodes;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Core.Llm;
using Flowgent.Engine.Core.Maf.Building;
using Flowgent.Engine.Core.Maf.Execution;
using Flowgent.Engine.Core.Runs.Execution;
using Flowgent.Engine.Tests.Helpers;

/// <summary>
/// Unit tests for the pure, host-independent seam pieces: the Demo response provider over module-declared demo files
/// (extract, rules, placeholders, missing step), the graph projector's nodes/edges/outcomes, the PowerFx JSON-string
/// round-trip, and the checkpoint <c>@Global</c> value extraction.
/// </summary>
public sealed class MafSeamUnitTests
{
    // ── Demo responses declared by the module (demo/hello.yaml, demo/review.yaml of the test module) ──

    [Theory]
    [InlineData("Summarise the release notes", "[Demo] the release notes")]
    [InlineData("no marker here", "[Demo] no marker here")]
    public void DemoResponse_PatternExtract_FallsBackToTheWholePrompt(string prompt, string summary)
    {
        var module = TestModules.LoadValid();

        var result = new DemoResponseProvider().Resolve(module.Id, module.FindWorkflow("hello")!, "summarize", prompt);

        Assert.Equal(summary, result["summary"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("Subject: an URGENT fix\n\nWrite for Beta", "URGENT Beta")]
    [InlineData("Subject: a small fix\n\nWrite for Alpha", "normal Alpha")]
    public void DemoResponse_FirstMatchingRuleWins_ElseTheDefaultOutput(string prompt, string summary)
    {
        var module = TestModules.LoadValid(TestModules.AddReviewWorkflow);

        var result = new DemoResponseProvider().Resolve(module.Id, module.FindWorkflow("review")!, "write", prompt);

        Assert.Equal(summary, result["summary"]?.GetValue<string>());
    }

    [Fact]
    public void DemoResponse_UnknownStep_FailsWithWhereToAddIt()
    {
        var module = TestModules.LoadValid();

        var ex = Assert.Throws<StepFailedException>(() => new DemoResponseProvider().Resolve(module.Id, module.FindWorkflow("hello")!, "ghost", "x"));

        Assert.Equal("ghost", ex.StepId);
        Assert.Contains("No demo response is defined for step 'ghost' of workflow 'hello'", ex.Message);
        Assert.Contains("Add step 'ghost' to the demo file", ex.Message);
    }

    [Fact]
    public void DemoResponse_WorkflowWithoutDemoFile_FailsWithWhereToAddIt()
    {
        var module = TestModules.LoadValid(f =>
        {
            f.Remove("demo/hello.yaml");
            f.Replace("module.yaml", "    demo: demo/hello.yaml\n", "");
        });

        var ex = Assert.Throws<StepFailedException>(() => new DemoResponseProvider().Resolve(module.Id, module.FindWorkflow("hello")!, "summarize", "Summarise x"));

        Assert.Contains("Add a demo file to workflow 'hello'", ex.Message);
    }

    [Fact]
    public void DemoResponse_FieldExtract_ReadsALabelledValueUpToTheNextBlankLine() =>
        Assert.Equal("line one\nline two", DemoResponseProvider.Field("Subject: s\n\nBody: line one\nline two\n\nComponent: X", "Body"));

    [Fact]
    public void PowerFxConversion_RoundTripsNodesAsJsonStrings()
    {
        var node = new JsonObject { ["a"] = 1, ["b"] = "x" };

        var json = PowerFxConversion.ToJsonString(node);
        var back = PowerFxConversion.FromJsonString(json);

        Assert.Equal(1, back?["a"]?.GetValue<int>());
        Assert.Equal("x", back?["b"]?.GetValue<string>());
        Assert.Null(PowerFxConversion.FromJsonString("null"));
        Assert.Equal("plain", PowerFxConversion.FromJsonString("plain")?.GetValue<string>());
    }

    [Fact]
    public void WorkflowProjector_ProjectsSnapshotStepsAndManifestTerminals()
    {
        // The graph's logical steps are the actions that snapshot their output via a trailing
        // "<stepId>_snapshot" SetVariable (kind taken from the action with id <stepId>); terminals come from the
        // manifest. Plumbing actions (input/parse tools, detail/terminal SetVariables, ConditionGroups) are excluded.
        const string yaml = """
            kind: Workflow
            trigger:
              kind: OnConversationStart
              id: proj
              actions:
                - kind: InvokeAzureAgent
                  id: classify
                - kind: SetVariable
                  id: classify_snapshot
                  variable: Global.classify
                  value: =x
                - kind: SendActivity
                  id: approved
                - kind: SendActivity
                  id: rejected
            """;
        var reference = new WorkflowRef
        {
            Id = "proj",
            File = "workflows/proj.yaml",
            Version = 1,
            Name = "Proj",
            Input = new SchemaRef { Schema = new SchemaSource(new JsonObject { ["type"] = "object" }, null) },
            Terminals = [new TerminalRef { Id = "approved", Outcome = WorkflowOutcome.Success }, new TerminalRef { Id = "rejected", Outcome = WorkflowOutcome.Failure }],
        };

        var graph = new WorkflowProjector().Project(reference, yaml);

        Assert.Equal("classify", graph.Start);
        Assert.Equal("llm", graph.Nodes.Single(n => n.Id == "classify").Kind);
        Assert.Equal("success", graph.Nodes.Single(n => n.Id == "approved").Outcome);
        Assert.Equal("failure", graph.Nodes.Single(n => n.Id == "rejected").Outcome);
        // One step node (classify) plus the two manifest terminals; plumbing and the branch are excluded.
        Assert.Equal(3, graph.Nodes.Count);
    }

    [Fact]
    public void MafRunDriver_ExtractsGlobalValuesFromCheckpointJson()
    {
        const string checkpoint = """
            { "state": { "capture|@Global|produce": { "typeId": { "typeName": "System.String" }, "value": "src/X.cs" } } }
            """;
        var globals = new Dictionary<string, string>(StringComparer.Ordinal);

        MafRunDriver.ExtractGlobals(checkpoint, globals);

        Assert.Equal("src/X.cs", globals["produce"]);
    }

}
