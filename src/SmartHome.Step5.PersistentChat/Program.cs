// =====================================================================================
// STEP 5 — Agent + PERSISTENT conversations (Postgres via Aspire). Fixes Step 4's
// "Container A has it, Container B doesn't" problem by externalizing the thread's storage.
// -------------------------------------------------------------------------------------
// Two things to show side by side:
//
//   1. The DEVUI AGENT below — registered exactly like Step 3/4. Out of the box, DevUI's
//      own conversation/thread management is in-memory (same limitation as Step 4) unless
//      you plug a custom chat-history/message-store into the framework's history-provider
//      extension point so DevUI's sessions themselves read/write Postgres. That hook is
//      one of the faster-moving parts of the framework — verify the exact interface
//      (the book's AgentWithFileBasedChatHistoryProvider / AgentWithVectorStoreChatHistoryProvider
//      folders show the shape) against your installed version, then swap the file/vector
//      backing store for PostgresConversationStore below.
//
//   2. The EXPLICIT /api/chat-persistent endpoint — a guaranteed-to-compile demonstration
//      of the actual pattern, independent of DevUI's internals: load this conversationId's
//      messages from Postgres ONCE, rehydrate an in-memory thread, run the agent, save the
//      updated transcript back ONCE. This is the literal "any container, same conversation"
//      mechanism from the deck's diagram slide.
//
// Run via `dotnet run --project src/SmartHome.AppHost` so Postgres comes up automatically.
// =====================================================================================

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Npgsql;
using SmartHome.Shared.Domain;
using SmartHome.Shared.Hosting;
using SmartHome.Step5.PersistentChat;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
ChatClientSetup.RegisterAIClient(builder);
builder.Services.AddSingleton<IHomeGateway, InMemoryHome>();

// Aspire's Postgres client integration: registers an NpgsqlDataSource resolved from the
// "conversations" connection string the AppHost injects via .WithReference(conversationsDb).
// Outside Aspire, falls back to a local connection string in appsettings (see appsettings.json).
builder.AddNpgsqlDataSource("conversations");
builder.Services.AddSingleton<PostgresConversationStore>();
builder.Services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<PostgresConversationStore>());
// The framework-native chat-history provider (see PostgresChatHistoryProvider.cs). It's shared
// across all sessions and keeps no per-session state, so registering it as a singleton is correct.
builder.Services.AddSingleton<PostgresChatHistoryProvider>();

builder.AddAIAgent("concierge-persistent-chat", (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var state = sp.GetRequiredService<IHomeGateway>();

    // Framework-native upgrade (DONE): give the agent a Postgres-backed ChatHistoryProvider so
    // DevUI's OWN sessions persist to Postgres too — not just the explicit endpoint below. With this
    // set, every DevUI run loads prior turns from Postgres before the model call and saves the new
    // turn after, keyed by an id carried in the (serialized) session — so any container resumes any
    // DevUI conversation. Instructions/tools move onto ChatOptions, which the options ctor expects.
    var options = new ChatClientAgentOptions
    {
        Name = key,
        ChatOptions = new ChatOptions
        {
            Instructions = Agents.HomeAssistanceInstructions,
            Tools = Agents.ToolsFor(state),
        },
        ChatHistoryProvider = sp.GetRequiredService<PostgresChatHistoryProvider>(),
    };
    return new ChatClientAgent(chat, options);
});

var app = builder.Build();
app.MapDefaultEndpoints();

// Create the table on startup — fine for a workshop; use real migrations in production.
// No scope needed: PostgresConversationStore is a singleton (there are no scoped services
// in this solution), so CreateScope() here would buy nothing but an extra IDisposable.
await app.Services.GetRequiredService<PostgresConversationStore>().EnsureSchemaAsync();

if (app.Environment.IsDevelopment())
    app.MapDevUI();

//// --- THE PATTERN ITSELF: explicit load → rehydrate → run → save, once per request ---
//app.MapPost("/api/chat-persistent", async (
//    PersistentChatRequest req, AIAgent agent, IConversationStore store, CancellationToken ct) =>
//{
//    // 1. Load this conversation's prior messages from Postgres — one round trip.
//    var history = await store.LoadAsync(req.ConversationId, ct);

//    // 2. Append the new user message to build the full conversation context.
//    var messages = new List<ChatMessage>(history)
//    {
//        new ChatMessage(ChatRole.User, req.Message)
//    };

//    // 3. Run with the full history — behaves exactly like Step 3/4 but stateless per-request.
//    var response = await agent.RunAsync(messages, null, null, ct);

//    // 4. Persist the updated transcript back. The NEXT request — on ANY container —
//    //    reloads from here, not from this process's memory.
//    var updatedHistory = new List<ChatMessage>(messages);
//    updatedHistory.Add(new ChatMessage(ChatRole.Assistant, response.Text));
//    await store.SaveAsync(req.ConversationId, updatedHistory, ct);

//    return Results.Ok(new { reply = response.Text, conversationId = req.ConversationId });
//});

app.Run();

public record PersistentChatRequest(string ConversationId, string Message);
