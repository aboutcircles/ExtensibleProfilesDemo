using Circles.Profiles.Interfaces;
using Circles.Profiles.Sdk;
using Circles.Profiles.Models.Core;
using Nethereum.Signer;
using System.Numerics;
using System.Threading.Tasks;

namespace Circles.Profile.UI.Services;

/// <summary>
/// Service for handling signatures and profile updates based on wallet type
/// </summary>
public class SignatureService
{
    private readonly IEthereumChainApi _chainApi;
    private readonly IIpfsStore _ipfsStore;
    private readonly INameRegistry _nameRegistry;

    public SignatureService(IEthereumChainApi chainApi, IIpfsStore ipfsStore, INameRegistry nameRegistry)
    {
        _chainApi = chainApi;
        _ipfsStore = ipfsStore;
        _nameRegistry = nameRegistry;
    }

    /// <summary>
    /// Signs a custom data link using the appropriate signer based on the wallet type
    /// </summary>
    /// <param name="draft">The draft link to sign</param>
    /// <param name="privateKey">Private key of the EOA or Safe owner</param>
    /// <param name="walletAddress">Address of the wallet (EOA or Safe)</param>
    /// <param name="isSafe">Whether the wallet is a Safe</param>
    /// <returns>The signed custom data link</returns>
    public async Task<CustomDataLink> SignLinkAsync(
        CustomDataLink draft, 
        string privateKey, 
        string walletAddress, 
        bool isSafe)
    {
        ISigner signer = isSafe
            ? new SafeSigner(walletAddress, new EthECKey(privateKey))
            : new EoaSigner(new EthECKey(privateKey));

        return await LinkSigning.SignAsync(draft, signer);
    }

    /// <summary>
    /// Sends a profile update transaction
    /// </summary>
    /// <param name="profile">The profile to update</param>
    /// <param name="privateKey">Private key of the EOA or Safe owner</param>
    /// <param name="walletAddress">Address of the wallet (EOA or Safe)</param>
    /// <param name="isSafe">Whether the wallet is a Safe</param>
    /// <returns>The transaction hash</returns>
    public async Task<string?> UpdateProfileAsync(
        Profile profile, 
        string privateKey, 
        string walletAddress, 
        bool isSafe)
    {
        var store = new ProfileStore(_ipfsStore, _nameRegistry);
        ISigner signer = isSafe
            ? new SafeSigner(walletAddress, new EthECKey(privateKey))
            : new EoaSigner(new EthECKey(privateKey));

        var (_, cid) = await store.SaveAsync(profile, signer);
        return cid;
    }

    /// <summary>
    /// Creates a namespace writer based on wallet type
    /// </summary>
    /// <param name="profile">The profile to write to</param>
    /// <param name="recipient">The recipient address</param>
    /// <param name="privateKey">Private key of the EOA or Safe owner</param>
    /// <param name="walletAddress">Address of the wallet (EOA or Safe)</param>
    /// <param name="isSafe">Whether the wallet is a Safe</param>
    /// <returns>The namespace writer</returns>
    public async Task<NamespaceWriter> CreateNamespaceWriterAsync(
        Profiles.Sdk.Profile profile,
        string recipient,
        string privateKey,
        string walletAddress,
        bool isSafe)
    {
        ILinkSigner signer;
        
        if (isSafe)
        {
            signer = new SafeLinkSigner(walletAddress, _chainApi);
        }
        else
        {
            signer = new DefaultLinkSigner();
        }

        return await NamespaceWriter.CreateAsync(profile, recipient, _ipfsStore, signer);
    }
}
