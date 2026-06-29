using System.ComponentModel;
using System.Text.RegularExpressions;

namespace SmartHome.Shared.Rag;

/// <summary>
/// Step 6 — a deliberately tiny, dependency-free retriever (keyword overlap, not
/// embeddings) so this compiles and runs with zero extra services. The framework-native
/// path is the book's AgentWithDataIngestion + AgentWithInMemoryVectorStoreProvider +
/// AgentWithTextSearchProvider (real embeddings in a vector store) — swap Score() for an
/// embedding similarity using Microsoft.Extensions.AI's IEmbeddingGenerator if you want
/// the production version. The lesson (retrieve THEN ground) is identical either way.
/// </summary>
public sealed partial class ManualStore
{
    private readonly List<(string Source, string Chunk)> _chunks = new();

    public ManualStore(string manualsFolder)
    {
        if (!Directory.Exists(manualsFolder)) return;
        foreach (var file in Directory.EnumerateFiles(manualsFolder, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            foreach (var chunk in ChunkByParagraph(File.ReadAllText(file)))
                _chunks.Add((name, chunk));
        }
    }

    [Description("Search the appliance manuals for instructions relevant to the question. " +
                 "Always call this before answering a 'how do I…' maintenance question, and " +
                 "ground your answer ONLY in what it returns.")]
    public string SearchManuals(string query, int topK = 3)
    {
        var terms = Tokenize(query);
        var hits = _chunks
            .Select(c => (c.Source, c.Chunk, Score: Score(terms, c.Chunk)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
        return hits.Count == 0 ? "No relevant passage found in the manuals."
                                : string.Join("\n\n", hits.Select(h => $"[{h.Source}] {h.Chunk}"));
    }

    private static IEnumerable<string> ChunkByParagraph(string text) =>
        text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length > 0);

    private static int Score(HashSet<string> queryTerms, string chunk) => queryTerms.Count(Tokenize(chunk).Contains);

    private static HashSet<string> Tokenize(string s) =>
        WordRegex().Matches(s.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length > 2).ToHashSet();

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex WordRegex();
}
