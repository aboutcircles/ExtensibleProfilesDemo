# Market API – Offer Listing Endpoint

The **Offer Listing** endpoint provides a convenient way to retrieve offers from multiple marketplace sellers in a single request.

## Endpoint

```
GET https://[host]/market/api/operator/[marketplace operator]/catalog?avatars=[seller1]&avatars=[seller2]&...
```

- **[host]** – Your API host (e.g., `api.example.com`).
- **[marketplace operator]** – The operator address that owns the catalog.
- **avatars** – One or more marketplace seller addresses whose offers you want to include. The parameter can be repeated to query multiple sellers.

### What it does
- Aggregates offers from the specified sellers.
- Returns a coherent list of offers, each enriched with the seller’s avatar information.
- Useful for building UI components that display a combined marketplace view or for backend services that need to process offers from many profiles at once.

### Example Request

```
GET https://api.example.com/market/api/operator/0xOperatorAddress/catalog?avatars=0xSellerA&avatars=0xSellerB
```

### Pagination & Additional Parameters

- **page** (optional, integer, default 1): Page number for paginated results.  
- **pageSize** (optional, integer, default 20, max 100): Number of offers per page.  
- **sort** (optional, string): Field to sort by, e.g., `price`, `createdAt`. Prefix with `-` for descending order.  
- **filter** (optional, string): Simple filter expression, e.g., `price>0.1` to only include offers above a certain price.

These parameters can be combined with the `avatars` query. Example:

```
GET https://api.example.com/market/api/operator/0xOperatorAddress/catalog?avatars=0xSellerA&avatars=0xSellerB&page=2&pageSize=10&sort=-price
```

The response will include pagination metadata:

```json
{
  "offers": [ … ],
  "page": 2,
  "pageSize": 10,
  "totalPages": 5,
  "totalOffers": 45
}
```

### Example Response (simplified)

```json
{
  "offers": [
    {
      "seller": "0xSellerA",
      "offerId": "123",
      "title": "Cool NFT",
      "price": "0.5 ETH",
      "avatar": "https://ipfs.io/ipfs/Qm..."
    },
    {
      "seller": "0xSellerB",
      "offerId": "456",
      "title": "Rare Token",
      "price": "1.2 ETH",
      "avatar": "https://ipfs.io/ipfs/Qn..."
    }
  ]
}
```

### Use Cases
- **Marketplace UI:** Show a unified list of items from several sellers.
- **Analytics:** Pull offers from many profiles for market analysis.
- **Bots/Automation:** Aggregate offers for price comparison or arbitrage.

---

For more details on other endpoints, see the main repository README or the source code in this folder.
