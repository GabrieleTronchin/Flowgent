namespace Flowgent.Runtime.Maf;

using Microsoft.Extensions.AI;

/// <summary>
/// Backing store for the conversations/messages the MAF agent provider creates while a run executes (design §6).
/// Keyed by conversation id (one or more per run). The default <see cref="RunConversationStore"/> is in-memory and
/// held for the run lifetime; it is registered swappably so a persistence-backed store can replace it later (D4).
/// </summary>
public interface IConversationStore
{
    Task<string> CreateConversationAsync(CancellationToken ct = default);

    Task<ChatMessage> AppendAsync(string conversationId, ChatMessage message, CancellationToken ct = default);

    Task<ChatMessage?> GetAsync(string conversationId, string messageId, CancellationToken ct = default);

    IReadOnlyList<ChatMessage> List(string conversationId, int? limit = null, bool newestFirst = false);
}
