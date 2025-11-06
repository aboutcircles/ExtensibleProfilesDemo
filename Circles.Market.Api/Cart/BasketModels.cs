using System.Text.Json.Serialization;

namespace Circles.Market.Api.Cart;

public static class CartJsonLd
{
    public static readonly string[] BasketContext =
    {
        "https://schema.org/",
        "https://aboutcircles.com/contexts/circles-market/"
    };
}

public enum BasketStatus
{
    Draft,
    Validating,
    Valid,
    CheckedOut,
    Expired
}

public class OfferSnapshot
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "Offer";
    public decimal? Price { get; set; }
    public string? PriceCurrency { get; set; }
    public SchemaOrgOrgId? Seller { get; set; }
    public string? CheckoutPageURLTemplate { get; set; }
}

public class SchemaOrgOrgId
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "Organization";
    [JsonPropertyName("@id")] public string? Id { get; set; }
}

public class OrderItemPreview
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "OrderItem";
    public int OrderQuantity { get; set; }
    public OrderedItemRef OrderedItem { get; set; } = new();
    public string? Seller { get; set; }
    public string? ProductCid { get; set; }
    public OfferSnapshot? OfferSnapshot { get; set; }
}

public class OrderedItemRef
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "Product";
    public string? Sku { get; set; }
}

public class PostalAddress
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "PostalAddress";
    public string? StreetAddress { get; set; }
    public string? AddressLocality { get; set; }
    public string? PostalCode { get; set; }
    public string? AddressCountry { get; set; }
}

public class PersonMinimal
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "Person";
    public string? BirthDate { get; set; } // ISO8601 date
}

public class ContactPoint
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "ContactPoint";
    public string? Email { get; set; }
    public string? Telephone { get; set; }
}

public class Basket
{
    [JsonPropertyName("@context")] public string[] Context { get; init; } = CartJsonLd.BasketContext;
    [JsonPropertyName("@type")] public string Type { get; init; } = "circles:Basket";

    public string BasketId { get; init; } = string.Empty;
    public string? Operator { get; set; }
    public long ChainId { get; set; }
    public string Status { get; set; } = nameof(BasketStatus.Draft);

    public List<OrderItemPreview> Items { get; set; } = new();
    public PostalAddress? ShippingAddress { get; set; }
    public PostalAddress? BillingAddress { get; set; }
    public PersonMinimal? AgeProof { get; set; }
    public ContactPoint? ContactPoint { get; set; }

    public long CreatedAt { get; init; }
    public long ModifiedAt { get; set; }
    public int TtlSeconds { get; set; } = 86400;
}

public class BasketCreateRequest
{
    public string? Operator { get; set; }
    public long? ChainId { get; set; }
}

public class BasketCreateResponse
{
    [JsonPropertyName("@type")] public string Type { get; init; } = "circles:Basket";
    public string BasketId { get; init; } = string.Empty;
}

public class ValidationRequirement
{
    public string Id { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Slot { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string[] ExpectedTypes { get; set; } = Array.Empty<string>();
    public Cardinality Cardinality { get; set; } = new();
    public string Status { get; set; } = "missing"; // ok | missing | typeMismatch | invalidShape
    public string? FoundAt { get; set; }
    public string? FoundType { get; set; }
}

public class Cardinality
{
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
}

public class RuleTrace
{
    public string Id { get; set; } = string.Empty;
    public bool Evaluated { get; set; }
    public string Result { get; set; } = string.Empty;
}

public class ValidationResult
{
    [JsonPropertyName("@context")] public string Context { get; init; } = "https://schema.org/";
    [JsonPropertyName("@type")] public string Type { get; init; } = "Thing";
    public string BasketId { get; set; } = string.Empty;
    public bool Valid { get; set; }
    public List<ValidationRequirement> Requirements { get; set; } = new();
    public List<MissingSlot> Missing { get; set; } = new();
    public List<RuleTrace> RuleTrace { get; set; } = new();
}

public class MissingSlot
{
    public string Slot { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string[] ExpectedTypes { get; set; } = Array.Empty<string>();
}

public class OrderSnapshot
{
    [JsonPropertyName("@context")] public string Context { get; init; } = "https://schema.org/";
    [JsonPropertyName("@type")] public string Type { get; init; } = "Order";

    public string OrderNumber { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = "https://schema.org/OrderProcessing";

    public SchemaOrgPersonId Customer { get; set; } = new();
    public SchemaOrgOrgId Broker { get; set; } = new();

    public List<OfferSnapshot> AcceptedOffer { get; set; } = new();
    public List<OrderItemLine> OrderedItem { get; set; } = new();

    public PostalAddress? BillingAddress { get; set; }
    public PostalAddress? ShippingAddress { get; set; }

    public PriceSpecification? TotalPaymentDue { get; set; }
    public string? PaymentUrl { get; set; }
}

public class PriceSpecification
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "PriceSpecification";
    public decimal? Price { get; set; }
    public string? PriceCurrency { get; set; }
}

public class OrderItemLine
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "OrderItem";
    public int OrderQuantity { get; set; }
    public OrderedItemRef OrderedItem { get; set; } = new();
    public string? ProductCid { get; set; }
}

public class SchemaOrgPersonId
{
    [JsonPropertyName("@type")] public string Type { get; set; } = "Person";
    [JsonPropertyName("@id")] public string? Id { get; set; }
}