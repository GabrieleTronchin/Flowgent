namespace Flowgent.Runtime.Maf;

using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Flowgent.Runtime.Execution;
using Flowgent.Runtime.Modules;

/// <summary>
/// Builds one MAF declarative <see cref="Workflow"/> per run (design §8) by wrapping
/// <see cref="DeclarativeWorkflowBuilder"/>.Build&lt;JsonObject&gt; over the run's raw YAML, with
/// <see cref="DeclarativeWorkflowOptions"/> carrying the per-run <see cref="MafAgentProvider"/> and
/// <see cref="ModuleToolHandler"/>. A fresh workflow per run is required: the framework takes exclusive ownership and
/// executors hold run state.
/// </summary>
public sealed class MafWorkflowFactory(
    IChatClient chatClient,
    ModuleToolInvoker toolInvoker,
    McpClientPool mcpClientPool,
    Microsoft.Extensions.Options.IOptions<Configuration.EngineOptions> engineOptions,
    Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
    Llm.DemoResponseProvider demo,
    Microsoft.Extensions.Options.IOptions<Llm.LlmOptions> llmOptions,
    Func<IConversationStore> conversationStoreFactory)
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly ModuleToolInvoker _toolInvoker = toolInvoker;
    private readonly McpClientPool _mcpClientPool = mcpClientPool;
    private readonly Configuration.EngineOptions _engineOptions = engineOptions.Value;
    private readonly Microsoft.Extensions.Logging.ILogger _toolHandlerLogger = Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<ModuleToolHandler>(loggerFactory);
    private readonly Llm.DemoResponseProvider _demo = demo;
    private readonly Llm.LlmOptions _llmOptions = llmOptions.Value;
    private readonly Func<IConversationStore> _conversationStoreFactory = conversationStoreFactory;

    /// <summary>The agent provider and MCP handler built for a run; kept so the caller can read run state they mutate.</summary>
    public sealed record BuiltWorkflow(Workflow Workflow, MafAgentProvider AgentProvider, ModuleToolHandler ToolHandler);

    public BuiltWorkflow Build(RunContext run)
    {
        var agentProvider = new MafAgentProvider(run, _chatClient, _llmOptions, _demo, _conversationStoreFactory());
        var toolHandler = new ModuleToolHandler(run, _toolInvoker, _mcpClientPool, _engineOptions, _toolHandlerLogger);

        var options = new DeclarativeWorkflowOptions(agentProvider)
        {
            McpToolHandler = toolHandler,
        };

        var workflow = DeclarativeWorkflowBuilder.Build<JsonObject>(
            new StringReader(run.Workflow.Yaml),
            options,
            MapInput);

        return new BuiltWorkflow(workflow, agentProvider, toolHandler);
    }

    /// <summary>
    /// PROBE-C-selected input shaping (<c>ParseJSON</c> absent): the input object crosses as one JSON string in the
    /// trigger message; the workflow YAML reads the fields it needs as named <c>Global.*</c> variables.
    /// </summary>
    private static ChatMessage MapInput(JsonObject input) => new(ChatRole.User, input.ToJsonString());
}
