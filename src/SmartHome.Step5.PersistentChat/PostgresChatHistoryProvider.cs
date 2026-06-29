using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartHome.Shared.Domain;

namespace SmartHome.Step5.PersistentChat;

/// <summary>
/// The framework-native half of Step 5: a <see cref="ChatHistoryProvider"/> that backs DevUI's
/// OWN sessions with Postgres. The explicit /api/chat-persistent endpoint in Program.cs shows the
/// load → run → save pattern by hand; this class hands that exact pattern to the framework so that
/// the DevUI "concierge" agent — created and driven entirely by DevUI's internals — persists too.
///
/// HOW IT HOOKS IN
/// ---------------
/// A <see cref="ChatClientAgent"/> created with <see cref="ChatClientAgentOptions.ChatHistoryProvider"/>
/// set will, on every run:
///   1. call <see cref="ProvideChatHistoryAsync"/> BEFORE the model call to prepend prior messages, and
///   2. call <see cref="StoreChatHistoryAsync"/> AFTER a successful run to append the new turn.
/// The base class does the merging/filtering/error-handling; we only implement load and store.
///
/// WHY A STATE-BAG KEY (and not the provider's own fields)
/// -------------------------------------------------------
/// One provider instance is shared across ALL sessions, so it must never hold session-specific
/// state in its fields. Each conversation's identity lives in the session's <see cref="AgentSession.StateBag"/>,
/// which the framework serializes with the session. DevUI persists that serialized session, so the
/// SAME conversation key comes back on a later request — on any container — and we reload the SAME
/// Postgres row. That is the "any container, same conversation" guarantee, now applied to DevUI itself.
///
/// Storage itself is delegated to <see cref="PostgresConversationStore"/> — the very same JSONB table
/// the explicit endpoint writes to — so both halves of Step 5 share one backing store.
/// </summary>
public sealed class PostgresChatHistoryProvider(PostgresConversationStore store) : ChatHistoryProvider
{
    // Key under which we stash this session's conversation id inside the (serialized) StateBag.
    private const string ConversationIdStateKey = "PostgresChatHistoryProvider.ConversationId";

    /// <summary>
    /// Load: return this session's prior transcript from Postgres. The base class prepends these
    /// to the caller-provided messages, so the model sees the full conversation — exactly like the
    /// manual <c>store.LoadAsync</c> step in the endpoint, but invoked by the framework.
    /// </summary>
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        // No session means a stateless one-shot run with no durable identity — nothing to load.
        var conversationId = GetOrCreateConversationId(context.Session);
        if (conversationId is null) return [];

        return await store.LoadAsync(conversationId, cancellationToken);
    }

    /// <summary>
    /// Store: persist the updated transcript back to Postgres after a successful run. The base class
    /// only calls this when the invocation succeeded, and hands us the request + response messages
    /// for this turn; we re-load the prior history and append them so the row stays the full transcript.
    /// </summary>
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var conversationId = GetOrCreateConversationId(context.Session);
        if (conversationId is null) return; // No session — nowhere durable to save this turn.

        // RequestMessages here are just this turn's new caller messages (NOT the chat-history-supplied
        // ones — see the InvokedContext.RequestMessages docs), so loading the prior transcript and
        // appending request + response keeps the stored row as the complete, ordered conversation.
        var history = await store.LoadAsync(conversationId, cancellationToken);

        var updated = new List<ChatMessage>(history);
        updated.AddRange(context.RequestMessages);
        if (context.ResponseMessages is not null)
            updated.AddRange(context.ResponseMessages);

        await store.SaveAsync(conversationId, updated, cancellationToken);
    }

    // The conversation id is created once per session and then preserved in the StateBag so it is
    // serialized with the session and reused on every subsequent run for that conversation.
    private static string? GetOrCreateConversationId(AgentSession? session)
    {
        if (session is null) return null;

        if (session.StateBag.TryGetValue<string>(ConversationIdStateKey, out var existing)
            && !string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var conversationId = Guid.NewGuid().ToString("n");
        session.StateBag.SetValue(ConversationIdStateKey, conversationId);
        return conversationId;
    }
}
