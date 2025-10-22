using System.Text;
using System.Text.Json;
using Circles.Profiles.Interfaces;
using Circles.Profiles.Models;
using Circles.Profiles.Models.Core;
using Circles.Profiles.Sdk;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Environment configuration
string? privKey = Environment.GetEnvironmentVariable("PRIVATE_KEY");
if (string.IsNullOrWhiteSpace(privKey))
{
    Console.WriteLine("[PayloadSignerApi] ERROR: PRIVATE_KEY environment variable is not set.");
}

builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.WriteIndented = true;
});

// DI: pinning and signing helpers
builder.Services.AddSingleton<IIpfsStore>(_ => new IpfsRpcApiStore());
builder.Services.AddSingleton<ILinkSigner, EoaLinkSigner>();

// Pluggable validation
builder.Services.AddSingleton<IPayloadValidator, JsonSyntaxValidator>();

var app = builder.Build();

app.MapPost("/sign", async (HttpRequest req, IIpfsStore ipfs, ILinkSigner signer, IPayloadValidator validator) =>
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

    CustomDataLink signed;
    try
    {
        signed = signer.Sign(draft, key);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to sign payload: {ex.Message}", statusCode: 500);
    }

    return Results.Json(signed);
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
