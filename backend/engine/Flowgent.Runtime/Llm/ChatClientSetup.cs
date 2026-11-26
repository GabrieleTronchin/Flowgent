namespace Flowgent.Runtime.Llm;

using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenAI;

public static class ChatClientSetup
{
    /// <summary>
    /// Registers the engine's <see cref="IChatClient"/>: telemetry middleware → OpenTelemetry → Demo or OpenAI-compatible client.
    /// </summary>
    public static IServiceCollection AddFlowgentChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(LlmOptions.SectionName);
        services.Configure<LlmOptions>(section);
        var options = section.Get<LlmOptions>() ?? new LlmOptions();

        var innerClient = options.IsDemo ? CreateDemoClient(options) : new StripDemoResponseChatClient(CreateOpenAiCompatibleClient(options));

        services.TryAddSingleton<LlmTelemetryStore>();
        services.TryAddSingleton<IChatClient>(sp => new LlmTelemetryMiddleware(
            innerClient.AsBuilder().UseOpenTelemetry(sourceName: "Flowgent.Llm").Build(sp),
            sp.GetRequiredService<ILogger<LlmTelemetryMiddleware>>(),
            sp.GetRequiredService<LlmTelemetryStore>()));

        return services;
    }

    /// <summary>Model reported by the runtime configuration endpoint.</summary>
    public static string EffectiveModel(LlmOptions options) => options.IsDemo ? DemoChatClient.DemoModelId : options.ModelName;

    public static string EffectiveProvider(LlmOptions options) => options.IsDemo ? LlmOptions.DemoProvider : options.Provider;

    /// <summary>Maps the configured provider to the endpoint and model used by the OpenAI-compatible client.</summary>
    internal static ChatClientSettings ResolveSettings(LlmOptions options) =>
        options.Provider.ToLowerInvariant() switch
        {
            // Endpoint is optional: when set, any OpenAI-compatible server (Gemini, Groq, Ollama, ...) can be used.
            "openai" => new(ParseEndpoint(options.Endpoint), options.ModelName),
            _ => throw new InvalidOperationException($"Unsupported LLM provider: {options.Provider}"),
        };

    private static IChatClient CreateDemoClient(LlmOptions options) =>
        new DemoChatClient(TimeSpan.FromMilliseconds(Math.Max(0, options.DemoLatencyMs)));

    private static IChatClient CreateOpenAiCompatibleClient(LlmOptions options)
    {
        var settings = ResolveSettings(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "LLM API key is not configured. Set 'Flowgent:Llm:ApiKey' (user secrets or appsettings.Development.json), " +
                "or set 'Flowgent:Llm:Provider' to 'Demo' to use simulated responses.");
        }

        var clientOptions = new OpenAIClientOptions();
        if (settings.Endpoint is not null)
        {
            clientOptions.Endpoint = settings.Endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions)
            .GetChatClient(settings.ModelName)
            .AsIChatClient();
    }

    private static Uri? ParseEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : throw new InvalidOperationException($"Invalid LLM endpoint: '{endpoint}'. Expected an absolute http(s) URL.");
    }
}

internal sealed record ChatClientSettings(Uri? Endpoint, string ModelName);
