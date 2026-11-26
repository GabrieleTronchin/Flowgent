namespace Flowgent.Engine.Core.Configuration;

/// <summary>Where module definitions are read from (section <c>Flowgent:Definitions</c>).</summary>
public sealed class DefinitionsOptions
{
    /// <summary>
    /// Root of a mounted definitions volume. When set and a module's folder exists under it, definitions are read from
    /// the volume (primary) instead of the module service over HTTP (secondary). The module folder is <c>&lt;Path&gt;/&lt;moduleId&gt;</c>.
    /// </summary>
    public string? Path { get; set; }
}
