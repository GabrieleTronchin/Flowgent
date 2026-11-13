namespace Flowgent.Engine.Tests.Helpers;

using Flowgent.Contracts.Modules;
using Flowgent.Definitions;

/// <summary>A small valid module used by definition and runtime tests; tests patch individual files.</summary>
public static class TestModules
{
    public const string Manifest = """
        id: demo
        name: Demo
        description: Test module.
        config:
          greeting: Hello
        agents:
          - id: demo_writer
            name: Writer
            group: Writers
            role: Writer
            persona: A concise writer.
        tools:
          - name: demo.count
            input:
              schema: { type: object, required: [text], properties: { text: { type: string } } }
            output:
              schema: { file: schemas/count.schema.json }
        workflows:
          - id: hello
            file: workflows/hello.yaml
            version: 1
            name: Hello
            title: "{text}"
            input:
              schema:
                type: object
                required: [text]
                properties:
                  text: { type: string, minLength: 1 }
            errorMessage: Text is required.
            terminals:
              - { id: long, outcome: success }
              - { id: short, outcome: failure }
        samples:
          - samples/hello.yaml
        """;

    // MAF declarative hello workflow: summarize (agent) -> count (demo.count tool) -> long|short terminal.
    // Demo output for (hello, summarize) is "[Demo] <text>" via DemoResponseProvider; count classifies on word count.
    public const string Workflow = """
        kind: Workflow
        trigger:
          kind: OnConversationStart
          id: hello
          actions:
            - kind: InvokeMcpTool
              id: read_input
              serverUrl: inproc://flowgent
              serverLabel: flowgent
              toolName: flowgent.input
              requireApproval: false
              arguments:
                payload: =System.LastMessage.Text
              output:
                result: Local.intake
            - kind: InvokeAzureAgent
              id: summarize
              conversationId: =System.ConversationId
              agent:
                name: summarize|demo_writer
              input:
                messages: =UserMessage(Concatenate("Summarise ", First(Local.intake).text))
              output:
                messages: Local.summarizeMsg
            - kind: InvokeMcpTool
              id: parse_summary
              serverUrl: inproc://flowgent
              serverLabel: flowgent
              toolName: flowgent.parse
              requireApproval: false
              arguments:
                payload: =MessageText(Local.summarizeMsg)
              output:
                result: Local.summaryObj
            - kind: SetVariable
              id: summary_text
              variable: Local.summary
              value: =First(Local.summaryObj).summary
            - kind: SetVariable
              id: summarize_detail
              variable: Global.summarize__detail
              value: =Local.summary
            - kind: SetVariable
              id: summarize_snapshot
              variable: Global.summarize
              value: ={ summary: Local.summary }
            - kind: InvokeMcpTool
              id: count
              serverUrl: inproc://demo
              serverLabel: demo
              toolName: demo.count
              requireApproval: false
              arguments:
                text: =Local.summary
              output:
                result: Local.countRaw
            - kind: SetVariable
              id: count_words
              variable: Local.words
              value: =First(Local.countRaw).words
            - kind: SetVariable
              id: count_snapshot
              variable: Global.count
              value: ={ words: Local.words }
            - kind: ConditionGroup
              id: after_count
              conditions:
                - condition: =Local.words > 3
                  id: long_branch
                  actions:
                    - kind: GotoAction
                      id: goto_long
                      actionId: long_final
              elseActions:
                - kind: GotoAction
                  id: goto_short
                  actionId: short_message
            - kind: SetVariable
              id: long_final
              variable: Global.__finalOutput
              value: ={ summary: Local.summary }
            - kind: SetVariable
              id: long_message
              variable: Global.__message
              value: =Local.summary
            - kind: SetVariable
              id: long_id
              variable: Global.__terminalId
              value: ="long"
            - kind: SendActivity
              id: long
              activity:
                text:
                  - "{Local.summary}"
            - kind: EndConversation
              id: end_long
            - kind: SetVariable
              id: short_message
              variable: Global.__message
              value: ="Too short"
            - kind: SetVariable
              id: short_id
              variable: Global.__terminalId
              value: ="short"
            - kind: SendActivity
              id: short
              activity:
                text:
                  - "Too short"
        """;

    public const string CountSchema = """
        {"type":"object","additionalProperties":false,"required":["words"],"properties":{"words":{"type":"integer"}}}
        """;

    public const string Samples = """
        samples:
          - id: s1
            name: Sample one
            group: Examples
            workflow: hello
            input: { text: "Agent Framework workflows" }
          - id: empty
            name: Empty
            workflow: hello
            input: { text: "" }
            expectInvalid: true
        """;

    /// <summary>
    /// A second workflow exercising rule, map, human-input and llm-with-rules demo:
    /// route (rule) → assign (map, assigns the agent) → approval (human-input) → write (llm) → done | declined.
    /// </summary>
    // MAF declarative review workflow: route (rule->PowerFx) -> assign (map, assigns demo_writer) ->
    // approval (Question+ConditionGroup) -> write (agent) -> done|declined. Routing: alpha->Alpha, beta->Beta,
    // both->fail_ambiguous, none->fail_no_team. Demo (review, write) via DemoResponseProvider (urgent/normal).
    public const string ReviewWorkflow = """"
        kind: Workflow
        trigger:
          kind: OnConversationStart
          id: review
          actions:
            - kind: InvokeMcpTool
              id: read_input
              serverUrl: inproc://flowgent
              serverLabel: flowgent
              toolName: flowgent.input
              requireApproval: false
              arguments:
                payload: =System.LastMessage.Text
              output:
                result: Local.intake
            - kind: SetVariable
              id: rt_text
              variable: Local.rtText
              value: =Lower(First(Local.intake).subject)
            - kind: SetVariable
              id: rt_a
              variable: Local.hasAlpha
              value: =Find("alpha", Local.rtText) > 0
            - kind: SetVariable
              id: rt_b
              variable: Local.hasBeta
              value: =Find("beta", Local.rtText) > 0
            - kind: ConditionGroup
              id: route
              conditions:
                - condition: =Local.hasAlpha And Local.hasBeta
                  id: both
                  actions:
                    - kind: GotoAction
                      id: goto_ambiguous
                      actionId: fail_ambiguous_message
                - condition: =Local.hasAlpha
                  id: is_alpha
                  actions:
                    - kind: SetVariable
                      id: rt_set_a
                      variable: Local.team
                      value: ="Alpha"
                - condition: =Local.hasBeta
                  id: is_beta
                  actions:
                    - kind: SetVariable
                      id: rt_set_b
                      variable: Local.team
                      value: ="Beta"
              elseActions:
                - kind: GotoAction
                  id: goto_no_team
                  actionId: fail_no_team_message
            - kind: SetVariable
              id: route_detail
              variable: Global.route__detail
              value: =Local.team
            - kind: SetVariable
              id: route_snapshot
              variable: Global.route
              value: ={ team: Local.team }
            - kind: SetVariable
              id: assign_role
              variable: Local.role
              value: =If(Local.team = "Alpha", "Writer", "Reviewer")
            - kind: SetVariable
              id: assign_label
              variable: Local.label
              value: =Concatenate(Local.team, "-", Local.role)
            - kind: SetVariable
              id: assign_agent
              variable: Global.assignedAgentId
              value: ="demo_writer"
            - kind: SetVariable
              id: assign_detail
              variable: Global.assign__detail
              value: =Local.label
            - kind: SetVariable
              id: assign_snapshot
              variable: Global.assign
              value: ={ role: Local.role, label: Local.label }
            - kind: SetVariable
              id: review_json
              variable: Local.reviewJson
              value: =Concatenate("{""title"":""Review ", First(Local.intake).subject, """,""fields"":[{""label"":""Team"",""value"":""", Local.team, """},{""label"":""Role"",""value"":""", Local.role, """}]}")
            - kind: Question
              id: approval
              property: Local.approvalRaw
              prompt:
                kind: Message
                text:
                  - "{Local.reviewJson}"
              entity:
                kind: StringPrebuiltEntity
            - kind: InvokeMcpTool
              id: parse_approval
              serverUrl: inproc://flowgent
              serverLabel: flowgent
              toolName: flowgent.parse
              requireApproval: false
              arguments:
                payload: =Local.approvalRaw
              output:
                result: Local.approvalObj
            - kind: SetVariable
              id: approval_snapshot
              variable: Global.approval
              value: =Local.approvalRaw
            - kind: ConditionGroup
              id: after_approval
              conditions:
                - condition: =Find("approved", Local.approvalRaw) > 0
                  id: approved
                  actions:
                    - kind: GotoAction
                      id: goto_write
                      actionId: write
              elseActions:
                - kind: GotoAction
                  id: goto_declined
                  actionId: declined_message
            - kind: InvokeAzureAgent
              id: write
              conversationId: =System.ConversationId
              agent:
                name: write|demo_writer
              input:
                messages: =UserMessage(Concatenate("Subject: ", First(Local.intake).subject, Char(10), Char(10), "Write for ", Local.team))
              output:
                messages: Local.writeMsg
            - kind: InvokeMcpTool
              id: parse_write
              serverUrl: inproc://flowgent
              serverLabel: flowgent
              toolName: flowgent.parse
              requireApproval: false
              arguments:
                payload: =MessageText(Local.writeMsg)
              output:
                result: Local.writeObj
            - kind: SetVariable
              id: write_detail
              variable: Global.write__detail
              value: =First(Local.writeObj).summary
            - kind: SetVariable
              id: write_snapshot
              variable: Global.write
              value: =MessageText(Local.writeMsg)
            - kind: SetVariable
              id: done_message
              variable: Global.__message
              value: =First(Local.writeObj).summary
            - kind: SetVariable
              id: done_id
              variable: Global.__terminalId
              value: ="done"
            - kind: SendActivity
              id: done
              activity:
                text:
                  - "{First(Local.writeObj).summary}"
            - kind: EndConversation
              id: end_done
            - kind: SetVariable
              id: declined_message
              variable: Global.__message
              value: =Coalesce(First(Local.approvalObj).reason, "Rejected")
            - kind: SetVariable
              id: declined_id
              variable: Global.__terminalId
              value: ="declined"
            - kind: SendActivity
              id: declined
              activity:
                text:
                  - "{Global.__message}"
            - kind: EndConversation
              id: end_declined
            - kind: SetVariable
              id: fail_ambiguous_message
              variable: Global.__message
              value: ="Ambiguous: both alpha and beta."
            - kind: SetVariable
              id: fail_ambiguous_id
              variable: Global.__terminalId
              value: ="fail_ambiguous"
            - kind: SendActivity
              id: fail_ambiguous
              activity:
                text:
                  - "Ambiguous: both alpha and beta."
            - kind: EndConversation
              id: end_fail_ambiguous
            - kind: SetVariable
              id: fail_no_team_message
              variable: Global.__message
              value: =Concatenate("No team for '", First(Local.intake).subject, "'.")
            - kind: SetVariable
              id: fail_no_team_id
              variable: Global.__terminalId
              value: ="fail_no_team"
            - kind: SendActivity
              id: fail_no_team
              activity:
                text:
                  - "{Global.__message}"
        """";

    /// <summary>Manifest descriptor entry for <see cref="ReviewWorkflow"/> (a <c>workflows[]</c> list item).</summary>
    public const string ReviewWorkflowRef = """
          - id: review
            file: workflows/review.yaml
            version: 1
            name: Review
            title: "{subject}"
            input:
              schema:
                type: object
                required: [subject]
                properties:
                  subject: { type: string, minLength: 1 }
            terminals:
              - { id: done, outcome: success }
              - { id: declined, outcome: failure }
              - { id: fail_ambiguous, outcome: failure }
              - { id: fail_no_team, outcome: failure }
        """;

    /// <summary>Adds <see cref="ReviewWorkflow"/> to the module.</summary>
    public static void AddReviewWorkflow(Dictionary<string, string> files)
    {
        files["workflows/review.yaml"] = ReviewWorkflow;
        // Insert the review descriptor as the last workflows[] item, just before the samples: key.
        // Newline-agnostic: the fixture source may be stored with CRLF.
        var manifest = files["module.yaml"].Replace("\r\n", "\n");
        var reviewRef = ReviewWorkflowRef.Replace("\r\n", "\n");
        files["module.yaml"] = manifest.Replace("samples:\n  - samples/hello.yaml", reviewRef + "\nsamples:\n  - samples/hello.yaml");
    }

    public static Dictionary<string, string> Files() => new()
    {
        ["module.yaml"] = Manifest,
        ["workflows/hello.yaml"] = Workflow,
        ["schemas/count.schema.json"] = CountSchema,
        ["prompts/summarize.md"] = "You are {{ agent.persona }}",
        ["samples/hello.yaml"] = Samples,
    };

    public static ModulePackage Package(Action<Dictionary<string, string>>? patch = null, string moduleId = "demo")
    {
        var files = Files();
        patch?.Invoke(files);
        return new ModulePackage(moduleId, ModuleLoader.ComputeVersion(files), files);
    }

    public static LoadResult Load(Action<Dictionary<string, string>>? patch = null) => ModuleLoader.Load(Package(patch));

    public static ModuleDefinition LoadValid(Action<Dictionary<string, string>>? patch = null)
    {
        var result = Load(patch);
        return result.Module ?? throw new InvalidOperationException(
            "Module is invalid: " + string.Join(" | ", result.Errors.Select(e => $"{e.File}:{e.Line} {e.StepId} {e.Message}")));
    }

    /// <summary>
    /// Replaces text in a file (fails if the text is not found, so tests cannot silently pass). Content is normalised
    /// to LF first so multi-line patches match regardless of how the fixture source is stored (CRLF on Windows); the
    /// module loader normalises line endings the same way at read time.
    /// </summary>
    public static void Replace(this Dictionary<string, string> files, string file, string oldText, string newText)
    {
        var content = files[file].Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Text not found in {file}: {oldText}");
        }

        files[file] = content.Replace(oldText, newText, StringComparison.Ordinal);
    }
}
