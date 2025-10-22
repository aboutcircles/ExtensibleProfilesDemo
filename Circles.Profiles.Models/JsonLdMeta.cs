namespace Circles.Profiles.Models;

/// <summary>
/// JSON-LD constants. Treat these as immutable once live, because they're part of the signed preimage.
/// </summary>
public static class JsonLdMeta
{
    public const string SchemaOrg = "https://schema.org/";
    public const string ProfileContext = "https://aboutcircles.com/contexts/circles-profile/";
    public const string NamespaceContext = "https://aboutcircles.com/contexts/circles-namespace/";
    public const string LinkContext = "https://aboutcircles.com/contexts/circles-linking/";
    public const string ChatContext = "https://aboutcircles.com/contexts/circles-chat/";
    public const string MarketContext = "https://aboutcircles.com/contexts/circles-market/";
    public const string MarketAggregateContext = "https://aboutcircles.com/contexts/circles-market-aggregate/";
}