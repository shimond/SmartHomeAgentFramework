using Microsoft.Extensions.AI;

namespace SmartHome.Shared.Rag;

/// <summary>
/// Step 6 — the "real" retriever: instead of keyword overlap (<see cref="ManualStore"/>),
/// this embeds every manual chunk once with OpenAI's text-embedding-3-small and keeps the
/// vectors in memory. Each query is embedded too, and retrieval is cosine similarity over
/// those vectors — so "how do I clean the descaler?" can match a passage that says
/// "remove limescale" even with no shared words.
///
/// In-memory by design: this is a teaching demo, so the index is a plain List held for the
/// process lifetime. For anything real, swap the List for Microsoft.Extensions.VectorData's
/// InMemoryVectorStore (or a Postgres/Qdrant store) — the retrieve-THEN-ground lesson is
/// identical; only where the vectors live changes.
/// </summary>
public sealed class EmbeddingManualStore : IManualStore
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly List<(string Source, string Chunk, ReadOnlyMemory<float> Vector)> _index = new();
    private readonly Task _ready;

    public EmbeddingManualStore(
        string manualsFolder,
        IEmbeddingGenerator<string, Embedding<float>> embeddings)
    {
        _embeddings = embeddings;
        _ready = BuildIndexAsync(manualsFolder);
    }

    private async Task BuildIndexAsync(string manualsFolder)
    {
        if (!Directory.Exists(manualsFolder)) return;

        var pending = new List<(string Source, string Chunk)>();
        foreach (var file in Directory.EnumerateFiles(manualsFolder, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            foreach (var chunk in ChunkByParagraph(await File.ReadAllTextAsync(file)))
                pending.Add((name, chunk));
        }
        if (pending.Count == 0) return;

        // One batched call — embed every chunk at once rather than per-chunk round trips.
        var vectors = await _embeddings.GenerateAsync(pending.Select(p => p.Chunk));
        for (var i = 0; i < pending.Count; i++)
            _index.Add((pending[i].Source, pending[i].Chunk, vectors[i].Vector));
    }

    public async Task<string> SearchManuals(string query, int topK = 3)
    {
        await _ready; // index is built lazily on first use
        if (_index.Count == 0) return "No relevant passage found in the manuals.";

        var queryVector = (await _embeddings.GenerateAsync([query]))[0].Vector;

        var hits = _index
            .Select(c => (c.Source, c.Chunk, Score: CosineSimilarity(queryVector.Span, c.Vector.Span)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        return string.Join("\n\n", hits.Select(h => $"[{h.Source}] {h.Chunk}"));
    }

    private static IEnumerable<string> ChunkByParagraph(string text) =>
        text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 0);

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }
}
