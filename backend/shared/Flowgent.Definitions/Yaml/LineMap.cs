namespace Flowgent.Definitions.Yaml;
/// <summary>Line numbers of YAML nodes indexed by JSON pointer (e.g. <c>/steps/2/prompt</c>).</summary>
public sealed class LineMap
{
    private readonly Dictionary<string, int> _lines = new(StringComparer.Ordinal);

    internal void Add(string pointer, int line) => _lines[pointer] = line;

    /// <summary>Line of the node at <paramref name="pointer"/>, or of its closest known ancestor.</summary>
    public int? Find(string? pointer)
    {
        var current = pointer ?? "";
        while (true)
        {
            if (_lines.TryGetValue(current, out var line))
            {
                return line;
            }

            if (current.Length == 0)
            {
                return null;
            }

            var slash = current.LastIndexOf('/');
            current = slash <= 0 ? "" : current[..slash];
        }
    }
}
