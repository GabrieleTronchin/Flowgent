namespace Flowgent.Definitions;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Schemas;
using Flowgent.Definitions.Yaml;
using Json.Schema;

/// <summary>
/// Loads a module definition (manifest, workflows, samples, prompts, schemas) from a package or a folder.
/// Keeps the envelope pipeline (manifest format schema, reference checks, schema-file existence, samples
/// validation) and carries each workflow's raw declarative YAML in a <see cref="WorkflowDescriptor"/>. The
/// declarative workflow document itself is not parsed or step-validated here (that is the MAF builder's job).
/// </summary>
public static class ModuleLoader
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly string[] DefinitionExtensions = [".yaml", ".yml", ".json", ".md", ".txt"];

    /// <summary>Reads every definition file of a folder into a package (version = SHA-256 of the files).</summary>
    public static ModulePackage ReadPackage(string definitionPath, string? moduleId = null)
    {
        var root = Path.GetFullPath(definitionPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Module definition folder not found: {root}");
        }

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => DefinitionExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToDictionary(
                f => Path.GetRelativePath(root, f).Replace('\\', '/'),
                f => File.ReadAllText(f).Replace("\r\n", "\n"),
                StringComparer.Ordinal);

        var id = moduleId ?? TryReadModuleId(files) ?? Path.GetFileName(Path.GetDirectoryName(root)) ?? "module";
        return new ModulePackage(id, ComputeVersion(files), files);
    }

    public static LoadResult LoadFromDirectory(string definitionPath) => Load(ReadPackage(definitionPath));

    public static string ComputeVersion(IReadOnlyDictionary<string, string> files)
    {
        var builder = new StringBuilder();
        foreach (var (path, content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            builder.Append(path).Append('\0').Append(content).Append('\0');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
    }

    public static LoadResult Load(ModulePackage package) => Load(package, MinimalWorkflowGraphProjector.Instance);

    /// <summary>
    /// Loads a package, projecting each workflow graph with the supplied projector. The engine injects a
    /// declarative-aware projector; the default produces a minimal graph.
    /// </summary>
    public static LoadResult Load(ModulePackage package, IWorkflowGraphProjector projector)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(projector);
        var files = package.Files.ToDictionary(f => DefinitionPaths.Normalize(f.Key), f => f.Value, StringComparer.Ordinal);
        var errors = new DefinitionErrors(package.ModuleId);

        var manifestDoc = ReadDocument<ModuleManifest>(files, ModuleManifest.FileName, FormatSchemas.Module, errors);
        if (manifestDoc is null)
        {
            return new LoadResult(null, errors.All);
        }

        var manifest = manifestDoc.Value.Model;
        errors.ModuleId = manifest.Id;

        var workflows = ResolveWorkflows(manifest, files, projector, errors);
        var samples = ReadSamples(manifest, files, errors);

        if (errors.HasErrors)
        {
            return new LoadResult(null, errors.All);
        }

        var schemas = new Dictionary<string, (JsonObject, JsonSchema)>(StringComparer.Ordinal);
        EnvelopeValidator.Validate(manifest, workflows, samples, files, schemas, errors);

        if (errors.HasErrors)
        {
            return new LoadResult(null, errors.All);
        }

        var module = new ModuleDefinition(
            manifest,
            package.Version,
            workflows,
            samples.Select(s => s.Sample).ToList(),
            files,
            schemas,
            errors.All);

        return new LoadResult(module, errors.All);
    }

    /// <summary>
    /// Resolves each workflow's raw YAML text (reference + existence checks only; the declarative document itself is
    /// not parsed here).
    /// </summary>
    private static List<WorkflowDescriptor> ResolveWorkflows(
        ModuleManifest manifest, Dictionary<string, string> files, IWorkflowGraphProjector projector, DefinitionErrors errors)
    {
        var workflows = new List<WorkflowDescriptor>();
        foreach (var reference in manifest.Workflows)
        {
            if (!CheckReference(files, reference.File, ModuleManifest.FileName, "workflow", errors))
            {
                continue;
            }

            var normalized = DefinitionPaths.Normalize(reference.File);
            var demo = ReadDemo(reference, files[normalized], files, errors);
            workflows.Add(new WorkflowDescriptor
            {
                Id = reference.Id,
                Version = reference.Version,
                Name = reference.Name,
                Description = reference.Description,
                TitleTemplate = reference.Title,
                InputSchema = reference.Input.Schema,
                InputErrorMessage = reference.ErrorMessage,
                Yaml = files[normalized],
                File = normalized,
                Graph = projector.Project(reference, files[normalized]),
                Demo = demo,
            });
        }

        return workflows;
    }

    /// <summary>
    /// Reads and checks the optional demo responses file of a workflow: format schema, regexes, placeholders, and a
    /// warning for a step id that no agent step of the workflow uses.
    /// </summary>
    private static DemoFile? ReadDemo(WorkflowRef reference, string workflowYaml, Dictionary<string, string> files, DefinitionErrors errors)
    {
        if (reference.Demo is null || !CheckReference(files, reference.Demo, ModuleManifest.FileName, "demo", errors))
        {
            return null;
        }

        var path = DefinitionPaths.Normalize(reference.Demo);
        if (ReadDocument<DemoFile>(files, path, FormatSchemas.Demo, errors) is not { } doc)
        {
            return null;
        }

        foreach (var (stepId, step) in doc.Model.Steps)
        {
            var known = new HashSet<string>(step.Extract.Keys, StringComparer.Ordinal) { DemoTemplate.PromptPlaceholder };
            foreach (var (name, extract) in step.Extract)
            {
                if (extract.Pattern is { } pattern)
                {
                    CheckRegex(pattern, p => DemoTemplate.Pattern(p), path, stepId, $"extract '{name}'", errors);
                }

                foreach (var placeholder in DemoTemplate.Placeholders(extract.Default ?? "").Where(p => p != DemoTemplate.PromptPlaceholder))
                {
                    errors.Add(path, stepId, null, $"The default of extract '{name}' can only use {{{DemoTemplate.PromptPlaceholder}}}, not {{{placeholder}}}.");
                }
            }

            for (var i = 0; i < step.Rules.Count; i++)
            {
                CheckRegex(step.Rules[i].When, p => DemoTemplate.When(p), path, stepId, $"rule {i + 1}", errors);
            }

            var outputs = step.Rules.Select(r => r.Output).Append(step.Output);
            foreach (var placeholder in outputs.SelectMany(o => DemoTemplate.Placeholders(o)).Distinct().Where(p => !known.Contains(p)))
            {
                errors.Add(path, stepId, null, $"Unknown placeholder {{{placeholder}}}: declare it under 'extract' or use {{{DemoTemplate.PromptPlaceholder}}}.");
            }

            if (!UsesAgentStep(workflowYaml, stepId))
            {
                errors.Warn(path, stepId, null, $"No agent step of '{reference.File}' is named '{stepId}' (agent name '{stepId}' or '{stepId}|<agentId>').");
            }
        }

        return doc.Model;
    }

    private static void CheckRegex(string pattern, Action<string> compile, string path, string stepId, string where, DefinitionErrors errors)
    {
        try
        {
            compile(pattern);
        }
        catch (ArgumentException ex)
        {
            errors.Add(path, stepId, null, $"Invalid regex in {where}: {ex.Message}");
        }
    }

    // The declarative YAML is not parsed here (its PowerFx values are not plain YAML), so the check is textual: the step
    // id appears as an agent name prefix ("<stepId>|", also inside an expression) or as a whole agent name.
    private static bool UsesAgentStep(string workflowYaml, string stepId) =>
        workflowYaml.Contains(stepId + "|", StringComparison.Ordinal)
        || Regex.IsMatch(
            workflowYaml,
            $@"^\s*name:\s*[""']?{Regex.Escape(stepId)}[""']?\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static List<SampleDocument> ReadSamples(ModuleManifest manifest, Dictionary<string, string> files, DefinitionErrors errors)
    {
        var samples = new List<SampleDocument>();
        foreach (var path in manifest.Samples)
        {
            if (!CheckReference(files, path, ModuleManifest.FileName, "samples", errors))
            {
                continue;
            }

            var doc = ReadDocument<SamplesFile>(files, DefinitionPaths.Normalize(path), FormatSchemas.Samples, errors);
            if (doc is { } d)
            {
                samples.AddRange(d.Model.Samples.Select((s, i) => new SampleDocument(s, DefinitionPaths.Normalize(path), d.Lines, i)));
            }
        }

        return samples;
    }

    private static bool CheckReference(Dictionary<string, string> files, string path, string fromFile, string what, DefinitionErrors errors)
    {
        if (!DefinitionPaths.IsSafe(path))
        {
            errors.Add(fromFile, null, null, $"The {what} path '{path}' must be relative and stay inside the definition folder.");
            return false;
        }

        if (!files.ContainsKey(DefinitionPaths.Normalize(path)))
        {
            errors.Add(fromFile, null, null, $"The {what} file '{path}' does not exist.");
            return false;
        }

        return true;
    }

    private static (T Model, LineMap Lines)? ReadDocument<T>(
        Dictionary<string, string> files, string path, JsonSchema schema, DefinitionErrors errors)
        where T : class
    {
        if (!files.TryGetValue(path, out var text))
        {
            errors.Add(path, null, null, $"Required file '{path}' is missing.");
            return null;
        }

        var read = YamlJsonReader.Read(text);
        if (!read.IsSuccess)
        {
            errors.Add(path, null, read.ErrorLine, read.Error!);
            return null;
        }

        var schemaErrors = JsonSchemaValidator.Validate(schema, read.Root);
        if (schemaErrors.Count > 0)
        {
            foreach (var error in schemaErrors)
            {
                errors.Add(path, StepIdAt(read.Root, error.Pointer), read.Lines.Find(error.Pointer), error.ToString());
            }

            return null;
        }

        try
        {
            var model = read.Root.Deserialize<T>(SerializerOptions) ?? throw new JsonException("The document is empty.");
            return (model, read.Lines);
        }
        catch (JsonException ex)
        {
            errors.Add(path, null, null, $"Invalid definition: {ex.Message}");
            return null;
        }
    }

    /// <summary>Step id for errors located under <c>/steps/{index}</c> (array of steps) or <c>/steps/{id}</c> (demo file).</summary>
    private static string? StepIdAt(JsonNode? root, string pointer)
    {
        if (!pointer.StartsWith("/steps/", StringComparison.Ordinal))
        {
            return null;
        }

        var segment = pointer["/steps/".Length..].Split('/')[0];
        return root?["steps"] switch
        {
            JsonArray steps when int.TryParse(segment, out var index) && index < steps.Count => steps[index]?["id"]?.GetValue<string>(),
            JsonObject steps when steps.ContainsKey(segment) => segment,
            _ => null,
        };
    }

    private static string? TryReadModuleId(IReadOnlyDictionary<string, string> files) =>
        files.TryGetValue(ModuleManifest.FileName, out var text) && YamlJsonReader.Read(text).Root?["id"] is JsonValue id
            && id.TryGetValue<string>(out var value)
            ? value
            : null;
}
