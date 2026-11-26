namespace Flowgent.Definitions;

using Flowgent.Definitions.Model;
using Flowgent.Definitions.Yaml;

internal sealed record SampleDocument(SampleDefinition Sample, string File, LineMap Lines, int Index);
