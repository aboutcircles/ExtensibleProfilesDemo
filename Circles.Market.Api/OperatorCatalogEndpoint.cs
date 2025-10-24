using System.Net;
using System.Text.Json;
using Circles.Profiles.Market;
using Circles.Profiles.Models.Market;

namespace Circles.Market.Api;

/// <summary>
/// GET /api/operator/{op}/catalog
///
/// Aggregates verified product links across many avatars under the operator namespace,
/// applies a time window and chain domain, reduces to a deterministic AggregatedCatalog,
/// and returns a **paged** JSON-LD object.
/// 
/// Query:
/// - avatars: repeated query param, at least one (e.g. ?avatars=0x..&avatars=0x..)
/// - chainId: long (default 100)
/// - start: unix seconds inclusive (default 0)
/// - end: unix seconds inclusive (default now)
/// - pageSize: 1..100 (default 20)
/// - cursor: opaque base64 { "start": <int> } (wins over offset)
/// - offset: 0-based (alternative to cursor)
///
/// Response: application/ld+json; charset=utf-8
/// Headers: Link, X-Next-Cursor
/// </summary>
public static class OperatorCatalogEndpoint
{
    public static async Task Handle(
        string op,
        long? chainId,
        long? start,
        long? end,
        int? pageSize,
        string? cursor,
        int? offset,
        HttpContext ctx,
        OperatorCatalogService opCatalog,
        CancellationToken ct)
    {
        try
        {
            var avatarsRaw = ctx.Request.Query["avatars"];
            var avatarList = avatarsRaw
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!)
                .ToArray();

            bool hasAvatars = avatarList is { Length: > 0 };
            if (!hasAvatars)
            {
                await WriteError(ctx, StatusCodes.Status400BadRequest,
                    "At least one avatars query parameter is required");
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long chain = chainId ?? 100;
            long winStart = start ?? 0;
            long winEnd = end ?? now;

            bool windowOk = winStart <= winEnd;
            if (!windowOk)
            {
                await WriteError(ctx, StatusCodes.Status400BadRequest, "start must be <= end");
                return;
            }

            int size = Math.Clamp(pageSize ?? 20, 1, 100);
            int startIndex = 0;
            if (!string.IsNullOrEmpty(cursor))
            {
                try
                {
                    var payload = Convert.FromBase64String(cursor);
                    var el = JsonSerializer.Deserialize<JsonElement>(payload);
                    startIndex = el.GetProperty("start").GetInt32();
                    if (startIndex < 0)
                    {
                        await WriteError(ctx, StatusCodes.Status400BadRequest, "cursor.start must be >= 0");
                        return;
                    }
                }
                catch
                {
                    await WriteError(ctx, StatusCodes.Status400BadRequest, "Invalid cursor");
                    return;
                }
            }
            else if (offset.HasValue)
            {
                if (offset.Value < 0)
                {
                    await WriteError(ctx, StatusCodes.Status400BadRequest, "offset must be >= 0");
                    return;
                }

                startIndex = offset.Value;
            }

            var (avatarsScanned, products, errors) =
                await opCatalog.AggregateAsync(op, avatarList, chain, winStart, winEnd, ct);

            int total = products.Count;

            bool beyondEnd = startIndex > total;
            if (beyondEnd)
            {
                ctx.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                return;
            }

            int endIndex = Math.Min(startIndex + size, total);
            string? next = endIndex < total
                ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { start = endIndex }))
                : null;
            if (next is not null)
            {
                var basePath = $"/api/operator/{WebUtility.UrlEncode(op)}/catalog";
                string nextUrl = $"{basePath}?pageSize={size}&cursor={WebUtility.UrlEncode(next)}";
                foreach (var av in avatarList)
                {
                    nextUrl += $"&avatars={WebUtility.UrlEncode(av)}";
                }

                nextUrl += $"&chainId={chain}&start={winStart}&end={winEnd}";
                ctx.Response.Headers.Append("Link", $"<{nextUrl}>; rel=\"next\"");
                ctx.Response.Headers.Append("X-Next-Cursor", next);
            }

            ctx.Response.ContentType = "application/ld+json; charset=utf-8";

            var page = products.Skip(startIndex).Take(endIndex - startIndex).ToList();

            var aggPayload = new AggregatedCatalog
            {
                Operator = Utils.NormalizeAddr(op),
                ChainId = chain,
                Window = new AggregatedCatalogWindow { Start = winStart, End = winEnd },
                AvatarsScanned = avatarsScanned,
                Products = page,
                Errors = errors
            };

            await JsonSerializer.SerializeAsync(ctx.Response.Body, aggPayload,
                Circles.Profiles.Models.JsonSerializerOptions.JsonLd, ct);
        }
        catch (ArgumentException ex)
        {
            await WriteError(ctx, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (PayloadTooLargeException ex)
        {
            await WriteError(ctx, StatusCodes.Status413PayloadTooLarge, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            await WriteError(ctx, StatusCodes.Status502BadGateway, ex.Message);
        }
        catch (IOException ex)
        {
            await WriteError(ctx, StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    private static async Task WriteError(HttpContext ctx, int status, string message, object? details = null)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/ld+json; charset=utf-8";
        var payload = JsonSerializer.Serialize(new { error = message, details });
        await ctx.Response.WriteAsync(payload);
    }
}