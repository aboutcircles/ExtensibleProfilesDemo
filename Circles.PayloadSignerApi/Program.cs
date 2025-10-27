using System.Text;
using System.Text.Json;
using Circles.PayloadSignerApi;
using Circles.PayloadSignerApi.Model;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Environment configuration
string? privKey = Environment.GetEnvironmentVariable("PRIVATE_KEY");
if (string.IsNullOrWhiteSpace(privKey))
{
    Console.WriteLine(
        "[PayloadSignerApi] WARNING: PRIVATE_KEY environment variable is not set. /sign will fail until configured.");
}

string? IPFS_RPC_URL = Environment.GetEnvironmentVariable("PFS_RPC_URL");
if (!string.IsNullOrWhiteSpace(IPFS_RPC_URL))
{
    Console.WriteLine(
        "[PayloadSignerApi] WARNING: PFS_RPC_URL environment variable is not set.");
}

string? RPC = Environment.GetEnvironmentVariable("RPC");
if (!string.IsNullOrWhiteSpace(RPC))
{
    Console.WriteLine(
        "[PayloadSignerApi] WARNING: RPC environment variable is not set.");
}

builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.WriteIndented = true;
});

// DI: pinning and signing helpers
builder.Services.AddSingleton<IIpfsStore>(_ => new IpfsRpcApiStore());

// Pluggable validation
builder.Services.AddSingleton<IPayloadValidator, JsonSyntaxValidator>();

// Buy-intent store (in-memory)
builder.Services.AddSingleton<IBuyIntentStore, InMemoryBuyIntentStore>();
Console.WriteLine("[BuyIntent] Using in-memory store (non-persistent).");

var app = builder.Build();

// --- Signing endpoint ---
app.MapPost("/sign", async (HttpRequest req, IIpfsStore ipfs, IPayloadValidator validator) =>
{
    // Ensure private key exists
    string? key = Environment.GetEnvironmentVariable("PRIVATE_KEY");
    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.Problem("PRIVATE_KEY environment variable is not set.", statusCode: 500);
    }

    // Read raw body as string
    using var reader = new StreamReader(req.Body, Encoding.UTF8);
    string raw = await reader.ReadToEndAsync();

    // Validate via pluggable validator
    var validation = await validator.ValidateAsync(raw, req.HttpContext.RequestAborted);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = "ValidationFailed", details = validation.ErrorMessage });
    }

    // Optional logical name via query (?name=...)
    string name = req.Query.TryGetValue("name", out var q) && !string.IsNullOrWhiteSpace(q)
        ? q.ToString()
        : "payload";

    // Pin payload to IPFS
    string cid;
    try
    {
        cid = await ipfs.AddStringAsync(raw, pin: true, req.HttpContext.RequestAborted);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to pin payload to IPFS: {ex.Message}", statusCode: 502);
    }

    // Build and sign the CustomDataLink
    var draft = new CustomDataLink
    {
        Name = name,
        Cid = cid,
        ChainId = Helpers.DefaultChainId,
        SignedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Nonce = CustomDataLink.NewNonce(),
        Encrypted = false
    };

    try
    {
        var signer = new EoaSigner(new Nethereum.Signer.EthECKey(key));
        var signed = await LinkSigning.SignAsync(draft, signer, req.HttpContext.RequestAborted);
        return Results.Json(signed);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to sign payload: {ex.Message}", statusCode: 500);
    }
});

// --- Buy intent endpoints ---
app.MapPost("/buy-intents", async (BuyIntentRequest req, IBuyIntentStore store, HttpContext ctx) =>
{
    // Basic validation
    if (string.IsNullOrWhiteSpace(req.Seller) || string.IsNullOrWhiteSpace(req.Namespace) ||
        string.IsNullOrWhiteSpace(req.Sku))
        return Results.BadRequest(new { error = "InvalidRequest", details = "seller, namespace and sku are required" });

    // Normalize inputs (addresses are lowercase per spec)
    string seller = req.Seller.Trim();
    string nsKey = req.Namespace.Trim().ToLowerInvariant();
    string sku = req.Sku.Trim();

    // Environment for chain access (read-only)
    string? rpcUrl = Environment.GetEnvironmentVariable("RPC");
    string? signerPriv = Environment.GetEnvironmentVariable("PRIVATE_KEY");
    if (string.IsNullOrWhiteSpace(signerPriv))
    {
        return Results.Problem("PRIVATE_KEY environment variable is required for registry reads.", statusCode: 500);
    }

    // Resolve seller profile → namespace index → head
    try
    {
        var ipfs = ctx.RequestServices.GetRequiredService<IIpfsStore>();

        // Use on-chain registry + IPFS to load the seller profile
        var registry = new NameRegistry(rpcUrl);
        var profileStore = new ProfileStore(ipfs, registry);
        var prof = await profileStore.FindAsync(seller, ctx.RequestAborted);
        if (prof is null)
        {
            return Results.NotFound(new { error = "SellerProfileNotFound", seller });
        }

        bool hasNs = prof.Namespaces.TryGetValue(nsKey, out var idxCid) && !string.IsNullOrWhiteSpace(idxCid);
        if (!hasNs)
        {
            return Results.NotFound(new { error = "NamespaceNotFound", seller, @namespace = nsKey });
        }

        var index = await Helpers.LoadIndex(idxCid, ipfs, ctx.RequestAborted);

        // Build a verified reader for the namespace
        var web3 = new Nethereum.Web3.Web3(rpcUrl);
        var chain = new EthereumChainApi(web3, Helpers.DefaultChainId);
        var verifier = new DefaultSignatureVerifier(chain);
        var reader = new DefaultNamespaceReader(index.Head, ipfs, verifier);

        string logicalName = $"product/{sku}";
        var link = await reader.GetLatestAsync(logicalName, ctx.RequestAborted);
        if (link is null)
        {
            return Results.NotFound(new
                { error = "OfferNotFound", details = "No product link found", seller, @namespace = nsKey, sku });
        }

        // Optional: ensure the signer matches the seller address
        if (!link.SignerAddress.Equals(seller, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error = "SignerMismatch", details = "Product was not signed by the seller avatar.", seller,
                linkSigner = link.SignerAddress
            });
        }

        // Optional: check for tombstone payload
        try
        {
            string raw = await ipfs.CatStringAsync(link.Cid, ctx.RequestAborted);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("@type", out var t) &&
                string.Equals(t.GetString(), "Tombstone", StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound(new
                {
                    error = "OfferDeleted", details = "Newest payload is a tombstone.", seller, @namespace = nsKey, sku
                });
            }
        }
        catch
        {
            // If payload fetch/parse fails, treat as not found for safety
            return Results.NotFound(new { error = "OfferPayloadUnavailable", seller, @namespace = nsKey, sku });
        }
    }
    catch (Exception ex)
    {
        return Results.Problem($"Offer validation failed: {ex.Message}", statusCode: 502);
    }

    var intent = BuyIntent.Create(seller, nsKey, sku);
    try
    {
        await store.SaveAsync(intent, ctx.RequestAborted);
        return Results.Created($"/buy-intents/{intent.Token}", BuyIntentResponse.From(intent));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to persist buy-intent: {ex.Message}", statusCode: 500);
    }
});

app.MapGet("/buy-intents/{token}", async (string token, IBuyIntentStore store, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(token)) return Results.BadRequest(new { error = "InvalidToken" });
    var intent = await store.GetAsync(token, ctx.RequestAborted);
    return intent is null ? Results.NotFound() : Results.Ok(BuyIntentResponse.From(intent));
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// ---- Pluggable validation interfaces/impls ----
public interface IPayloadValidator
{
    Task<PayloadValidationResult> ValidateAsync(string rawPayload, CancellationToken ct = default);
}

public sealed record PayloadValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static PayloadValidationResult Ok() => new(true, null);
    public static PayloadValidationResult Error(string msg) => new(false, msg);
}

/// <summary>
/// Simple example validator – checks the payload is syntactically valid JSON.
/// </summary>
public sealed class JsonSyntaxValidator : IPayloadValidator
{
    public Task<PayloadValidationResult> ValidateAsync(string rawPayload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return Task.FromResult(PayloadValidationResult.Error("Empty body"));

        try
        {
            // Just parse to ensure valid JSON; do not transform.
            _ = JsonDocument.Parse(rawPayload);
            return Task.FromResult(PayloadValidationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(PayloadValidationResult.Error($"Invalid JSON: {ex.Message}"));
        }
    }
}