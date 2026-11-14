namespace Flowgent.Modules.Support.Tools;

using System.Text.RegularExpressions;

/// <summary>The result of locating a component: the module-relative <see cref="Path"/> and its <see cref="Snippet"/>.</summary>
/// <param name="Path">Relative to the support module folder, e.g. <c>dummy-apps/application-a/src/Controllers/OrderController.cs</c>.</param>
public sealed record LocateResult(bool Found, string Component, string Path, string Language, string? Snippet);

/// <summary>Raised when the application folder is malformed or does not resolve to a known dummy app.</summary>
public sealed class LocateComponentException(string message) : Exception(message);

/// <summary>
/// Pure, SDK-free implementation of <c>support.locateComponent</c>: finds the dummy-app source file mentioned
/// in a support email (by file name or type name, earliest mention wins) and returns its module-relative path
/// and content. Extracted verbatim from <c>LocateComponentTool</c> so the algorithm is single-sourced and can
/// be hosted either by the module-SDK adapter or, later, by a standalone MCP server. Takes the resolved
/// absolute <c>dummyAppsRoot</c> and the module-relative root prefix as explicit parameters (no SDK types),
/// and signals validation failures with <see cref="LocateComponentException"/> instead of the SDK's
/// <c>ToolException</c>.
/// </summary>
public static partial class LocateComponentCore
{
    public const string UnknownComponent = "UnknownComponent";
    public const int MaxSnippetLength = 4000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp",
        [".razor"] = "razor",
        [".cshtml"] = "razor",
        [".ts"] = "typescript",
        [".tsx"] = "tsx",
        [".js"] = "javascript",
        [".css"] = "css",
    };

    /// <summary>
    /// Locates the component for <paramref name="applicationFolder"/> within <paramref name="dummyAppsRoot"/>
    /// (an absolute, normalized path). <paramref name="relativeRootPrefix"/> is the module-relative prefix used
    /// to build the returned <see cref="LocateResult.Path"/> (e.g. <c>dummy-apps</c>).
    /// </summary>
    public static async Task<LocateResult> LocateAsync(
        string applicationFolder,
        string? text,
        string dummyAppsRoot,
        string relativeRootPrefix,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(applicationFolder) || !FolderPattern().IsMatch(applicationFolder))
        {
            throw new LocateComponentException($"Invalid application folder '{applicationFolder}'.");
        }

        var applicationRoot = Path.GetFullPath(Path.Combine(dummyAppsRoot, applicationFolder));

        if (!applicationRoot.StartsWith(dummyAppsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(applicationRoot))
        {
            throw new LocateComponentException($"Unknown application folder '{applicationFolder}'.");
        }

        var relativeRoot = $"{relativeRootPrefix.Trim('/', '\\').Replace('\\', '/')}/{applicationFolder}";
        var match = FindFirstMentioned(applicationRoot, text ?? "");
        if (match is null)
        {
            return new LocateResult(false, UnknownComponent, $"{relativeRoot}/src/{UnknownComponent}", "", null);
        }

        var content = await File.ReadAllTextAsync(match, ct);
        var relative = Path.GetRelativePath(applicationRoot, match).Replace('\\', '/');
        return new LocateResult(
            true,
            Path.GetFileName(match),
            $"{relativeRoot}/{relative}",
            Languages.GetValueOrDefault(Path.GetExtension(match), ""),
            content.Length <= MaxSnippetLength ? content : content[..MaxSnippetLength] + "\n// ... (truncated)");
    }

    /// <summary>The source file whose name (with or without extension) appears first in the text.</summary>
    internal static string? FindFirstMentioned(string applicationRoot, string text) =>
        Directory.EnumerateFiles(applicationRoot, "*", SearchOption.AllDirectories)
            .Where(f => Languages.ContainsKey(Path.GetExtension(f)))
            .Select(f => (File: f, Index: FirstMention(text, f)))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .ThenByDescending(x => Path.GetFileName(x.File).Length)
            .ThenBy(x => x.File, StringComparer.Ordinal)
            .Select(x => x.File)
            .FirstOrDefault();

    private static int FirstMention(string text, string file)
    {
        var name = Path.GetFileName(file);
        var stem = Path.GetFileNameWithoutExtension(file);
        var pattern = $@"\b(?:{Regex.Escape(name)}|{Regex.Escape(stem)})\b";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Index : -1;
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex FolderPattern();
}
