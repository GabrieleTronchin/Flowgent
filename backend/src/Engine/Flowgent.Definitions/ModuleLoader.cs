namespace Flowgent.Definitions;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Modules;
using Flowgent.Definitions.Model;
using Flowgent.Definitions.Schemas;
using Flowgent.Definitions.Validation;
using Flowgent.Definitions.Yaml;

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

        // Resolve each workflow's raw YAML text (reference + existence checks only; the declarative document
        // itself is not parsed here).
        var workflows = new List<WorkflowDescriptor>();
        foreach (var reference in manifest.Workflows)
        {
            if (!CheckReference(files, reference.File, ModuleManifest.FileName, "workflow", errors))
            {
                continue;
            }

            var normalized = DefinitionPaths.Normalize(reference.File);
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
            });
        }

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

    /// <summary>Step id for errors located under <c>/steps/{index}</c>.</summary>
    private static string? StepIdAt(JsonNode? root, string pointer)
    {
        if (!pointer.StartsWith("/steps/", StringComparison.Ordinal))
        {
            return null;
        }

        var segment = pointer["/steps/".Length..].Split('/')[0];
        return int.TryParse(segment, out var index) && root?["steps"] is JsonArray steps && index < steps.Count
            ? steps[index]?["id"]?.GetValue<string>()
            : null;
    }

    private static string? TryReadModuleId(IReadOnlyDictionary<string, string> files) =>
        files.TryGetValue(ModuleManifest.FileName, out var text) && YamlJsonReader.Read(text).Root?["id"] is JsonValue id
            && id.TryGetValue<string>(out var value)
            ? value
            : null;
}

internal sealed record SampleDocument(SampleDefinition Sample, string File, LineMap Lines, int Index);

/// <summary>Collects definition errors and warnings for one module.</summary>
internal sealed class DefinitionErrors(string moduleId)
{
    private readonly List<DefinitionErrorInfo> _items = [];

    public string ModuleId { get; set; } = moduleId;

    public IReadOnlyList<DefinitionErrorInfo> All => _items;

    public bool HasErrors => _items.Exists(e => e.Severity == DefinitionSeverity.Error);

    public void Add(string? file, string? stepId, int? line, string message) =>
        _items.Add(new DefinitionErrorInfo(ModuleId, file, stepId, line, message, DefinitionSeverity.Error));

    public void Warn(string? file, string? stepId, int? line, string message) =>
        _items.Add(new DefinitionErrorInfo(ModuleId, file, stepId, line, message, DefinitionSeverity.Warning));
}
