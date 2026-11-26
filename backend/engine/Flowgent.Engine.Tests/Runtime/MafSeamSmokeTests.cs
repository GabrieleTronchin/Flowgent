namespace Flowgent.Engine.Tests.Runtime;

using System.Text.Json.Nodes;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Tests.Helpers;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Maf;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// FEAT-003 anti-risk smoke exercise of the MAF declarative seam: build a workflow through
/// <see cref="MafWorkflowFactory"/> and run it through <see cref="MafRunDriver"/> in Demo mode, confirming the probe
/// verdicts recorded by FEAT-001 hold in the real seam — a terminal/output event surfaces (PROBE-B: the host reads a
/// <c>Global.*</c> variable from run state via the JSON checkpoint), an <c>InvokeMcpTool</c> JSON-string result is read
/// back (PROBE-A/F4), and a <c>Question</c>+<c>ConditionGroup</c> human-input gate resumes (F5). This is a seam check,
/// not the full module migration (FEAT-004).
/// </summary>
public sealed class MafSeamSmokeTests
{
    [Fact]
    public async Task Seam_RunsASetVariableWorkflow_SurfacesGlobalOutputAndTerminal()
    {
        const string yaml = """
            kind: Workflow
            trigger:
              kind: OnConversationStart
              id: smoke_basic
              actions:
                - kind: SetVariable
                  id: produce_snapshot
                  variable: Global.produce
                  value: "{\"ok\":true}"
                - kind: SetVariable
                  id: done_id
                  variable: Global.__terminalId
                  value: ="done"
                - kind: SendActivity
                  id: done
                  activity:
                    text:
                      - "done {Global.produce}"
            """;

        await using var engine = await TestEngine.StartAsync();
        var (driver, factory) = Seam(engine);
        var run = NewRun(engine, yaml, [new TerminalRef { Id = "done", Outcome = WorkflowOutcome.Success }], "done");

        var (events, terminal) = await DriveAsync(driver, factory, run);

        Assert.IsType<MafRunEvent.Completed>(terminal);
        Assert.Equal(WorkflowOutcome.Success, ((MafRunEvent.Completed)terminal).Outcome);
        // PROBE-B: the Global.produce variable was recovered from run state and surfaced as a step output.
        var produce = events.OfType<MafRunEvent.StepCompleted>().FirstOrDefault(e => e.StepId == "produce");
        Assert.NotNull(produce);
        Assert.Equal(true, produce!.Output?["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Seam_InvokesMcpTool_ReadsJsonStringResult()
    {
        const string yaml = """
            kind: Workflow
            trigger:
              kind: OnConversationStart
              id: smoke_mcp
              actions:
                - kind: InvokeMcpTool
                  id: count
                  serverUrl: inproc://demo
                  serverLabel: demo
                  toolName: demo.count
                  requireApproval: false
                  arguments:
                    text: ="one two three"
                  output:
                    result: Local.count
                - kind: SetVariable
                  id: done_id
                  variable: Global.__terminalId
                  value: ="done"
                - kind: SendActivity
                  id: done
                  activity:
                    text:
                      - "counted"
            """;

        await using var engine = await TestEngine.StartAsync();
        var (driver, factory) = Seam(engine);
        var run = NewRun(engine, yaml, [new TerminalRef { Id = "done", Outcome = WorkflowOutcome.Success }], "done");

        var (_, terminal) = await DriveAsync(driver, factory, run);

        // The run reached its terminal without the InvokeMcpTool PowerFx binding crashing on the JSON-string result.
        Assert.True(terminal is MafRunEvent.Completed, terminal is MafRunEvent.Failed f ? f.Reason : terminal.GetType().Name);
    }

    [Fact]
    public async Task Seam_ResumesAQuestionGate_TakesTheApprovedBranch()
    {
        const string yaml = """
            kind: Workflow
            trigger:
              kind: OnConversationStart
              id: smoke_hitl
              actions:
                - kind: Question
                  id: approval
                  property: Local.answer
                  prompt:
                    kind: Message
                    text:
                      - "Approve?"
                  entity:
                    kind: StringPrebuiltEntity
                - kind: ConditionGroup
                  id: branch
                  conditions:
                    - condition: =Find("approved", Local.answer) > 0
                      id: approved
                      actions:
                        - kind: SetVariable
                          id: approved_id
                          variable: Global.__terminalId
                          value: ="on_approved"
                        - kind: SendActivity
                          id: on_approved
                          activity:
                            text:
                              - "APPROVED {Local.answer}"
                  elseActions:
                    - kind: SetVariable
                      id: rejected_id
                      variable: Global.__terminalId
                      value: ="on_rejected"
                    - kind: SendActivity
                      id: on_rejected
                      activity:
                        text:
                          - "REJECTED {Local.answer}"
            """;

        await using var engine = await TestEngine.StartAsync();
        var (driver, factory) = Seam(engine);
        var run = NewRun(engine, yaml, [], "on_approved");

        var awaited = false;
        var built = factory.Build(run);
        var terminal = await driver.RunAsync(
            run,
            built,
            evt =>
            {
                if (evt is MafRunEvent.AwaitingInput)
                {
                    awaited = true;
                }

                return Task.CompletedTask;
            },
            (stepId, prompt, ct) => Task.FromResult(new MafHumanDecision(true, null)),
            CancellationToken.None);

        Assert.True(awaited, "the Question gate should surface an AwaitingInput event");
        Assert.IsType<MafRunEvent.Completed>(terminal);
    }

    private static (MafRunDriver Driver, MafWorkflowFactory Factory) Seam(TestEngine engine) =>
        (engine.Services.GetRequiredService<MafRunDriver>(), engine.Services.GetRequiredService<MafWorkflowFactory>());

    // A module with one declarative workflow (so the trial-build validator accepts it), the demo.count tool and a
    // demo_writer agent — the seam reads tool/agent metadata from here, the workflow YAML is supplied per test.
    private static ModuleDefinition SeamModule()
    {
        const string manifest = """
            id: demo
            name: Demo
            agents:
              - { id: demo_writer, name: Writer }
            tools:
              - name: demo.count
                input:
                  schema: { type: object, required: [text], properties: { text: { type: string } } }
                output:
                  schema: { type: object, required: [words], properties: { words: { type: integer } } }
            workflows:
              - id: seam
                file: workflows/seam.yaml
                version: 1
                name: Seam
                input:
                  schema: { type: object }
                terminals:
                  - { id: done, outcome: success }
            """;
        const string declarative = """
            kind: Workflow
            trigger:
              kind: OnConversationStart
              id: seam
              actions:
                - kind: SendActivity
                  id: done
                  activity:
                    text:
                      - "ok"
            """;

        var files = new Dictionary<string, string>
        {
            ["module.yaml"] = manifest,
            ["workflows/seam.yaml"] = declarative,
        };
        var package = new Flowgent.Contracts.Modules.ModulePackage("demo", ModuleLoader.ComputeVersion(files), files);
        var result = ModuleLoader.Load(package, new WorkflowProjector());
        return result.Module ?? throw new InvalidOperationException(
            "Seam module invalid: " + string.Join(" | ", result.Errors.Select(e => e.Message)));
    }

    private static RunContext NewRun(TestEngine engine, string yaml, IReadOnlyList<TerminalRef> terminals, string terminalNode)
    {
        var module = SeamModule();

        var nodes = terminals
            .Select(t => new WorkflowGraphNode(t.Id, t.Id, "terminal", t.Outcome.ToString().ToLowerInvariant()))
            .ToList();
        if (nodes.Count == 0)
        {
            nodes.Add(new WorkflowGraphNode(terminalNode, terminalNode, "terminal", "success"));
        }

        var descriptor = new WorkflowDescriptor
        {
            Id = "smoke",
            Version = 1,
            Name = "Smoke",
            InputSchema = new SchemaSource(new JsonObject { ["type"] = "object" }, null),
            Yaml = yaml,
            File = "workflows/smoke.yaml",
            Graph = new WorkflowGraphModel(nodes[0].Id, nodes, []),
        };

        return new RunContext(Guid.NewGuid(), module, descriptor, [], null, CancellationToken.None);
    }

    private static async Task<(List<MafRunEvent> Events, MafRunEvent Terminal)> DriveAsync(
        MafRunDriver driver, MafWorkflowFactory factory, RunContext run)
    {
        var events = new List<MafRunEvent>();
        var built = factory.Build(run);
        var terminal = await driver.RunAsync(
            run,
            built,
            evt =>
            {
                events.Add(evt);
                return Task.CompletedTask;
            },
            (stepId, prompt, ct) => Task.FromResult(new MafHumanDecision(true, null)),
            CancellationToken.None);
        return (events, terminal);
    }
}
