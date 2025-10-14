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
* **Images are URLs** (generic URI—`ipfs://`, `https://`, `ar://`, `data:`, etc.).
* **One numeric price** per product (no currency/tax semantics in this spec).
* **Optional external stock URL** (generic URI) returning live availability.
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
  `name = "product/<productId>"` → payload `product.v1` (pinned to IPFS as a CPP object; payload URLs can use any scheme).

### 3.1 Product identifier

* `productId` is seller-scoped and stable.
* Regex: `^[a-z0-9][a-z0-9-_]{0,62}$` (lowercase).

---

## 4. Payloads (normative)

### 4.1 `product.v1`

A single, self-contained product document.

```json
{
  "type": "product.v1",
  "schemaVersion": "1.2",
  "productId": "tee-classic-black",
  "title": "Classic Tee",
  "description": "Unisex cotton tee.",
  "images": [
    "ipfs://Qm.../front.jpg",
    "https://cdn.example.com/back.jpg",
    "ar://ABC123...",
    "data:image/png;base64,iVBORw0KGgo..."
  ],
  "price": 19.0,
  "stockUrl": "ipfs://QmStockPointerOrHttpsOrOther",
  "createdAt": 1724310000,
  "updatedAt": 1724310000
}
```

**Rules (normative):**

* `images[*]` and `stockUrl` (if present) **MUST** be syntactically valid **URIs**. No restriction on scheme.
  *Guidance:* Implementations MAY prefer transports they understand (e.g., `https://`, `ipfs://`) but **must not** reject others solely by scheme.
* `price` **MUST** be a finite, non-negative JSON number.
* Entire payload object **MUST** be ≤ **8 MiB**.
* Writers **SHOULD** ensure referenced assets are reasonably retrievable (pin, mirror, or CDN as appropriate for the scheme).

### 4.2 `stock.v1` (transport-independent, optional)

Returned when dereferencing `stockUrl` (using whatever transport the URI implies).

```json
{
  "type": "stock.v1",
  "productId": "tee-classic-black",
  "policy": "finite",
  "quantity": 12,
  "updatedAt": 1724310600
}
```

**Rules (normative):**

* `policy ∈ {"finite","infinite","preorder"}`. If `policy="infinite"`, `quantity` is ignored (SHOULD be `0`).
* Response size SHOULD be small (well under 8 MiB).
* Transport-specific caching (e.g., HTTP `ETag`, IPFS pinning) is allowed but **not required** by this spec.

---

## 5. Writer semantics (additions to CPP §7)

**Upsert product**

1. Serialize `product.v1`; validate; **pin** to IPFS (CIDv0) as with all CPP objects.
2. Build `CustomDataLink`:

    * `name = "product/<productId>"`
    * `cid = <payload CID>`
    * `signerAddress = <seller avatar>`
    * plus required replay fields: `chainId`, `signedAt`, `nonce`.
3. Append/replace in the head chunk of `(seller, operator)`; rotate at 100 links.
4. **Commit atomically** (pin head; update index; **rebase** latest seller profile; set `profile.namespaces[operator] = <indexCid>`; pin new profile; publish digest).

**Tombstone (optional deletion)**

* Publish with the **same link name** and a tombstone payload:

  ```json
  { "type": "tombstone.v1", "schemaVersion": "1.0", "productId": "<id>", "at": 1724310700 }
  ```
* Readers treat the product as deleted if the newest valid payload is a tombstone.

---

## 6. Reader semantics (single seller → single product)

1. Resolve seller profile digest; fetch profile; take `indexCid = profile.namespaces[operator]`.
2. From the namespace **index**, locate logical name `product/<productId>`.
3. Resolve the newest valid link (CPP §8 ordering) and fetch its payload:

    * If `type == "tombstone.v1"` → deleted.
    * If `type == "product.v1"` → enforce §4.1 rules.
4. If `stockUrl` present, dereference it according to its scheme and, if it yields `stock.v1`, merge it in best-effort (transport failures **MUST NOT** hide the product).

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

    * Accept `type == "product.v1"` and `schemaVersion == "1.2"`, valid URIs, `price` finite ≥ 0, size ≤ 8 MiB.
    * If newest valid payload is `tombstone.v1`, **exclude** that `(seller, productId)`.
3. **Group** by `(sellerAvatar = item.avatar, productId)` (productId from payload).
4. **Pick winner** using CPA’s total order (signedAt ↓, indexInChunk ↓, avatar ↑, linkKeccak ↑).
5. **Compose** a deterministic catalog item with provenance.

### 7.2 Catalog aggregate output

```json
{
  "type": "CatalogAgg.v1",
  "operator": "0x<operator>",
  "chainId": 100,
  "window": { "start": 1724310000, "end": 1726902000 },
  "avatarsScanned": ["0x<seller1>", "0x<seller2>"],
  "products": [
    {
      "seller": "0x<seller1>",
      "productCid": "Qm<cidv0>",
      "publishedAt": 1724410000,
      "linkKeccak": "0x<64-hex>",
      "product": { /* product.v1 */ }
    }
  ],
  "errors": [ /* passthrough from CPA */ ]
}
```

* **Sorting of `products`**: default to the winner link’s CPA order (newest-first). UI may re-sort arbitrarily.

---

## 8. Conformance checklist

1. **Addresses**: lowercase, regex per CPP §2.1.
2. **Link name**: `product/<productId>`; `productId` matches `^[a-z0-9][a-z0-9-_]{0,62}$`.
3. **Payload**: `product.v1` with valid URIs in `images[*]` and `stockUrl?`; `price` finite, non-negative; size ≤ 8 MiB.
4. **Writes**: rotation at 100; **rebase before serialize**; atomic commit; pinning per CPP §7.
5. **Verification**: canonical bytes; EOA low-S; Safe ERC-1271 (bytes) with `from = signerAddress`; chain domain match; tuple-scoped nonce replay (CPP §8).
6. **Aggregation**: CPA inputs/outputs respected; reduction uses CPA total order; tombstones excluded.
7. **Transport independence**: No requirement on URI scheme; implementations MUST NOT reject by scheme alone.

---

## 9. Examples

### 9.1 Publish product (seller → operator namespace)

* Namespace: `(0xseller…, 0xoperator…)`
* Link:

    * `name = "product/tee-classic-black"`
    * `cid = QmPayloadCid` (of `product.v1`)
    * `signerAddress = 0xseller…`
    * `chainId`, `signedAt`, `nonce` set per CPP

### 9.2 `product.v1` (minimal)

```json
{
  "type": "product.v1",
  "schemaVersion": "1.2",
  "productId": "tee-classic-black",
  "title": "Classic Tee",
  "images": ["ipfs://Qm...", "https://cdn.example.com/img.jpg"],
  "price": 19.0,
  "createdAt": 1724310000,
  "updatedAt": 1724310000
}
```

### 9.3 `stock.v1` (optional)

```json
{
  "type": "stock.v1",
  "productId": "tee-classic-black",
  "policy": "finite",
  "quantity": 12,
  "updatedAt": 1724310600
}
```

---

## 10. Notes

* Payloads themselves are stored/pinned as CPP objects (CIDv0) for consistency and auditability, **but** the URIs they *reference* are transport-agnostic.
* If a URI’s transport isn’t supported by a given client, that client may skip fetching it without failing the product as a whole.
