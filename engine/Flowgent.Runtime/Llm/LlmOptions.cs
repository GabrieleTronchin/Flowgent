namespace Flowgent.Runtime.Llm;

/// <summary>LLM provider configuration (section <c>Flowgent:Llm</c>).</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Flowgent:Llm";
    public const string DemoProvider = "Demo";
    public const string OpenAIProvider = "OpenAI";

    /// <summary>
    /// <c>Demo</c> (default: simulated responses from the workflow definitions, no API key needed) or
    /// <c>OpenAI</c> (any OpenAI-compatible API, see <see cref="Endpoint"/>).
    /// </summary>
    public string Provider { get; set; } = DemoProvider;

    public string ModelName { get; set; } = "gpt-4o-mini";

    public string ApiKey { get; set; } = "";

    /// <summary>Optional OpenAI-compatible endpoint (e.g. Gemini, Groq, Ollama). Defaults to OpenAI.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Simulated latency per LLM call in Demo mode, so run progress is visible in the dashboard.</summary>
    public int DemoLatencyMs { get; set; } = 1500;

    public bool IsDemo => Provider.Equals(DemoProvider, StringComparison.OrdinalIgnoreCase);
}
