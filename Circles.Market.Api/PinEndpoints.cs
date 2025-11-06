using System.Net;
using Circles.Profiles.Interfaces;

namespace Circles.Market.Api;

public static class PinEndpoints
{
    private const int MaxUploadBytes = 8 * 1024 * 1024; // 8 MiB cap

    public static IEndpointRouteBuilder MapPinApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pin", Pin)
            .WithSummary("Pin user-authored JSON-LD to IPFS and return its CID")
            .WithDescription("Accepts raw JSON-LD; verifies payload shape against allowed user-generated models only, stores via IPFS /add + /pin/add, returns { cid } JSON.")
            .Accepts<string>("application/ld+json", "application/json")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .WithOpenApi();
        return app;
    }

    private static async Task<IResult> Pin(HttpRequest req, IIpfsStore ipfs, IJsonLdShapeVerifier verifier, CancellationToken ct)
    {
        try
        {
            // Read request body with an explicit 8 MiB cap
            await using var body = req.Body;
            using var ms = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            long total = 0;
            while ((read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                total += read;
                if (total > MaxUploadBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
                ms.Write(buffer, 0, read);
            }
            var bytes = ms.ToArray();

            // Verify JSON-LD shape before pinning (allow only user-generated shapes)
            if (!verifier.CanPin(bytes, out var reason))
            {
                return Results.Problem(title: "Unsupported JSON-LD shape", detail: reason, statusCode: (int)HttpStatusCode.BadRequest);
            }

            // Delegate pinning to the IIpfsStore (pin=true by default)
            string cid = await ipfs.AddBytesAsync(bytes, pin: true, ct);
            return Results.Json(new { cid });
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(title: "Invalid request", detail: ex.Message, statusCode: (int)HttpStatusCode.BadRequest);
        }
        catch (PayloadTooLargeException)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
    }
}
