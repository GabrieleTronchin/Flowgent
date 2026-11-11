namespace Flowgent.Engine.Tests.Definitions;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Tests.Helpers;

public class ModuleLoaderTests
{
    private const string WorkflowFile = "workflows/hello.yaml";

    [Fact]
    public void ValidModule_IsLoaded()
    {
        var module = TestModules.LoadValid();

        Assert.Equal("demo", module.Id);
        var workflow = Assert.Single(module.Workflows);
        Assert.Equal("hello", workflow.Id);
        Assert.Equal(WorkflowFile, workflow.File);
        Assert.Equal(1, workflow.Version);
        Assert.Equal("Hello", workflow.Name);
        // The raw declarative YAML is carried verbatim; the step model is no longer parsed at load time.
        Assert.Contains("start: summarize", workflow.Yaml);
        Assert.Equal(2, module.Samples.Count);
        Assert.Equal("Hello", module.Config["greeting"]!.GetValue<string>());
    }

    [Fact]
    public void ValidModule_ResolvesFilesAndSchemas()
    {
        var module = TestModules.LoadValid();

        // Prompt files are still available via ResolveText.
        Assert.Equal("You are {{ agent.persona }}", module.ResolveText(TextSource.FromFile("prompts/summarize.md")));
        // The workflow input schema (manifest-side) and the tool schemas are compiled and cached.
        Assert.Equal("object", module.GetSchemaNode(module.Workflows[0].InputSchema)["type"]!.GetValue<string>());
        Assert.NotNull(module.GetSchema(module.Workflows[0].InputSchema));
        Assert.NotNull(module.GetSchema(module.FindTool("demo.count")!.Output!.Schema));
    }

    [Fact]
    public void Version_IsDeterministicAndChangesWithContent()
    {
        var a = TestModules.Package();
        var b = TestModules.Package();
        var c = TestModules.Package(f => f["prompts/summarize.md"] = "changed");

        Assert.Equal(a.Version, b.Version);
        Assert.NotEqual(a.Version, c.Version);
    }

    [Fact]
    public void MissingManifest_IsReported()
    {
        var result = TestModules.Load(f => f.Remove("module.yaml"));

        AssertError(result, "module.yaml", null, "missing");
    }

    [Fact]
    public void YamlSyntaxError_ReportsFileAndLine()
    {
        var result = TestModules.Load(f => f[WorkflowFile] = "id: hello\nsteps: [\n");

        var error = AssertError(result, WorkflowFile, null, "Invalid YAML");
        Assert.NotNull(error.Line);
    }

    [Fact]
    public void FormatSchemaViolation_ReportsStepAndLine()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "    temperature: 0.2", "    temperature: 0.2\n    unknownField: 1"));

        var error = AssertError(result, WorkflowFile, "summarize", "not allowed");
        Assert.Equal(19, error.Line);
    }

    [Fact]
    public void UnknownStepKind_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "kind: tool", "kind: code"));

        AssertError(result, WorkflowFile, "count", "");
    }

    [Fact]
    public void DuplicateStepIds_AreReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "- id: short", "- id: long"));

        AssertError(result, WorkflowFile, "long", "declared more than once");
    }

    [Fact]
    public void UnknownTransitionTarget_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "to: short", "to: nowhere"));

        AssertError(result, WorkflowFile, "count", "'nowhere' does not exist");
    }

    [Fact]
    public void UnknownStartStep_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "start: summarize", "start: missing"));

        AssertError(result, WorkflowFile, null, "Start step 'missing' does not exist");
    }

    [Fact]
    public void UnreachableStep_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "      - to: short", "      - to: long"));

        AssertError(result, WorkflowFile, "short", "not reachable");
    }

    [Fact]
    public void NoReachableTerminal_IsReported()
    {
        var result = TestModules.Load(f => f[WorkflowFile] = """
            id: hello
            version: 1
            name: Loop
            input: { schema: { type: object } }
            start: a
            steps:
              - id: a
                kind: map
                name: A
                outputs: [{ name: x, value: 1 }]
                next: b
              - id: b
                kind: map
                name: B
                outputs: [{ name: x, value: 1 }]
                next: a
            """);

        AssertError(result, WorkflowFile, null, "No terminal step is reachable");
    }

    [Fact]
    public void TerminalWithNext_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "    message: Too short", "    message: Too short\n    next: long"));

        AssertError(result, WorkflowFile, "short", "cannot declare");
    }

    [Fact]
    public void MissingPromptFile_IsReported()
    {
        var result = TestModules.Load(f => f.Remove("prompts/summarize.md"));

        AssertError(result, WorkflowFile, "summarize", "prompts/summarize.md");
    }

    [Fact]
    public void InvalidSchemaFile_IsReported()
    {
        var result = TestModules.Load(f => f["schemas/summary.schema.json"] = "{ not json");

        AssertError(result, "schemas/summary.schema.json", "summarize", "Invalid JSON");
    }

    [Fact]
    public void NonStrictLlmOutputSchema_IsReported()
    {
        var result = TestModules.Load(f => f["schemas/summary.schema.json"] = """{"type":"object","properties":{"summary":{"type":"string"}}}""");

        AssertError(result, WorkflowFile, "summarize", "additionalProperties: false");
    }

    [Fact]
    public void UndeclaredAgent_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "agent: demo_writer", "agent: ghost"));

        AssertError(result, WorkflowFile, "summarize", "Agent 'ghost' is not declared");
    }

    [Fact]
    public void UndeclaredTool_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "tool: demo.count", "tool: demo.missing"));

        AssertError(result, WorkflowFile, "count", "Tool 'demo.missing' is not declared");
    }

    [Fact]
    public void ToolWithoutModulePrefix_IsReported()
    {
        var result = TestModules.Load(f =>
        {
            f.Replace("module.yaml", "name: demo.count", "name: other.count");
            f.Replace(WorkflowFile, "tool: demo.count", "tool: other.count");
        });

        AssertError(result, "module.yaml", null, "prefixed with the module id");
    }

    [Fact]
    public void InvalidTemplate_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "detail: \"{{ steps.summarize.summary }}\"", "detail: \"{{ steps. }}\""));

        AssertError(result, WorkflowFile, "summarize", "Invalid detail template");
    }

    [Fact]
    public void InvalidCondition_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "when: steps.count.words > 3", "when: steps.count.words >"));

        AssertError(result, WorkflowFile, "count", "Invalid transition condition");
    }

    [Fact]
    public void UnknownStepReference_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "message: \"{{ steps.summarize.summary }}\"", "message: \"{{ steps.ghost.summary }}\""));

        AssertError(result, WorkflowFile, "long", "Unknown step 'steps.ghost'");
    }

    [Fact]
    public void InvalidRegex_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(
            WorkflowFile,
            "    kind: tool\n    name: Count\n    tool: demo.count\n    input: { text: \"{{ steps.summarize.summary }}\" }",
            "    kind: rule\n    name: Count\n    value: \"{{ input.text }}\"\n    rules:\n      - match: '([a-'\n        output: { words: 5 }"));

        AssertError(result, WorkflowFile, "count", "Invalid regular expression");
    }

    [Fact]
    public void PathTraversal_IsRejected()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "file: prompts/summarize.md", "file: ../../secrets.txt"));

        AssertError(result, WorkflowFile, "summarize", "does not exist");
    }

    [Fact]
    public void SampleNotMatchingInputSchema_IsReported()
    {
        var result = TestModules.Load(f => f.Replace("samples/hello.yaml", "input: { text: \"Agent Framework workflows\" }", "input: { other: 1 }"));

        AssertError(result, "samples/hello.yaml", null, "Sample 's1' does not match");
    }

    [Fact]
    public void SampleWithUnknownWorkflow_IsReported()
    {
        var result = TestModules.Load(f => f.Replace("samples/hello.yaml", "workflow: hello\n    input: { text: \"Agent", "workflow: ghost\n    input: { text: \"Agent"));

        AssertError(result, "samples/hello.yaml", null, "unknown workflow 'ghost'");
    }

    [Fact]
    public void LlmStepWithoutDemo_IsAWarningOnly()
    {
        var result = TestModules.Load(f => f.Replace(WorkflowFile, "    demo:\n      output: { summary: \"[Demo] {{ input.text }}\" }\n", ""));

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Errors);
        Assert.Equal(DefinitionSeverity.Warning, warning.Severity);
        Assert.Equal("summarize", warning.StepId);
    }

    [Fact]
    public void ReadPackage_ReadsFolderFilesWithForwardSlashes()
    {
        var root = Directory.CreateTempSubdirectory("flowgent-def-");
        try
        {
            foreach (var (path, content) in TestModules.Files())
            {
                var full = Path.Combine(root.FullName, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content.Replace("\n", "\r\n"));
            }

            var package = ModuleLoader.ReadPackage(root.FullName);
            var result = ModuleLoader.Load(package);

            Assert.Equal("demo", package.ModuleId);
            Assert.Contains("workflows/hello.yaml", package.Files.Keys);
            Assert.DoesNotContain('\r', package.Files["module.yaml"]);
            Assert.True(result.IsValid);
            Assert.Equal(TestModules.Package().Version, package.Version);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void InlineSchemas_AreSupported()
    {
        // An inline workflow input schema (in the manifest) is compiled and cached like a file reference.
        // Newline-agnostic (the fixture source may be stored with CRLF): normalise, then collapse the workflow
        // input `schema:` multi-line block onto a single inline line.
        const string block =
            "      schema:\n" +
            "        type: object\n" +
            "        required: [text]\n" +
            "        properties:\n" +
            "          text: { type: string, minLength: 1 }";
        const string inline =
            "      schema: { type: object, additionalProperties: false, required: [text], properties: { text: { type: string } } }";
        var module = TestModules.LoadValid(f => f["module.yaml"] = f["module.yaml"].Replace("\r\n", "\n").Replace(block, inline));

        var input = module.Workflows[0].InputSchema;
        Assert.Null(input.File);
        Assert.Equal(JsonValue.Create(false)!.ToJsonString(), module.GetSchemaNode(input)["additionalProperties"]!.ToJsonString());
    }

    private static DefinitionErrorInfo AssertError(LoadResult result, string file, string? stepId, string messagePart)
    {
        Assert.False(result.IsValid, "Expected the module to be invalid.");
        var errors = result.BlockingErrors.ToList();
        var match = errors.FirstOrDefault(e => e.File == file && (stepId is null || e.StepId == stepId) && e.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase));
        Assert.True(match is not null,
            $"No error for {file}/{stepId} containing '{messagePart}'. Errors: {string.Join(" | ", errors.Select(e => $"{e.File}:{e.Line} [{e.StepId}] {e.Message}"))}");
        Assert.Equal("demo", match!.ModuleId);
        return match;
    }
}
