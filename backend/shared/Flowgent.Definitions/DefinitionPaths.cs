namespace Flowgent.Definitions;
/// <summary>Path rules for files inside a definition folder.</summary>
public static class DefinitionPaths
{
    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/').Trim();

    /// <summary>True for relative paths that stay inside the definition folder.</summary>
    public static bool IsSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':'))
        {
            return false;
        }

        return path.Replace('\\', '/').Split('/').All(segment => segment != "..");
    }
}
