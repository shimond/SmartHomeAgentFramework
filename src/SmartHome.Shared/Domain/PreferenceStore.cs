using System.ComponentModel;
using System.Text.Json;

namespace SmartHome.Shared.Domain;

/// <summary>
/// Step 4 — long-term memory, IN-PROCESS. Survives a restart of THIS container (it writes
/// to a local file) but does NOT survive multiple containers — that limitation is exactly
/// what motivates Step 5's externalized, DB-backed conversation store.
/// </summary>
public sealed class PreferenceStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _prefs;
    private readonly Lock _gate = new();

    public PreferenceStore(string path)
    {
        _path = path;
        _prefs = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new()
            : new();
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _prefs[key.Trim().ToLowerInvariant()] = value;
            File.WriteAllText(_path, JsonSerializer.Serialize(_prefs));
        }
    }

    public string? Get(string key) => _prefs.TryGetValue(key.Trim().ToLowerInvariant(), out var v) ? v : null;
    public IReadOnlyDictionary<string, string> All() => _prefs;
}

public sealed class PreferenceTools(PreferenceStore store)
{
    [Description("Remember a long-term user preference, e.g. key 'movie night' value '20C and 15% brightness'.")]
    public string RememberPreference(string key, string value) { store.Set(key, value); return $"Got it — I'll remember that {key} = {value}."; }

    [Description("Recall a previously saved preference by key. Returns the stored value or 'none'.")]
    public string RecallPreference(string key) => store.Get(key) is { } v ? v : "none";

    [Description("List every preference remembered about the user.")]
    public string ListPreferences()
    {
        var all = store.All();
        return all.Count == 0 ? "No preferences remembered yet." : string.Join("; ", all.Select(kv => $"{kv.Key} = {kv.Value}"));
    }
}
