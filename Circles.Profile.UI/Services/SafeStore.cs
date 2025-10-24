using System.Text.Json;
using Circles.Profiles.Safe;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace Circles.Profile.UI.Services;

/// <summary>
/// Persists already‑deployed single‑owner Safes in <c>safes.json</c>
/// and deploys new ones via the shared <see cref="SafeHelper"/> logic.
/// </summary>
internal sealed class SafeStore
{
    private readonly string _file;
    private readonly List<SafeInfo> _safes = new();
    private readonly Web3 _web3;
    private readonly Account _deployer;

    public IReadOnlyList<SafeInfo> Safes => _safes;

    public SafeStore(Web3 web3, Account deployer, string? dir = null)
    {
        _web3 = web3 ?? throw new ArgumentNullException(nameof(web3));
        _deployer = deployer ?? throw new ArgumentNullException(nameof(deployer));

        var root = dir ?? AppContext.BaseDirectory;
        _file = Path.Combine(root, "safes.json");
        Directory.CreateDirectory(root);

        Load();
    }

    private void Load()
    {
        if (!File.Exists(_file)) return;

        try
        {
            var json = File.ReadAllText(_file);
            var list = JsonSerializer.Deserialize<List<SafeInfo>>(json);
            if (list is { Count: > 0 }) _safes.AddRange(list);
        }
        catch
        {
            /* ignore corrupt store – user can redeploy */
        }
    }

    public async Task<SafeInfo> CreateAsync(WalletKey owner, CancellationToken ct = default)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));

        /* 1) fund owner so the Safe can pay future gas (idempotent) */
        try
        {
            await SafeHelper.FundAsync(_web3, _deployer, owner.Address, 0.001m, ct);
        }
        catch
        {
            /* not fatal if it fails – maybe already funded */
        }

        /* 2) deploy Safe (threshold = 1) */
        string safeAddr = await SafeHelper.DeploySafe141OnGnosisAsync(
            _web3,
            [_deployer.Address, owner.Address],
            threshold: 1,
            ct);

        /* 3) persist locally */
        var info = new SafeInfo(safeAddr, owner.Address);
        _safes.Add(info);
        Save();
        return info;
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_safes,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_file, json);
    }
}

internal sealed record SafeInfo(string Address, string Owner);