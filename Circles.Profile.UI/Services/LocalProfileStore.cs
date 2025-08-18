using System.Text.Json;

namespace Circles.Profile.UI.Services;

/// <summary>
/// Very small **local mirror** so edits are still around when the user
/// hasn’t published the profile on‑chain yet.
/// </summary>
internal sealed class LocalProfileStore
{
    private readonly string _dir;
    private readonly Dictionary<string, Profiles.Models.Profile> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public LocalProfileStore(string? dir = null)
    {
        _dir = dir ?? Path.Combine(AppContext.BaseDirectory, "profiles");
        Directory.CreateDirectory(_dir);

        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
            try
            {
                var p = JsonSerializer.Deserialize<Profiles.Models.Profile>(File.ReadAllText(f));
                if (p != null) _cache[Path.GetFileNameWithoutExtension(f)] = p;
            }
            catch { /* ignore */ }
    }

    public Profiles.Models.Profile GetOrCreate(string addr)
        => _cache.TryGetValue(addr, out var p)
            ? p
            : _cache[addr] = new Profiles.Models.Profile { Name = addr, Description = "" };

    public void Save(string addr, Profiles.Models.Profile p)
    {
        _cache[addr] = p;
        File.WriteAllText(Path.Combine(_dir, $"{addr}.json"),
            JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
    }
}