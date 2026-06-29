using System.Text.Json;
using Microsoft.Extensions.AI;
using Npgsql;
using SmartHome.Shared.Domain;

namespace SmartHome.Step5.PersistentChat;

/// <summary>
/// The Postgres implementation of IConversationStore (defined in SmartHome.Shared). This
/// is the fix for the "Container A has it, Container B doesn't" problem from Step 4: every
/// container reads and writes the SAME row, keyed by conversationId, so any of them can
/// pick up any conversation. Messages are stored as JSONB — a Mongo-style document inside
/// a relational column, which is why Postgres works just as naturally here as a document DB.
///
/// Rehydration pattern (see Program.cs): load once at the START of a request, run the
/// agent against an in-memory thread for the DURATION of that request, save once at the
/// END. Not a per-token round trip to the database.
/// </summary>
public sealed class PostgresConversationStore(NpgsqlDataSource dataSource) : IConversationStore
{
    // Call once at startup (see Program.cs) — fine for a workshop; use a real migration
    // tool (e.g. DbUp, EF Core migrations) for anything beyond the demo.
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS conversations (
                conversation_id   TEXT PRIMARY KEY,
                messages_json     JSONB NOT NULL,
                updated_at        TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> LoadAsync(string conversationId, CancellationToken ct = default)
    {
        const string sql = "SELECT messages_json FROM conversations WHERE conversation_id = $1";
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(conversationId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (json is null) return [];

        return JsonSerializer.Deserialize<List<ChatMessage>>(json, AIJsonUtilities.DefaultOptions) ?? [];
    }

    public async Task SaveAsync(string conversationId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(messages.ToList(), AIJsonUtilities.DefaultOptions);

        const string sql = """
            INSERT INTO conversations (conversation_id, messages_json, updated_at)
            VALUES ($1, $2::jsonb, now())
            ON CONFLICT (conversation_id)
            DO UPDATE SET messages_json = EXCLUDED.messages_json, updated_at = now();
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue(conversationId);
        cmd.Parameters.AddWithValue(json);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
