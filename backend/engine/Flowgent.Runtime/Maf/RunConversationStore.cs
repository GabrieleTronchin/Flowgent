namespace Flowgent.Runtime.Maf;

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

/// <summary>
/// Default in-memory <see cref="IConversationStore"/>, held for the run lifetime (one instance per run, registered as
/// a transient/scoped-per-run dependency of <see cref="MafAgentProvider"/>). Mirrors the spike provider's tiny
/// in-memory map; a persistence-backed store can replace it without touching the provider.
/// </summary>
public sealed class RunConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new(StringComparer.Ordinal);

    public Task<string> CreateConversationAsync(CancellationToken ct = default)
    {
        var id = $"conv-{Guid.NewGuid():N}";
        _conversations[id] = [];
        return Task.FromResult(id);
    }

    public Task<ChatMessage> AppendAsync(string conversationId, ChatMessage message, CancellationToken ct = default)
    {
        var list = _conversations.GetOrAdd(conversationId, _ => []);
        if (string.IsNullOrEmpty(message.MessageId))
        {
            message.MessageId = $"msg-{Guid.NewGuid():N}";
        }

        lock (list)
        {
            list.Add(message);
        }

        return Task.FromResult(message);
    }

    public Task<ChatMessage?> GetAsync(string conversationId, string messageId, CancellationToken ct = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var list))
        {
            return Task.FromResult<ChatMessage?>(null);
        }

        lock (list)
        {
            return Task.FromResult(list.FirstOrDefault(m => m.MessageId == messageId));
        }
    }

    public IReadOnlyList<ChatMessage> List(string conversationId, int? limit = null, bool newestFirst = false)
    {
        if (!_conversations.TryGetValue(conversationId, out var list))
        {
            return [];
        }

        lock (list)
        {
            IEnumerable<ChatMessage> seq = newestFirst ? Enumerable.Reverse(list) : list;
            if (limit is > 0)
            {
                seq = seq.Take(limit.Value);
            }

            return seq.ToList();
        }
    }
}
