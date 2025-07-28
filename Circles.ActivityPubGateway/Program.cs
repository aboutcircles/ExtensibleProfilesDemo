using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Sdk;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Net.Http.Headers;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Web3.Accounts;

var builder = WebApplication.CreateBuilder(args);

/* ---------------- dependency wiring ---------------- */

// read‑only access is enough → throw‑away key is fine
var acc = new Account(EthECKey.GenerateKey());
string rpcUrl = Environment.GetEnvironmentVariable("RPC_URL")
                ?? "https://rpc.aboutcircles.com";

builder.Services.AddSingleton<IIpfsStore>(_ => new IpfsStore());
builder.Services.AddSingleton<INameRegistry>(_ => new NameRegistry(acc.PrivateKey, rpcUrl));
builder.Services.AddSingleton<IProfileStore, ProfileStore>();

// crypto helpers for on‑the‑fly signature checks in the outbox
builder.Services.AddSingleton<IChainApi>(_ =>
{
    var web3 = new Nethereum.Web3.Web3(rpcUrl);
    return new EthereumChainApi(web3, Helpers.DefaultChainId);
});
builder.Services.AddSingleton<ISignatureVerifier, DefaultSignatureVerifier>();

builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

static string PemFromHex(string hex)
{
    // expect 0x04… (130 bytes); strip optional 0x
    if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        hex = hex[2..];

    byte[] der = hex.HexToByteArray();
    string b64 = Convert.ToBase64String(der);
    return $"-----BEGIN PUBLIC KEY-----\n{b64}\n-----END PUBLIC KEY-----";
}

/* ------------- helper: map Profile → ActivityPub Person ------------- */
static object ToActor(Profile p, string avatar, string selfUrl)
{
    // pick the *first* signing key as ActivityPub public key
    var firstKey = p.SigningKeys.Values.FirstOrDefault();
    string? pem = firstKey is null ? null : PemFromHex(firstKey.PublicKey);

    return new
    {
        @context = new[]
        {
            "https://www.w3.org/ns/activitystreams",
            "https://w3id.org/security/v1"
        },
        id = selfUrl,
        type = "Person",
        inbox = $"{selfUrl}/inbox",
        outbox = $"{selfUrl}/outbox",
        following = $"{selfUrl}/following",
        followers = $"{selfUrl}/followers",
        preferredUsername = avatar,
        name = p.Name,
        summary = p.Description,
        publicKey = pem is null
            ? null
            : new
            {
                id = $"{selfUrl}#main-key",
                owner = selfUrl,
                publicKeyPem = pem
            }
    };
}

/* ------------- helper: link -> ActivityPub Create<Note> ------------- */
static async Task<object> ConvertLinkAsync(
    CustomDataLink l,
    IIpfsStore ipfs,
    string actorUrl)
{
    string raw = await ipfs.CatStringAsync(l.Cid);
    var msg = JsonSerializer.Deserialize<ChatMessage>(raw, Helpers.JsonOpts)
              ?? new ChatMessage { Text = raw }; // fall‑back

    var noteObj = new
    {
        id = $"ipfs://{l.Cid}",
        type = "Note",
        content = msg.Text,
        to = new[] { msg.To }
    };

    return new
    {
        id = $"ipfs://{l.Cid}#activity",
        type = "Create",
        actor = actorUrl,
        published = DateTimeOffset.FromUnixTimeSeconds(l.SignedAt).UtcDateTime,
        to = new[] { msg.To },
        cc = Array.Empty<string>(),
        @object = noteObj
    };
}

/* -------------------- WebFinger -------------------- */
app.MapGet("/.well-known/webfinger", async (
    HttpRequest req,
    HttpResponse res,
    IProfileStore store) =>
{
    if (!req.Query.TryGetValue("resource", out var resource))
        return Results.BadRequest();

    // expected format acct:0xavatar@host
    string[] parts = resource.ToString().Split(':', 2);
    if (parts.Length != 2 || !parts[0].Equals("acct", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest();

    string[] acct = parts[1].Split('@', 2);
    if (acct.Length != 2) return Results.BadRequest();

    string avatar = acct[0];
    string host = $"{req.Scheme}://{req.Host}";
    string actor = $"{host}/users/{avatar}";

    var prof = await store.FindAsync(avatar);
    if (prof is null) return Results.NotFound();

    var jrd = new
    {
        subject = resource.ToString(),
        links = new[]
        {
            new { rel = "self", type = "application/activity+json", href = actor }
        }
    };
    res.ContentType = "application/jrd+json";
    return Results.Json(jrd);
});

/* -------------------- Actor -------------------- */
app.MapGet("/users/{avatar}", async (
    string avatar,
    HttpRequest req,
    HttpResponse res,
    IProfileStore store) =>
{
    var prof = await store.FindAsync(avatar);
    if (prof is null) return Results.NotFound();

    string selfUrl = $"{req.Scheme}://{req.Host}/users/{avatar}";
    res.Headers[HeaderNames.ContentType] = "application/activity+json; charset=utf-8";
    return Results.Json(ToActor(prof, avatar, selfUrl));
});

/* -------------------- Outbox (page 1 only) -------------------- */
app.MapGet("/users/{avatar}/outbox", async (
    string avatar,
    HttpRequest req,
    HttpResponse res,
    IProfileStore store,
    ISignatureVerifier verifier,
    IIpfsStore ipfs) =>
{
    var prof = await store.FindAsync(avatar);
    if (prof is null) return Results.NotFound();

    const int PageSize = 20;
    var allLinks = new List<CustomDataLink>();

    /* collect newest links from *all* namespaces – that is the author’s outbox */
    foreach (var nsCid in prof.Namespaces.Values)
    {
        var idx = await Helpers.LoadIndex(nsCid, ipfs);
        var reader = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

        await foreach (var l in reader.StreamAsync().WithCancellation(req.HttpContext.RequestAborted))
        {
            allLinks.Add(l);
            if (allLinks.Count >= PageSize) break;
        }

        if (allLinks.Count >= PageSize) break;
    }

    string selfUrl = $"{req.Scheme}://{req.Host}/users/{avatar}";
    string outboxUrl = $"{selfUrl}/outbox";

    var activities = await Task.WhenAll(
        allLinks
            .OrderByDescending(l => l.SignedAt)
            .Take(PageSize)
            .Select(l => ConvertLinkAsync(l, ipfs, selfUrl)));

    var collection = new
    {
        @context = "https://www.w3.org/ns/activitystreams",
        id = outboxUrl,
        type = "OrderedCollection",
        totalItems = activities.Length,
        first = new
        {
            id = $"{outboxUrl}?page=1",
            type = "OrderedCollectionPage",
            orderedItems = activities
        }
    };

    res.Headers[HeaderNames.ContentType] = "application/activity+json; charset=utf-8";
    return Results.Json(collection);
});

app.Run();