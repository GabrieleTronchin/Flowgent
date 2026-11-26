namespace Flowgent.Module.Sdk;
/// <summary>Expected tool failure: returned to the engine as an error (the step fails with this message).</summary>
public sealed class ToolException(string message) : Exception(message);
