interface LlmModeBadgeProps {
  demoMode: boolean;
  llmProvider: string | null;
  llmModel: string | null;
}

/**
 * Shows which LLM backs the pipeline: "Demo Mode" when responses are simulated,
 * otherwise the configured provider and model (e.g. "OpenAI · gpt-4o-mini").
 * Renders nothing while the information is unknown.
 */
export function LlmModeBadge({ demoMode, llmProvider, llmModel }: LlmModeBadgeProps) {
  if (demoMode) {
    return (
      <span
        className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-violet-900/50 text-violet-300 border border-violet-700"
        title="LLM responses are simulated (Flowgent:Llm:Provider = Demo)"
      >
        Demo Mode
      </span>
    );
  }

  if (!llmModel) {
    return null;
  }

  const label = llmProvider ? `${llmProvider} · ${llmModel}` : llmModel;

  return (
    <span
      className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-sky-900/50 text-sky-300 border border-sky-700"
      title={`LLM calls use ${label}`}
      aria-label={`LLM model: ${label}`}
    >
      {label}
    </span>
  );
}
