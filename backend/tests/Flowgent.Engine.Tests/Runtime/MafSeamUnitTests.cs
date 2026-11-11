namespace Flowgent.Engine.Tests.Runtime;

using System.Text.Json.Nodes;
using Flowgent.Definitions.Model;
using Flowgent.Runtime.Llm;
using Flowgent.Runtime.Maf;

/// <summary>
/// FEAT-003 unit tests for the pure, host-independent seam pieces (design AC): the Demo response provider's
/// topic/rule/severity logic, the graph projector's nodes/edges/outcomes, the PowerFx JSON-string round-trip, and the
/// checkpoint <c>@Global</c> value extraction.
/// </summary>
public sealed class MafSeamUnitTests
{
    [Theory]
    [InlineData("Tell a joke about: cats", "cats")]
    [InlineData("please... Tell a joke about: tax season", "tax season")]
    public void DemoResponseProvider_JokeTopic_IsInterpolatedByteIdentically(string prompt, string topic)
    {
        var result = new DemoResponseProvider().Resolve("joke", "tell", prompt, RunStub());

        Assert.Equal($"[Demo] Why did the {topic} cross the road? To get to the other side.", result["joke"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("the tests are flaky", "QualityTestIssue")]
    [InlineData("the razor component won't render", "FrontendBug")]
    [InlineData("a null reference exception in the api", "BackendBug")]
    [InlineData("please change my billing address", "OutOfScope")]
    public void DemoResponseProvider_Classify_SelectsFirstMatchingRuleOrDefault(string prompt, string category)
    {
        var result = new DemoResponseProvider().Resolve("support-email", "classify", prompt, RunStub());

        Assert.Equal(category, result["category"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("possible sql injection", "Critical")]
    [InlineData("a security hole", "Critical")]
    [InlineData("unhandled exception on checkout", "High")]
    [InlineData("the app will crash", "High")]
    [InlineData("the button is the wrong colour", "Medium")]
    public void DemoResponseProvider_Severity_FollowsTheKeywordLadder(string text, string severity) =>
        Assert.Equal(severity, DemoResponseProvider.AssessSeverity(text));

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
    public void WorkflowProjector_ProjectsActionsToDashboardNodesAndConditionEdges()
    {
        const string yaml = """
            kind: Workflow
            trigger:
              kind: OnConversationStart
              id: proj
              actions:
                - { kind: InvokeAzureAgent, id: classify, displayName: Classify }
                - kind: ConditionGroup
                  id: branch
                  conditions:
                    - condition: =Global.ok
                      id: yes
                      actions:
                        - { kind: GotoAction, id: g1, actionId: approved }
                  elseActions:
                    - { kind: GotoAction, id: g2, actionId: rejected }
                - { kind: SendActivity, id: approved }
                - { kind: SendActivity, id: rejected }
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

        Assert.Equal("llm", graph.Nodes.Single(n => n.Id == "classify").Kind);
        Assert.Equal("rule", graph.Nodes.Single(n => n.Id == "branch").Kind);
        Assert.Equal("success", graph.Nodes.Single(n => n.Id == "approved").Outcome);
        Assert.Equal("failure", graph.Nodes.Single(n => n.Id == "rejected").Outcome);
        Assert.Contains(graph.Edges, e => e is { From: "branch", To: "approved", Label: "Global.ok" });
        Assert.Contains(graph.Edges, e => e is { From: "branch", To: "rejected", Label: null });
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

    private static Flowgent.Runtime.Execution.RunContext RunStub()
    {
        var descriptor = new WorkflowDescriptor
        {
            Id = "stub",
            Version = 1,
            Name = "Stub",
            InputSchema = new SchemaSource(new JsonObject { ["type"] = "object" }, null),
            Yaml = "kind: Workflow",
            File = "f.yaml",
            Graph = new WorkflowGraphModel("x", [], []),
        };
        // ModuleDefinition is only needed for cross-step reads this stub does not exercise; use a loaded module.
        return new Flowgent.Runtime.Execution.RunContext(Guid.NewGuid(), LoadStubModule(), descriptor, [], null, CancellationToken.None);
    }

    private static Flowgent.Definitions.ModuleDefinition LoadStubModule()
    {
        var files = new Dictionary<string, string>
        {
            ["module.yaml"] = """
                id: demo
                name: Demo
                workflows:
                  - id: seam
                    file: workflows/seam.yaml
                    version: 1
                    name: Seam
                    input: { schema: { type: object } }
                    terminals:
                      - { id: done, outcome: success }
                """,
            ["workflows/seam.yaml"] = """
                kind: Workflow
                trigger:
                  kind: OnConversationStart
                  id: seam
                  actions:
                    - { kind: SendActivity, id: done, activity: { text: [ "ok" ] } }
                """,
        };
        var package = new Flowgent.Contracts.Modules.ModulePackage("demo", Flowgent.Definitions.ModuleLoader.ComputeVersion(files), files);
        return Flowgent.Definitions.ModuleLoader.Load(package, new WorkflowProjector()).Module!;
    }
}
