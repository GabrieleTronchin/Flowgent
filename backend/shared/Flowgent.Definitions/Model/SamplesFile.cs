namespace Flowgent.Definitions.Model;

/// <summary>A samples file (<c>samples/*.yaml</c>).</summary>
public sealed class SamplesFile
{
    public List<SampleDefinition> Samples { get; init; } = [];
}
