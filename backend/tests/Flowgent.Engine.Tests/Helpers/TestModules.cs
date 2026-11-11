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
              schema: { type: object, required: [words], properties: { words: { type: integer } } }
        workflows:
          - id: hello
            file: workflows/hello.yaml
            version: 1
            name: Hello
            title: "{{ input.text }}"
            input:
              schema:
                type: object
                required: [text]
                properties:
                  text: { type: string, minLength: 1 }
            terminals:
              - { id: long, outcome: success }
              - { id: short, outcome: failure }
        samples:
          - samples/hello.yaml
        """;

    public const string Workflow = """
        id: hello
        version: 1
        name: Hello
        title: "{{ input.text }}"
        input:
          schema:
            type: object
            required: [text]
            properties:
              text: { type: string, minLength: 1 }
          errorMessage: Text is required.
        start: summarize
        steps:
          - id: summarize
            kind: llm
            name: Summary
            agent: demo_writer
            temperature: 0.2
            systemPrompt: { file: prompts/summarize.md }
            prompt: "Summarise {{ input.text }}"
            output:
              schema: { file: schemas/summary.schema.json }
            demo:
              output: { summary: "[Demo] {{ input.text }}" }
            detail: "{{ steps.summarize.summary }}"
            next: count
          - id: count
            kind: tool
            name: Count
            tool: demo.count
            input: { text: "{{ steps.summarize.summary }}" }
            transitions:
              - when: steps.count.words > 3
                to: long
              - to: short
          - id: long
            kind: terminal
            name: Long
            outcome: success
            message: "{{ steps.summarize.summary }}"
            output: "{{ steps.summarize }}"
          - id: short
            kind: terminal
            name: Short
            outcome: failure
            message: Too short
        """;

    public const string SummarySchema = """
        {"type":"object","additionalProperties":false,"required":["summary"],"properties":{"summary":{"type":"string"}}}
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
    public const string ReviewWorkflow = """
        id: review
        version: 1
        name: Review
        title: "{{ input.subject }}"
        input:
          schema:
            type: object
            required: [subject]
            properties:
              subject: { type: string, minLength: 1 }
        start: route
        steps:
          - id: route
            kind: rule
            name: Route
            value: "{{ input.subject }}"
            rules:
              - match: 'alpha'
                output: { team: Alpha }
              - match: 'beta'
                output: { team: Beta }
            onMultipleMatches: { fail: "Ambiguous: both alpha and beta." }
            onNoMatch: { fail: "No team for '{{ input.subject }}'." }
            detail: "{{ steps.route.team }}"
            next: assign
          - id: assign
            kind: map
            name: Assign
            lookups:
              roleByTeam: { Alpha: Writer, Beta: Reviewer }
            outputs:
              - name: role
                lookup: roleByTeam
                key: "{{ steps.route.team }}"
                onMissing: "No role for team {{ steps.route.team }}."
              - name: label
                value: "{{ steps.route.team }}-{{ output.role }}"
            assignAgent: demo_writer
            detail: "{{ steps.assign.label }}"
            next: approval
          - id: approval
            kind: human-input
            name: Approval
            review:
              title: "Review {{ input.subject }}"
              fields:
                - { label: Team, value: "{{ steps.route.team }}" }
                - { label: Role, value: "{{ steps.assign.role }}" }
            detail: Waiting for review
            transitions:
              - when: steps.approval.decision == "approved"
                to: write
              - to: declined
          - id: write
            kind: llm
            name: Write
            prompt: "Write for {{ steps.route.team }}"
            output:
              schema: { file: schemas/summary.schema.json }
            demo:
              value: "{{ input.subject }}"
              rules:
                - match: 'urgent'
                  output: { summary: "URGENT {{ steps.route.team }}" }
              default: { summary: "normal {{ steps.route.team }}" }
            next: done
          - id: done
            kind: terminal
            name: Done
            outcome: success
            message: "{{ steps.write.summary }}"
          - id: declined
            kind: terminal
            name: Declined
            outcome: failure
            message: "{{ steps.approval.reason | object.default 'Rejected' }}"
        """;

    /// <summary>Manifest descriptor entry for <see cref="ReviewWorkflow"/> (a <c>workflows[]</c> list item).</summary>
    public const string ReviewWorkflowRef = """
          - id: review
            file: workflows/review.yaml
            version: 1
            name: Review
            title: "{{ input.subject }}"
            input:
              schema:
                type: object
                required: [subject]
                properties:
                  subject: { type: string, minLength: 1 }
            terminals:
              - { id: done, outcome: success }
              - { id: declined, outcome: failure }
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
        ["schemas/summary.schema.json"] = SummarySchema,
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

    /// <summary>Replaces text in a file (fails if the text is not found, so tests cannot silently pass).</summary>
    public static void Replace(this Dictionary<string, string> files, string file, string oldText, string newText)
    {
        if (!files[file].Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Text not found in {file}: {oldText}");
        }

        files[file] = files[file].Replace(oldText, newText, StringComparison.Ordinal);
    }
}
