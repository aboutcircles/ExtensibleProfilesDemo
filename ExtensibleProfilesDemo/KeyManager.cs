using System.Text.Json;

namespace ExtensibleProfilesDemo;

internal sealed class KeyManager
{
    private const string FileName = "keys.json";

    private record KeyFile(Dictionary<string, string> Keys, string Current);

    private KeyFile _state;

    public KeyManager()
    {
        if (File.Exists(FileName))
        {
            _state = JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(FileName))
                     ?? new(new(), "");
        }
        else
        {
            _state = new KeyFile(new(), "");
        }
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FileName, json);
    }

    public IEnumerable<(string alias, string priv)> List() =>
        _state.Keys.Select(kv => (kv.Key, kv.Value));

    public string? GetPrivateKey(string alias) =>
        _state.Keys.TryGetValue(alias, out string? priv) ? priv : null;

    public string? CurrentAlias => _state.Current.Length == 0 ? null : _state.Current;

    public void Add(string alias, string priv)
    {
        _state.Keys[alias] = priv;
        if (_state.Current.Length == 0)
        {
            _state = _state with { Current = alias };
        }

        Save();
    }

    public void Use(string alias)
    {
        if (!_state.Keys.ContainsKey(alias))
        {
            throw new ArgumentException($"No such alias: {alias}");
        }

        _state = _state with { Current = alias };
        Save();
    }
}