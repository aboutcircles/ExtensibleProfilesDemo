# Circles Profiles Aggregator

**Purpose:** Read and verify links under an operator namespace across a set of avatars, filter by a time window, and emit a deterministic, auditable result.
This core reuses `CustomDataLink` from the Circles Profiles Protocol (CPP) and adds only the minimal provenance needed for correctness.

Out of scope: pagination/cursors, ranking/scoring, UI/transport, mirroring/writes, schema classification, operator capability ads.

---

## 0. Normative dependencies (CPP)

Implementations **MUST** follow the Circles Profiles Protocol (**CPP**) for all referenced behavior:

* **Identifiers & encodings:** addresses, CIDs, hex, time, sizes — **CPP §2**
* **Registry ABI & semantics:** `getMetadataDigest(address)` — **CPP §3**
* **Data structures:** `Profile`, `NameIndexDoc`, `NamespaceChunk`, `CustomDataLink` — **CPP §4**
* **Canonicalisation & payload hash:** JSON canonicalisation, `keccak256(canonicalBytes)` — **CPP §5**
* **Signing & verification:** EOA low‑S; ERC‑1271 bytes path with `eth_call.from = signerAddress` — **CPP §6**
* **Reading semantics:** namespaces keyed by addresses only; chunk rotation semantics — **CPP §§7–8**
* **Error handling & size caps:** transport vs. invalid; 8 MiB object cap — **CPP §§11–12, §15**

> For convenience, the exact JSON shapes used by this spec are reproduced in **Appendix A** (normative copies from CPP).

---

## 1. Inputs (normative)

* `operator: string` — Ethereum address. Case‑insensitive on input; **normalize to lowercase**.
* `avatars: string[]` — non‑empty array of Ethereum addresses. Case‑insensitive on input; **normalize to lowercase**.
* `chainId: int` — chain domain used for signature verification and chain checks.
* `window: { start: int64, end: int64 }` — Unix seconds; **MUST** satisfy `start ≤ end`.

---

## 2. Acceptance & policy (normative)

* **Addresses:** All addresses on output **MUST** be lowercase `^0x[a-f0-9]{40}$` (CPP §2.1).
* **Namespace key policy:** Only address keys are valid; ignore non‑address keys (CPP §§7–8).
* **CID policy:** `Profile`, `NameIndexDoc`, `NamespaceChunk` **MUST** be **CIDv0**; payload CIDs **SHOULD** be CIDv0 (CPP §2.2, §15).
* **Size limit:** Any fetched object (profile/index/chunk/payload) **MUST** be ≤ **8,388,608 bytes** (8 MiB) (CPP §2.3).
* **Time skew:** Tolerate future skew ≤ **30 s** for `signedAt` (CPP §12).
* **Chain domain:** Require `link.chainId == chainId`; mismatches are **dropped** (not errors) (CPP §12).
* **Payload neutrality:** Aggregator **MUST NOT** depend on payload parsing; only link‑level cryptography and metadata.

---

## 3. Output (normative)

### 3.1 Top‑level — `AggResult.v1`

```json
{
  "type": "AggResult.v1",
  "operator": "0x<address>",
  "chainId": 100,
  "window": { "start": 1724310000, "end": 1726902000 },
  "avatarsScanned": ["0x<addr>", "..."],
  "items": [ /* LinkWithProvenance.v1 ... */ ],
  "errors": [ /* AggErrorV1 ... */ ]
}
```

**Constraints:**

* `operator` and all `avatarsScanned[*]` are lowercase addresses.
* `avatarsScanned` lists every avatar the Aggregator attempted to scan (even if errors occurred).
* `items` is **newest‑first** per §5.

### 3.2 Item — `LinkWithProvenance.v1`

```json
{
  "avatar": "0x<namespaceOwner>",
  "chunkCid": "Qm<chunk-cidv0>",
  "indexInChunk": 42,
  "link": { /* CustomDataLink from CPP, unchanged */ },
  "linkKeccak": "0x<64-hex>"
}
```

**Semantics:**

* `avatar` is the namespace owner in `(avatar, operator)`.
* `chunkCid` and `indexInChunk` pinpoint where the link was read (required for tie‑breaks and audit).
* `link` is the unmodified **CustomDataLink** (CPP §4.4).
* `linkKeccak` is `keccak256(canonical(link without signature))` per CPP §5.
  This field **MAY** be omitted in output; if omitted, consumers **MUST** recompute it for ordering and de‑dup. The Aggregator **MUST** compute and use it internally either way.

### 3.3 Error — `AggErrorV1`

```json
{
  "avatar": "0x<address>|null",
  "stage": "registry|profile|index|chunk|verify|rpc|other",
  "cid": "Qm<cidv0>|null",
  "message": "human-readable"
}
```

**Semantics:**

* **Drop silently:** cryptographically invalid links; tuple‑replay duplicates.
* **Record as errors:** registry/IPFS/RPC transport failures; CID policy violations; size‑cap violations; malformed JSON. Populate best‑known `avatar` and/or `cid`.

---

## 4. Algorithm (normative)

For each avatar `A` in `avatars`:

1. **Resolve profile digest**
   Call Registry `getMetadataDigest(A)` (CPP §3). If zero → **skip** `A`.

2. **Fetch + validate profile**
   Derive profile CIDv0 from the digest (CPP §2.2) and fetch via IPFS. Require `schemaVersion == "1.2"` (CPP §4.1).
   If `profile.namespaces[operator]` is missing → **skip** `A`.

3. **Load namespace index & walk chunks**
   Let `indexCid = profile.namespaces[operator]`. Fetch `NameIndexDoc` (CPP §4.2). Set `cur = index.head`.
   While `cur != null`:

    * Fetch `NamespaceChunk` at `cur` (CPP §4.3).
    * Iterate `chunk.links` in **array order** with zero‑based index `i`.
    * For each `link`:

        1. **Canonical bytes & hash:** compute canonical JSON with `signature` removed (CPP §5) and `linkKeccak = keccak256(canonicalBytes)`.
        2. **Signature verification** (CPP §6):

            * **EOA:** recover; enforce **low‑S**; on‑wire `v ∈ {27,28}`.
            * **Contract:** call `isValidSignature(bytes,bytes)` with **`eth_call.from = signerAddress`**; allow one `v`‑toggle retry; optionally try the `bytes32` overload.
            * On cryptographic failure: **drop** (no error entry).
        3. **Chain check:** require `link.chainId == chainId`; else **drop**.
        4. **Replay scope:** within tuple `(A, operator, link.signerAddress)`, if `nonce` duplicates an already accepted link in this run → **drop**.
        5. **Time filter:** keep only `window.start ≤ link.signedAt ≤ window.end + 30`.
        6. **Collect** a `LinkWithProvenance.v1`:

           ```json
           { "avatar": "0x...", "chunkCid": "Qm...", "indexInChunk": i, "link": { ... }, "linkKeccak": "0x..." }
           ```
    * Set `cur = chunk.prev` and continue.

4. **Error capture & continuation**
   On transport/RPC/IPFS failures, CID policy or size violations, or malformed JSON, **record** an `AggErrorV1` with the most precise `stage`, and continue with other avatars.

**After processing all avatars:**

5. **Sort** `items` by the total order in §5.
6. **De‑dup across mirrors:** after sorting, drop subsequent items with identical `linkKeccak` (stable‑unique; keep the first).

> **Non‑normative (early stop):** Early termination while walking chunks is unsafe without per‑chunk time metadata. Implementations MAY early‑stop only if they have external guarantees that all remaining links are older than `window.start`.

---

## 5. Ordering (normative)

Sort `items` by this **total order**:

1. `link.signedAt` — **descending**
2. `indexInChunk` — **descending** (higher array index = newer within the chunk; CPP §8)
3. `avatar` — **ascending** (hex lexical)
4. `linkKeccak` — **ascending** (or the recomputed value if the field is omitted)

This order is stable and supports future pagination without changing item content.

---

## 6. Conformance checklist

1. Lowercase all addresses; `window.start ≤ window.end`; `chainId` provided.
2. Profiles fetched via digest→CIDv0; `schemaVersion == "1.2"`.
3. Namespace index located at `profile.namespaces[operator]` (address key only).
4. Chunk walk: `head → prev`; iterate `links` in array order.
5. Verification: canonicalisation per CPP §5; EOA low‑S; ERC‑1271 bytes path with `from = signerAddress`; allow one `v`‑toggle retry; optional `bytes32` overload.
6. Chain domain: require `link.chainId == chainId`.
7. Replay scope: nonce de‑dup per `(avatar, operator, signerAddress)`.
8. Time filter: `window` with ≤30 s future skew tolerance.
9. Size cap: every fetched object ≤ 8 MiB.
10. Output: `AggResult.v1` with `items` sorted by §5; errors recorded per §3.3.
11. Global de‑dup after sort by `linkKeccak`.
