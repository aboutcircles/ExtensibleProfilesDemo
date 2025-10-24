using Circles.Profiles.Models.Market;

namespace Circles.Profiles.Market;

public sealed class ProductBuilder
{
    private string _sku = string.Empty;
    private string _name = string.Empty;
    private readonly List<ImageRef> _images = new();
    private readonly List<SchemaOrgOffer> _offers = new();

    public ProductBuilder WithSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException(nameof(sku));
        _sku = sku;
        return this;
    }

    public ProductBuilder WithName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
        _name = name;
        return this;
    }

    public ProductBuilder AddImage(Uri absolute)
    {
        if (absolute == null || !absolute.IsAbsoluteUri) throw new ArgumentException(nameof(absolute));
        _images.Add(new ImageRef { Url = absolute });
        return this;
    }

    public ProductBuilder AddImageObject(string contentUrl, string? url = null)
    {
        if (string.IsNullOrWhiteSpace(contentUrl)) throw new ArgumentException(nameof(contentUrl));
        _images.Add(new ImageRef { Object = new SchemaOrgImageObject { ContentUrl = contentUrl, Url = url } });
        return this;
    }

    public ProductBuilder WithOffer(decimal price, string currency, string checkout)
    {
        bool badCur = string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(c => c >= 'A' && c <= 'Z');
        if (badCur) throw new ArgumentException("currency must be ISO-4217 (e.g., EUR)");

        if (string.IsNullOrWhiteSpace(checkout) || !Uri.TryCreate(checkout, UriKind.Absolute, out _))
            throw new ArgumentException("checkout must be absolute URI");

        _offers.Clear();
        _offers.Add(new SchemaOrgOffer { Price = price, PriceCurrency = currency, Checkout = checkout });
        return this;
    }

    public SchemaOrgProduct Build()
    {
        if (string.IsNullOrWhiteSpace(_sku)) throw new InvalidOperationException("sku is required");
        if (string.IsNullOrWhiteSpace(_name)) throw new InvalidOperationException("name is required");
        return new SchemaOrgProduct
        {
            Sku = _sku,
            Name = _name,
            Image = _images.ToList(),
            Offers = _offers.ToList()
        };
    }
}
