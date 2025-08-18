using Nethereum.Signer;

namespace Circles.Profile.UI.Services;

/// <summary>
/// Very small utility that persists private keys as plain‑text files
/// under <c>./keys</c> and reloads them on startup.
/// </summary>
internal sealed class KeyStore
{
    private readonly string _dir;
    private readonly List<WalletKey> _wallets = new();

    public IReadOnlyList<WalletKey> Wallets => _wallets;

    public KeyStore(string? dir = null)
    {
        _dir = dir ?? Path.Combine(AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(_dir);
        Load();
    }

    private void Load()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*.key"))
        {
            string hex = File.ReadAllText(file).Trim();
            try
            {
                var key = new EthECKey(hex);
                _wallets.Add(new WalletKey(hex, key.GetPublicAddress()));
            }
            catch
            {
                /* ignore corrupt files */
            }
        }
    }

    public WalletKey CreateNew()
    {
        var key    = EthECKey.GenerateKey();
        var wallet = new WalletKey(key.GetPrivateKey(), key.GetPublicAddress());

        File.WriteAllText(Path.Combine(_dir, $"{wallet.Address}.key"), wallet.PrivateKey);
        _wallets.Add(wallet);
        return wallet;
    }
}

/// <summary>POCO wrapper for a wallet key pair.</summary>
public sealed record WalletKey(string PrivateKey, string Address)
{
    public override string ToString() => Address;
}