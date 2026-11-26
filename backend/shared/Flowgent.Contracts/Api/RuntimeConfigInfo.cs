namespace Flowgent.Contracts.Api;

public sealed record RuntimeConfigInfo(bool SequentialProcessing, bool DemoMode, string LlmProvider, string LlmModel);
