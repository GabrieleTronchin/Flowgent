namespace Flowgent.Engine.Tests.Definitions;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Definitions;
using Flowgent.Definitions.Model;
using Flowgent.Engine.Tests.Helpers;

/// <summary>
/// Module-envelope loading after the MAF declarative migration. The custom step-format
/// validator (DefinitionValidator) is deleted: the loader no longer parses or step-validates the workflow document
/// (unknown step kind, unreachable step, template/condition/regex, terminal-with-next, duplicate step ids, etc.).
/// Those checks are gone as a documented behavior change — a malformed declarative workflow is now reported by the
/// MAF trial build wired into the catalog (see MafSeamSmokeTests/ModuleCatalogTests), not here. What the loader still
/// enforces is the envelope: the manifest format schema, agent/tool declarations, tool/workflow-input schema files,
/// and samples against the workflow input schema.
/// </summary>
public class ModuleLoaderTests
{
    private const string WorkflowFile = "workflows/hello.yaml";
    private const string ManifestFile = "module.yaml";

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
        Assert.Contains("kind: Workflow", workflow.Yaml);
        Assert.Contains("id: summarize", workflow.Yaml);
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

    // ── Envelope checks that survive the migration ─────────────────────

    [Fact]
    public void MissingManifest_IsReported()
    {
        var result = TestModules.Load(f => f.Remove(ManifestFile));

        AssertError(result, ManifestFile, null, "missing");
    }

    [Fact]
    public void ManifestYamlSyntaxError_ReportsFileAndLine()
    {
        var result = TestModules.Load(f => f[ManifestFile] = "id: demo\nagents: [\n");

        var error = AssertError(result, ManifestFile, null, "Invalid YAML");
        Assert.NotNull(error.Line);
    }

    [Fact]
    public void MissingWorkflowFile_IsReported()
    {
        var result = TestModules.Load(f => f.Remove(WorkflowFile));

        AssertError(result, ManifestFile, null, "workflows/hello.yaml");
    }

    [Fact]
    public void DuplicateWorkflowId_IsReported()
    {
        var result = TestModules.Load(f =>
        {
            TestModules.AddReviewWorkflow(f);
            // Rename the review descriptor's id to collide with the hello workflow.
            f.Replace(ManifestFile, "- id: review", "- id: hello");
        });

        AssertError(result, "workflows/review.yaml", null, "declared more than once");
    }

    [Fact]
    public void DuplicateAgent_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(
            ManifestFile,
            "agents:\n  - id: demo_writer",
            "agents:\n  - id: demo_writer\n    name: Dup\n    group: Writers\n    role: Writer\n    persona: A duplicate.\n  - id: demo_writer"));

        AssertError(result, ManifestFile, null, "declared more than once");
    }

    [Fact]
    public void ToolWithoutModulePrefix_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(ManifestFile, "name: demo.count", "name: other.count"));

        AssertError(result, ManifestFile, null, "prefixed with the module id");
    }

    [Fact]
    public void InvalidSchemaFile_IsReported()
    {
        // count.schema.json is referenced as the demo.count tool output schema (envelope), so a broken JSON file is reported.
        var result = TestModules.Load(f => f["schemas/count.schema.json"] = "{ not json");

        AssertError(result, "schemas/count.schema.json", null, "Invalid JSON");
    }

    [Fact]
    public void MissingSchemaFile_IsReported()
    {
        var result = TestModules.Load(f => f.Remove("schemas/count.schema.json"));

        AssertError(result, ManifestFile, null, "does not exist");
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
    public void ReadPackage_ReadsFolderFilesWithForwardSlashes()
    {
        var root = Directory.CreateTempSubdirectory("flowgent-def-");
        try
        {
            // The fixture strings are raw literals, so they carry the checkout's line endings (CRLF on Windows with
            // core.autocrlf=true). Normalise to LF first, then write every file with CRLF on disk.
            var lfFiles = TestModules.Files().ToDictionary(f => f.Key, f => f.Value.ReplaceLineEndings("\n"));
            foreach (var (path, content) in lfFiles)
            {
                var full = Path.Combine(root.FullName, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content.Replace("\n", "\r\n"));
            }

            var package = ModuleLoader.ReadPackage(root.FullName);
            var result = ModuleLoader.Load(package);

            Assert.Equal("demo", package.ModuleId);
            Assert.Contains("workflows/hello.yaml", package.Files.Keys);
            Assert.All(package.Files.Values, content => Assert.DoesNotContain('\r', content));
            Assert.True(result.IsValid);
            Assert.Equal(ModuleLoader.ComputeVersion(lfFiles), package.Version);
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

    // ── Demo responses file (workflows[].demo) ───────────────────────

    private const string DemoFile = "demo/hello.yaml";

    [Fact]
    public void DemoFile_IsLoadedOntoTheWorkflow()
    {
        var module = TestModules.LoadValid();

        var step = module.FindWorkflow("hello")!.Demo!.Steps["summarize"];
        Assert.Equal(@"Summarise\s*(.+)", step.Extract["text"].Pattern);
        Assert.Equal("[Demo] {text}", step.Output!["summary"]!.GetValue<string>());
        Assert.Empty(module.Warnings);
    }

    [Fact]
    public void DemoFile_IsOptional()
    {
        var module = TestModules.LoadValid(f =>
        {
            f.Remove(DemoFile);
            f.Replace(ManifestFile, "    demo: demo/hello.yaml\n", "");
        });

        Assert.Null(module.FindWorkflow("hello")!.Demo);
    }

    [Fact]
    public void MissingDemoFile_IsReported()
    {
        var result = TestModules.Load(f => f.Remove(DemoFile));

        AssertError(result, ManifestFile, null, "The demo file 'demo/hello.yaml' does not exist");
    }

    [Fact]
    public void DemoFileFormatViolation_IsReportedWithTheStepId()
    {
        // A step needs an output or rules.
        var result = TestModules.Load(f => f[DemoFile] = "steps:\n  summarize:\n    extract:\n      text: { field: Text }\n");

        AssertError(result, DemoFile, "summarize", "");
    }

    [Fact]
    public void InvalidDemoRegex_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(DemoFile, @"'Summarise\s*(.+)'", "'Summarise ((('"));

        AssertError(result, DemoFile, "summarize", "Invalid regex in extract 'text'");
    }

    [Fact]
    public void UnknownDemoPlaceholder_IsReported()
    {
        var result = TestModules.Load(f => f.Replace(DemoFile, "\"[Demo] {text}\"", "\"[Demo] {ghost}\""));

        AssertError(result, DemoFile, "summarize", "Unknown placeholder {ghost}");
    }

    [Fact]
    public void DemoStepWithoutAgentStep_IsAWarningOnly()
    {
        var module = TestModules.LoadValid(f => f.Replace(DemoFile, "  summarize:", "  ghost_step:"));

        var warning = Assert.Single(module.Warnings);
        Assert.Equal(DemoFile, warning.File);
        Assert.Equal("ghost_step", warning.StepId);
        Assert.Contains("No agent step", warning.Message);
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
