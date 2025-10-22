# Circles Product Catalog

A minimal, transport-agnostic product catalog that rides entirely on **Circles Profiles Protocol (CPP)** primitives.
Sellers publish products into a dApp/operator namespace; the dApp aggregates and displays them client-side.

---

## 0. Normative dependencies (CPP)

Everything not explicitly redefined here **MUST** follow CPP:

* **Identifiers & encodings** (addresses, CIDs, hex, time, sizes) — CPP §2
* **Registry ABI & semantics** — CPP §3
* **Data structures** (`Profile`, `NameIndexDoc`, `NamespaceChunk`, `CustomDataLink`) — CPP §4
* **Canonicalisation & payload hash** — CPP §5
* **Signing & verification** (EOA low-S; Safe ERC-1271 bytes path with `eth_call.from = signerAddress`) — CPP §6
* **Write semantics, rotation, atomicity, rebase-before-serialize** — CPP §7
* **Read semantics, ordering, replay scope** — CPP §8
* **Error handling & size caps (8 MiB)** — CPP §§11–12, §15

---

## 1. Scope & goals

* **One link per product** under an operator namespace.
* **Images are URLs** (generic URI—`ipfs://`, `https://`, `ar://`, `data:`, etc.) **or schema.org `ImageObject`** with non-HTTP transports.
* **Pricing lives in schema.org `Offer`**; when `price` is present, **`priceCurrency` is required** (ISO-4217).
* **Optional external live feeds** (generic URIs) for availability/inventory.
* **Transport-independent types**: no type name references to HTTP/IPFS.

Out of scope: variants, discounts/tax, multi-currency, carts/orders, ranking, pagination.

---

## 2. Roles

* **Seller avatar** — EOA or Safe that owns products.
* **Operator (dApp)** — Ethereum address under which sellers publish.
* **Client/Aggregator** — software resolving, verifying, assembling the catalog.

---

## 3. Namespaces & link naming (normative)

* **Namespace:** `(ownerAvatar = seller, namespaceKey = operator)` — both lowercase addresses (CPP §2.1).
* **One link per product:**
  `name = "product/<sku>"` → payload **schema.org `Product`** (JSON-LD with contexts; payload URLs can use any scheme).

### 3.1 Product identifier

* `sku` is seller-scoped and stable.
* Regex: `^[a-z0-9][a-z0-9-_]{0,62}$` (lowercase).

---

## 4. Payloads (normative)

### 4.1 `Product` (schema.org with circles-market extensions)

A single, self-contained **JSON-LD** product document.

```json
{
  "@context": [
    "https://schema.org/",
    "https://aboutcircles.com/contexts/circles-market/"
  ],
  "@type": "Product",
  "name": "Classic Tee",
  "description": "Unisex cotton tee.",
  "sku": "tee-classic-black",
  "image": [
    "ipfs://Qm.../front.jpg",
    { "@type": "ImageObject", "contentUrl": "ar://ABC123...", "url": "https://cdn.example.com/front.jpg" },
    "data:image/png;base64,iVBORw0KGgo..."
  ],
  "offers": [
    {
      "@type": "Offer",
      "price": 19.0,
      "priceCurrency": "EUR",
      "availabilityFeed": "https://api.example.com/availability/tee-classic-black",
      "inventoryFeed": "https://api.example.com/inventory/tee-classic-black",
      "url": "https://shop.example.com/products/tee-classic-black",
      "seller": { "@type": "Organization", "@id": "eip155:100:0xseller…", "name": "TeeCo" },
      "priceValidUntil": "2024-08-22T10:30:00Z",
      "dateModified": "2024-08-22T10:30:00Z"
    }
  ],
  "url": "https://shop.example.com/products/tee-classic-black",
  "dateCreated": "2024-08-22T10:00:00Z",
  "dateModified": "2024-08-22T10:30:00Z"
}
```

**Rules (normative):**

* `@context` **MUST** include **schema.org** and **[https://aboutcircles.com/contexts/circles-market/](https://aboutcircles.com/contexts/circles-market/)**. `@type` **MUST** be `"Product"`.
* `sku` **MUST** be present and match §3.1.
* `image[*]` accepts either:

    * a **string absolute URI** (any scheme; MUST be non-empty), or
    * an **`ImageObject`** with at least one of `contentUrl` (e.g., `ipfs://`, `ar://`, `data:`) or `url` (HTTP(S) mirror).
* `offers[*]` are schema.org `Offer`. When `price` is present, **`priceCurrency` is required** (ISO-4217).

    * `availability` is a schema.org IRI (e.g., `https://schema.org/InStock`).
    * `inventoryLevel` (optional) is `QuantitativeValue` with integer `value` (unit `C62` recommended for “count”).
    * `availabilityFeed` and `inventoryFeed` (optional) **MUST** be syntactically valid **URIs** (no restriction on scheme). When dereferenced, their responses **MUST** conform to §4.2.
* **Timestamps** inside Product/Offer **MUST** be ISO-8601/RFC-3339 UTC strings (`…Z`), fractional seconds optional.
* Entire payload object **MUST** be ≤ **8 MiB**.
* Writers **SHOULD** ensure referenced assets/feeds are reasonably retrievable (pin, mirror, or CDN as appropriate).

### 4.2 Live feeds (transport-independent, optional)

URIs in `availabilityFeed` / `inventoryFeed` may be dereferenced by clients using the transport the URI implies.
**Feed response shapes (normative):**

* **`availabilityFeed` →** the response body **MUST** be shaped **exactly** like the value of the non-feed field `availability` would be: a single **JSON string** that is the schema.org IRI (e.g.,
  `"https://schema.org/InStock"`).
* **`inventoryFeed` →** the response body **MUST** be shaped **exactly** like the value of the non-feed field `inventoryLevel` would be: a **`QuantitativeValue` JSON object**, e.g.,

  ```json
  { "@type": "QuantitativeValue", "value": 12, "unitCode": "C62" }
  ```

Responses SHOULD be small (well under 8 MiB).
Transport-specific caching (e.g., HTTP `ETag`, IPFS pinning) is allowed but **not required** by this spec.

---

## 5. Writer semantics (additions to CPP §7)

**Upsert product**

1. Serialize the **Product** JSON-LD; validate; **pin** to IPFS (CIDv0) as with all CPP objects.
2. Build `CustomDataLink`:

    * `name = "product/<sku>"`
    * `cid = <payload CID>`
    * `signerAddress = <seller avatar>`
    * plus required replay fields: `chainId`, `signedAt`, `nonce`, `signature`.
3. Append/replace in the head chunk of `(seller, operator)`; rotate at 100 links.
4. **Commit atomically** (pin head; update index; **rebase** latest seller profile; set `profile.namespaces[operator] = <indexCid>`; pin new profile; publish digest).

**Tombstone (optional deletion)**

* Publish with the **same link name** and a tombstone payload:

  ```json
  {
    "@context": "https://aboutcircles.com/contexts/circles-market/",
    "@type": "Tombstone",
    "sku": "<sku>",
    "at": 1724310700
  }
  ```
* Readers treat the product as deleted if the newest valid payload is a tombstone.

---

## 6. Reader semantics (single seller → single product)

1. Resolve seller profile digest; fetch profile; take `indexCid = profile.namespaces[operator]`.
2. From the namespace **index**, locate logical name `product/<sku>`.
3. Resolve the newest valid link (CPP §8 ordering) and fetch its payload:

    * If `@type == "Tombstone"` → deleted.
    * If `@type == "Product"` with required contexts → enforce §4.1 rules.
4. If `Offer.availabilityFeed` / `Offer.inventoryFeed` present, dereference them according to their scheme and, if they yield meaningful data, merge it best-effort (transport failures **MUST NOT** hide the product).

---

## 7. Aggregation (operator across many sellers)

Use the **Circles Profiles Aggregator** (CPA) exactly as specced, with:

* `operator = <operator address>`
* `avatars = <list of seller addresses>`
* `chainId = <verifying chain>`
* `window = { start, end }`

CPA emits verified links (with provenance) from namespaces `(seller, operator)`.

### 7.1 Catalog reduction (normative)

Given `AggResult.v1`:

1. **Filter** links where `link.name` matches `^product/([a-z0-9][a-z0-9-_]{0,62})$`.
2. **Fetch & validate** payload:

    * Accept `@type == "Product"` with `@context` including schema.org and circles-market; valid images; for any `Offer` with `price`, `priceCurrency` is present; size ≤ 8 MiB.
    * If newest valid payload is `Tombstone`, **exclude** that `(seller, sku)`.
3. **Group** by `(sellerAvatar = item.avatar, sku)` (sku from payload).
4. **Pick winner** using CPA’s total order (signedAt ↓, indexInChunk ↓, avatar ↑, linkKeccak ↑).
5. **Compose** a deterministic catalog item with provenance.

### 7.2 Catalog aggregate output

```json
{
  "@context": "https://aboutcircles.com/contexts/circles-market-aggregate/",
  "@type": "AggregatedCatalog",
  "operator": "0x<operator>",
  "chainId": 100,
  "window": { "start": 1724310000, "end": 1726902000 },
  "avatarsScanned": ["0x<seller1>", "0x<seller2>"],
  "products": [
    {
      "@type": "AggregatedCatalogItem",
      "seller": "0x<seller1>",
      "productCid": "Qm<cidv0>",
      "publishedAt": 1724410000,
      "linkKeccak": "0x<64-hex>",
      "product": { /* schema.org Product per §4.1 */ }
    }
  ],
  "errors": [ /* passthrough from CPA */ ]
}
```

* **Sorting of `products`**: default to the winner link’s CPA order (newest-first). UI may re-sort arbitrarily.

---

## 8. Conformance checklist

1. **Addresses**: lowercase, regex per CPP §2.1.
2. **Link name**: `product/<sku>`; `sku` matches `^[a-z0-9][a-z0-9-_]{0,62}$`.
3. **Payload**: schema.org **`Product`** JSON-LD with contexts; images valid (absolute URI strings or `ImageObject` with `contentUrl`/`url`); any `Offer` with `price` has **`priceCurrency`**; size ≤ 8 MiB; ISO-8601/RFC-3339 timestamps where present.
4. **Writes**: rotation at 100; **rebase before serialize**; atomic commit; pinning per CPP §7.
5. **Verification**: canonical bytes; EOA low-S; Safe ERC-1271 (bytes) with `from = signerAddress`; chain domain match; tuple-scoped nonce replay (CPP §8).
6. **Aggregation**: CPA inputs/outputs respected; reduction uses CPA total order; tombstones excluded.
7. **Transport independence**: No requirement on URI scheme; implementations MUST NOT reject by scheme alone; live feeds are optional hints.

---

## 9. Examples

### 9.1 Publish product (seller → operator namespace)

* Namespace: `(0xseller…, 0xoperator…)`
* Link:

    * `name = "product/tee-classic-black"`
    * `cid = QmPayloadCid` (of **Product** JSON-LD)
    * `signerAddress = 0xseller…`
    * `chainId`, `signedAt`, `nonce` set per CPP

### 9.2 `Product` (minimal)

```json
{
  "@context": [
    "https://schema.org/",
    "https://aboutcircles.com/contexts/circles-market/"
  ],
  "@type": "Product",
  "name": "Classic Tee",
  "sku": "tee-classic-black",
  "image": ["ipfs://Qm...", "https://cdn.example.com/img.jpg"],
  "offers": [{ "@type": "Offer", "price": 19.0, "priceCurrency": "EUR" }],
  "dateCreated": "2024-08-22T10:00:00Z",
  "dateModified": "2024-08-22T10:00:00Z"
}
```

### 9.3 Tombstone (optional)

```json
{
  "@context": "https://aboutcircles.com/contexts/circles-market/",
  "@type": "Tombstone",
  "sku": "tee-classic-black",
  "at": 1724310700
}
```

---

## 10. Notes

* Payloads themselves are stored/pinned as CPP objects (CIDv0) for consistency and auditability, **but** the URIs they *reference* are transport-agnostic.
* If a URI’s transport isn’t supported by a given client, that client may skip fetching it without failing the product as a whole.
* **JSON-LD contexts** are part of the signed preimage; treat them as immutable once live:
  `https://aboutcircles.com/contexts/{circles-market|circles-market-aggregate|circles-profile|circles-namespace|circles-linking|circles-chat}/`.
* **Time types:** inside Product/Offer are ISO-8601 UTC strings; aggregation window and tombstone `at` remain unix seconds.
