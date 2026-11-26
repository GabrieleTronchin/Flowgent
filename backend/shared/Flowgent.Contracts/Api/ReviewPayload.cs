namespace Flowgent.Contracts.Api;

public sealed record ReviewPayload(string Title, IReadOnlyList<ReviewField> Fields);

public sealed record ReviewField(string Label, string? Value);
