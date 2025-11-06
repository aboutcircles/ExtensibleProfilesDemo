using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Circles.Market.Api.Cart;

public static class CartEndpoints
{
    private const string ContentType = "application/ld+json; charset=utf-8";

    public static RouteGroupBuilder MapCartApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cart/v1");

        g.MapPost("/baskets", CreateBasket)
            .WithSummary("Create a new basket")
            .WithDescription("Body: { operator, chainId }")
            .Accepts<BasketCreateRequest>("application/ld+json", "application/json")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        g.MapGet("/baskets/{basketId}", GetBasket)
            .WithSummary("Get basket by id")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);

        g.MapPatch("/baskets/{basketId}", PatchBasket)
            .WithSummary("Patch basket (merge)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);

        g.MapPost("/baskets/{basketId}/validate", Validate)
            .WithSummary("Validate basket")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);

        g.MapPost("/baskets/{basketId}/checkout", Checkout)
            .WithSummary("Checkout basket -> create order id")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status410Gone);

        g.MapPost("/baskets/{basketId}/preview", Preview)
            .WithSummary("Preview Order snapshot without persisting")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);

        return g;
    }

    private static async Task<IResult> CreateBasket(HttpContext ctx, IBasketStore store, ILoggerFactory logger)
    {
        ctx.Response.ContentType = ContentType;
        ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        try
        {
            var req = await JsonSerializer.DeserializeAsync<BasketCreateRequest>(ctx.Request.Body,
                Circles.Profiles.Models.JsonSerializerOptions.JsonLd, ctx.RequestAborted) ?? new BasketCreateRequest();
            string? op = req.Operator is null ? null : Utils.NormalizeAddr(req.Operator);
            long? chain = req.ChainId;
            var b = store.Create(op, chain);
            var resp = new BasketCreateResponse { BasketId = b.BasketId };
            return Results.Json(resp, Circles.Profiles.Models.JsonSerializerOptions.JsonLd, contentType: ContentType);
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    private static IResult NotFoundOrGone((Basket basket, bool expired)? res)
    {
        if (res is null) return Results.StatusCode(StatusCodes.Status404NotFound);
        return res.Value.expired
            ? Results.StatusCode(StatusCodes.Status410Gone)
            : ResultsExtensions.JsonLd(res.Value.basket);
    }

    private static async Task<IResult> GetBasket(string basketId, IBasketStore store)
    {
        var res = store.Get(basketId);
        if (res is null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if (res.Value.expired) return Results.StatusCode(StatusCodes.Status410Gone);
        return ResultsExtensions.JsonLd(res.Value.basket);
    }

    private class BasketPatch
    {
        [JsonPropertyName("items")] public List<OrderItemPreview>? Items { get; set; }
        [JsonPropertyName("shippingAddress")] public PostalAddress? ShippingAddress { get; set; }
        [JsonPropertyName("billingAddress")] public PostalAddress? BillingAddress { get; set; }
        [JsonPropertyName("ageProof")] public PersonMinimal? AgeProof { get; set; }
        [JsonPropertyName("contactPoint")] public ContactPoint? ContactPoint { get; set; }
        [JsonPropertyName("ttlSeconds")] public int? TtlSeconds { get; set; }
    }

    private static readonly HashSet<string> AllowedPatchFields = new(StringComparer.Ordinal)
    {
        "items", "shippingAddress", "billingAddress", "ageProof", "contactPoint", "ttlSeconds"
    };

    private static async Task<IResult> PatchBasket(string basketId, HttpContext ctx, IBasketStore store)
    {
        ctx.Response.ContentType = ContentType;
        ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");

        // Enforce whitelist of top-level fields
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!AllowedPatchFields.Contains(prop.Name))
            {
                return Error(StatusCodes.Status400BadRequest, $"Unknown field '{prop.Name}'",
                    new { path = "/" + prop.Name });
            }
        }

        // Deserialize after validation
        var patch = doc.RootElement.Deserialize<BasketPatch>(Circles.Profiles.Models.JsonSerializerOptions.JsonLd) ??
                    new BasketPatch();

        try
        {
            var res = store.Get(basketId);
            if (res is null) return Results.StatusCode(StatusCodes.Status404NotFound);
            if (res.Value.expired) return Results.StatusCode(StatusCodes.Status410Gone);

            var b = store.Patch(basketId, b =>
            {
                if (patch.TtlSeconds.HasValue && patch.TtlSeconds.Value > 0) b.TtlSeconds = patch.TtlSeconds.Value;
                if (patch.Items is not null)
                {
                    if (patch.Items.Count > 500)
                        throw new ArgumentException("items.length must be <= 500");
                    foreach (var it in patch.Items)
                    {
                        if (it.OrderQuantity < 0 || it.OrderQuantity > 1_000_000)
                            throw new ArgumentException("orderQuantity out of bounds [1, 1_000_000]");
                        if (!string.IsNullOrEmpty(it.Seller)) it.Seller = Utils.NormalizeAddr(it.Seller!);
                    }

                    b.Items = patch.Items;
                }

                if (patch.ShippingAddress is not null) b.ShippingAddress = patch.ShippingAddress;
                if (patch.BillingAddress is not null) b.BillingAddress = patch.BillingAddress;
                if (patch.AgeProof is not null) b.AgeProof = patch.AgeProof;
                if (patch.ContactPoint is not null) b.ContactPoint = patch.ContactPoint;
            });

            return ResultsExtensions.JsonLd(b);
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return Results.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Error(StatusCodes.Status409Conflict, ex.Message);
        }
    }

    private static async Task<IResult> Validate(string basketId, IBasketStore store, ICartValidator validator)
    {
        var res = store.Get(basketId);
        if (res is null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if (res.Value.expired) return Results.StatusCode(StatusCodes.Status410Gone);
        try
        {
            var vr = validator.Validate(res.Value.basket);
            return ResultsExtensions.JsonLd(vr);
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, ex.Message);
        }
    }

    private static async Task<IResult> Preview(string basketId, string? buyer, IBasketStore store)
    {
        var res = store.Get(basketId);
        if (res is null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if (res.Value.expired) return Results.StatusCode(StatusCodes.Status410Gone);
        var order = ComposeOrder(res.Value.basket, NewId("ord_"), buyer);
        return ResultsExtensions.JsonLd(order);
    }

    private static async Task<IResult> Checkout(string basketId, string? buyer, IBasketStore store,
        ICartValidator validator)
    {
        var res = store.Get(basketId);
        if (res is null) return Results.StatusCode(StatusCodes.Status404NotFound);
        if (res.Value.expired) return Results.StatusCode(StatusCodes.Status410Gone);
        try
        {
            var vr = validator.Validate(res.Value.basket);
            if (!vr.Valid)
            {
                return Results.Json(new { error = "invalid basket", validation = vr }, contentType: ContentType,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!store.TryFreeze(basketId))
            {
                return Results.StatusCode(StatusCodes.Status409Conflict);
            }

            string orderId = NewId("ord_");
            var order = ComposeOrder(res.Value.basket, orderId, buyer);
            var payload = new { orderId = orderId, basketId = basketId, orderCid = (string?)null };
            return Results.Json(payload, Circles.Profiles.Models.JsonSerializerOptions.JsonLd, contentType: ContentType,
                statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, ex.Message);
        }
    }

    private static OrderSnapshot ComposeOrder(Basket b, string orderId, string? buyer)
    {
        var order = new OrderSnapshot
        {
            OrderNumber = orderId,
            Customer = new SchemaOrgPersonId { Id = buyer is null ? null : $"eip155:{b.ChainId}:{buyer}" },
            Broker = new SchemaOrgOrgId { Id = b.Operator is null ? null : $"eip155:{b.ChainId}:{b.Operator}" },
            BillingAddress = b.BillingAddress,
            ShippingAddress = b.ShippingAddress,
        };
        foreach (var it in b.Items)
        {
            if (it.OfferSnapshot is not null) order.AcceptedOffer.Add(it.OfferSnapshot);
            order.OrderedItem.Add(new OrderItemLine
            {
                OrderQuantity = it.OrderQuantity,
                OrderedItem = it.OrderedItem,
                ProductCid = it.ProductCid
            });
        }

        decimal total = 0m;
        string? currency = null;
        foreach (var offer in order.AcceptedOffer)
        {
            if (offer.Price.HasValue) total += offer.Price.Value * 1m;
            if (currency is null && offer.PriceCurrency is not null) currency = offer.PriceCurrency;
        }

        order.TotalPaymentDue = new PriceSpecification { Price = total, PriceCurrency = currency };
        return order;
    }

    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 22);

    private static IResult Error(int status, string message, object? details = null)
        => ResultsExtensions.JsonLd(new { error = message, details }, status);
}

internal static class ResultsExtensions
{
    public static IResult JsonLd(object payload, int statusCode = StatusCodes.Status200OK)
        => Results.Json(payload, Circles.Profiles.Models.JsonSerializerOptions.JsonLd,
            contentType: "application/ld+json; charset=utf-8", statusCode: statusCode);
}