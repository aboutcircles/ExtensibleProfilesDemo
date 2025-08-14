using Circles.Profiles.Sdk;
using System.Threading.Tasks;

namespace Circles.Profile.UI.Services;

/// <summary>
/// Service for handling profile updates and custom data link signing
/// </summary>
public class ProfileUpdateService
{
    private readonly SignatureService _signatureService;
    private readonly IIpfsStore _ipfsStore;

    public ProfileUpdateService(SignatureService signatureService, IIpfsStore ipfsStore)
    {
        _signatureService = signatureService;
        _ipfsStore = ipfsStore;
    }

    /// <summary>
    /// Signs and adds a custom data link to a namespace, then updates the profile
    /// </summary>
    /// <param name="profile">The profile to update</param>
    /// <param name="recipient">The recipient namespace address</param>
    /// <param name="linkName">The name of the link</param>
    /// <param name="jsonData">The JSON data to add</param>
    /// <param name="privateKey">Private key of the EOA or Safe owner</param>
    /// <param name="walletAddress">Address of the wallet (EOA or Safe)</param>
    /// <param name="isSafe">Whether the wallet is a Safe</param>
    /// <returns>The transaction hash of the profile update</returns>
    public async Task<string> SignAndAddDataLinkAsync(
        Profiles.Sdk.Profile profile,
        string recipient,
        string linkName,
        string jsonData,
        string privateKey,
        string walletAddress,
        bool isSafe)
    {
        // Create a namespace writer based on the wallet type
        var writer = await _signatureService.CreateNamespaceWriterAsync(
            profile, recipient, privateKey, walletAddress, isSafe);

        // Add the JSON data to the namespace
        await writer.AddJsonAsync(linkName, jsonData, privateKey);

        // Update the profile using the appropriate method based on wallet type
        return await _signatureService.UpdateProfileAsync(
            profile, privateKey, walletAddress, isSafe);
    }

    /// <summary>
    /// Creates and signs a custom data link that can be sent to another party for acceptance
    /// </summary>
    /// <param name="linkName">The name of the link</param>
    /// <param name="jsonData">The JSON data to add</param>
    /// <param name="privateKey">Private key of the EOA or Safe owner</param>
    /// <param name="walletAddress">Address of the wallet (EOA or Safe)</param>
    /// <param name="isSafe">Whether the wallet is a Safe</param>
    /// <returns>The signed custom data link</returns>
    public async Task<CustomDataLink> CreateSignedLinkAsync(
        string linkName,
        string jsonData,
        string privateKey,
        string walletAddress,
        bool isSafe)
    {
        // Add the JSON data to IPFS and get the CID
        var cid = await _ipfsStore.AddJsonAsync(jsonData, pin: true);

        // Create the draft link
        var draft = new CustomDataLink
        {
            Name = linkName,
            Cid = cid,
            ChainId = Helpers.DefaultChainId,
            SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = CustomDataLink.NewNonce(),
            Encrypted = false,
            SignerAddress = walletAddress
        };

        // Sign the link using the appropriate signer
        return await _signatureService.SignLinkAsync(
            draft, privateKey, walletAddress, isSafe);
    }
}
