using System.ComponentModel;

namespace SmartHome.Shared.Rag;

/// <summary>
/// The retrieve-THEN-ground contract Step 6 hangs the RAG lesson on. Two implementations
/// ship: <see cref="ManualStore"/> (dependency-free keyword overlap) and
/// <see cref="EmbeddingManualStore"/> (OpenAI embeddings + in-memory cosine similarity).
/// SmartHome:RagStore picks which one is registered; the agent only ever sees this tool.
/// </summary>
public interface IManualStore
{
    [Description("Search the appliance manuals for instructions relevant to the question. " +
                 "Always call this before answering a 'how do I…' maintenance question, and " +
                 "ground your answer ONLY in what it returns.")]
    Task<string> SearchManuals(string query, int topK = 3);
}
