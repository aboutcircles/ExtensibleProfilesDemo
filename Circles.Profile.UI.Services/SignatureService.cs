using Circles.Profiles.Interfaces;
using Circles.Profiles.Sdk;
using Nethereum.Web3;
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
        if (isSafe)
        {
            var safeSigner = new SafeLinkSigner(walletAddress, _chainApi);
            return safeSigner.Sign(draft, privateKey);
        }
        else
        {
            var defaultSigner = new DefaultLinkSigner();
            return defaultSigner.Sign(draft, privateKey);
        }
    }

    /// <summary>
    /// Sends a profile update transaction
    /// </summary>
    /// <param name="profile">The profile to update</param>
    /// <param name="privateKey">Private key of the EOA or Safe owner</param>
    /// <param name="walletAddress">Address of the wallet (EOA or Safe)</param>
    /// <param name="isSafe">Whether the wallet is a Safe</param>
    /// <returns>The transaction hash</returns>
    public async Task<string> UpdateProfileAsync(
        Profiles.Sdk.Profile profile, 
        string privateKey, 
        string walletAddress, 
        bool isSafe)
    {
        var store = new ProfileStore(_ipfsStore, _nameRegistry);

        if (isSafe)
        {
            // For Safe wallets, we need to use GnosisSafeExecutor to send the transaction
            var web3 = new Web3(privateKey);
            var safeExecutor = new GnosisSafeExecutor(web3, walletAddress);
            
            // First, we pin the profile to IPFS and get the CID
            var profileCid = await store.PinProfileAsync(profile);
            
            // Then, we create the transaction data to update the registry
            var registryData = _nameRegistry.CreateUpdateMetadataDigestCalldata(walletAddress, profileCid);
            
            // Finally, we execute the transaction through the Safe
            return await safeExecutor.ExecTransactionAsync(
                _nameRegistry.ContractAddress,
                registryData,
                BigInteger.Zero);
        }
        else
        {
            // For EOA wallets, we can use the ProfileStore directly
            return await store.SaveAsync(profile, privateKey);
        }
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
