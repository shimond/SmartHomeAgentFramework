using Microsoft.Extensions.AI;

namespace SmartHome.Shared.Domain;

/// <summary>
/// Step 5's swappable boundary. The THREAD (session) is just an in-memory object for the
/// duration of one run; this interface is where its messages are durably stored, so any
/// container can rehydrate any conversationId — not just the one that created it.
///
/// Today: Postgres (SmartHome.Step5.PersistentChat/PostgresConversationStore.cs).
/// Drop-in alternative: Mongo, Redis, a vector store — the endpoint code never changes.
/// </summary>
public interface IConversationStore
{
    Task<IReadOnlyList<ChatMessage>> LoadAsync(string conversationId, CancellationToken ct = default);
    Task SaveAsync(string conversationId, IEnumerable<ChatMessage> messages, CancellationToken ct = default);
}

/// <summary>
/// The Step-4-equivalent baseline: an in-memory store, so Step 5 can start by literally
/// swapping ONE registration line and show nothing else in the endpoint needs to change.
/// This is intentionally what breaks across multiple containers/restarts — that's the point.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly Dictionary<string, List<ChatMessage>> _byId = new();
    private readonly Lock _gate = new();

    public Task<IReadOnlyList<ChatMessage>> LoadAsync(string conversationId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ChatMessage>>(
                _byId.TryGetValue(conversationId, out var list) ? list.ToList() : []);
    }

    public Task SaveAsync(string conversationId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        lock (_gate) _byId[conversationId] = messages.ToList();
        return Task.CompletedTask;
    }
}
