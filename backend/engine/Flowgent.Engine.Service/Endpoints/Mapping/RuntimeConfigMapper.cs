namespace Flowgent.Engine.Service.Endpoints.Mapping;

using Flowgent.Contracts.Api;
using Flowgent.Engine.Core.Configuration;
using Flowgent.Engine.Core.Llm;

public static class RuntimeConfigMapper
{
    /// <summary>Dashboard view of the runtime configuration: never the API key or the endpoint URL.</summary>
    public static RuntimeConfigInfo BuildConfig(EngineOptions engine, LlmOptions llm) =>
        new(engine.SequentialProcessing, llm.IsDemo, ChatClientSetup.EffectiveProvider(llm), ChatClientSetup.EffectiveModel(llm));
}
