namespace Flowgent.Runtime.Maf;

using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Flowgent.Contracts.Api;
using Flowgent.Definitions.Model;

/// <summary>
/// Trial-builds each workflow descriptor's declarative YAML so a malformed document is reported at load time
/// (design §4.3), the replacement for the deleted custom step-graph validation. Lives in the seam (it needs the
/// declarative builder); <see cref="Flowgent.Runtime.Modules.ModuleCatalog"/> injects it so the Definitions layer stays
/// declarative-free.
/// </summary>
public interface IDeclarativeDefinitionValidator
{
    /// <summary>Trial-builds each descriptor; returns one <see cref="DefinitionErrorInfo"/> per workflow that fails to build.</summary>
    IReadOnlyList<DefinitionErrorInfo> Validate(string moduleId, IReadOnlyList<WorkflowDescriptor> workflows);
}

/// <summary>
/// Default <see cref="IDeclarativeDefinitionValidator"/>: builds each descriptor YAML with a no-op agent provider and
/// no-op MCP handler. A build exception becomes a <see cref="DefinitionErrorInfo"/> pointing at the workflow file; the
/// line is best-effort (null, since the builder gives no position).
/// </summary>
public sealed class DeclarativeValidation : IDeclarativeDefinitionValidator
{
    public IReadOnlyList<DefinitionErrorInfo> Validate(string moduleId, IReadOnlyList<WorkflowDescriptor> workflows)
    {
        var errors = new List<DefinitionErrorInfo>();
        foreach (var workflow in workflows)
        {
            try
            {
                var options = new DeclarativeWorkflowOptions(NoOpAgentProvider.Instance)
                {
                    McpToolHandler = NoOpMcpToolHandler.Instance,
                };

                _ = DeclarativeWorkflowBuilder.Build<JsonObject>(
                    new StringReader(workflow.Yaml),
                    options,
                    input => new ChatMessage(ChatRole.User, input.ToJsonString()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new DefinitionErrorInfo(moduleId, workflow.File, null, null, $"The workflow could not be built: {ex.Message}", DefinitionSeverity.Error));
            }
        }

        return errors;
    }

    private sealed class NoOpAgentProvider : ResponseAgentProvider
    {
        public static readonly NoOpAgentProvider Instance = new();

        public override IAsyncEnumerable<AgentResponseUpdate> InvokeAgentAsync(
            string agentId,
            string? agentVersion,
            string? conversationId,
            IEnumerable<ChatMessage>? messages,
            IDictionary<string, object?>? inputArguments,
            CancellationToken cancellationToken = default) => Empty();

        public override Task<string> CreateConversationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"conv-{Guid.NewGuid():N}");

        public override Task<ChatMessage> CreateMessageAsync(string conversationId, ChatMessage conversationMessage, CancellationToken cancellationToken = default) =>
            Task.FromResult(conversationMessage);

        public override Task<ChatMessage> GetMessageAsync(string conversationId, string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatMessage(ChatRole.Assistant, string.Empty));

        public override async IAsyncEnumerable<ChatMessage> GetMessagesAsync(
            string conversationId,
            int? limit = null,
            string? after = null,
            string? before = null,
            bool newestFirst = false,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        private static async IAsyncEnumerable<AgentResponseUpdate> Empty()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    private sealed class NoOpMcpToolHandler : IMcpToolHandler
    {
        public static readonly NoOpMcpToolHandler Instance = new();

        public Task<McpServerToolResultContent> InvokeToolAsync(
            string serverUrl,
            string? serverLabel,
            string toolName,
            IDictionary<string, object?>? arguments,
            IDictionary<string, string>? headers,
            string? connectionName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new McpServerToolResultContent($"noop-{Guid.NewGuid():N}") { Outputs = [new TextContent("null")] });
    }
}
