# Circles Profiles Protocol

## 0. Scope & high‑level

Circles Profiles gives every on‑chain *avatar* (EOA or contract wallet like a Safe) a public **profile document** and a
set of **append‑only namespaces** that point to signed content on IPFS. A client can:

* Publish/update a **profile** (one CID per avatar) via an on‑chain **name registry**.
* Append **links** (signed envelopes) into a per‑namespace log (stored on IPFS).
* Read a sender’s namespace for a recipient, **verify signatures**, and fetch payloads from IPFS.

Storage is IPFS. Authenticity is cryptographic (EOA or ERC‑1271). The registry stores the SHA‑256 **digest** of the
profile JSON.

**JSON‑LD framing (normative):** All protocol objects defined in this spec — **Profile**, **NameIndexDoc**, **NamespaceChunk**, and **CustomDataLink** — are **JSON‑LD** and MUST include an `@context` and `@type` matching the object kind:

* Profile: `@context = "https://aboutcircles.com/contexts/circles‑profile/"`, `@type = "Profile"`
* NameIndexDoc / NamespaceChunk: `@context = "https://aboutcircles.com/contexts/circles‑namespace/"`, `@type ∈ {"NameIndexDoc","NamespaceChunk"}`
* CustomDataLink: `@context = "https://aboutcircles.com/contexts/circles‑linking/"`, `@type = "CustomDataLink"`

The shared JSON‑LD terms live under:
`https://aboutcircles.com/contexts/circles‑common/` (types like `cidv0`, `ethAddress`, `unixSeconds`).

---

## 1. Actors & components

* **Avatar** — an Ethereum address (EOA or contract) owning a profile.
* **Profile** — JSON‑LD stored on IPFS, referenced on‑chain by its digest.
* **Namespace** — `(ownerAvatar, namespaceKey)` pair; append‑only log of *links*.
* **Link** — signed envelope pointing to a payload CID with replay guards.
* **Registry** — Solidity contract mapping `avatar → bytes32 digest` (current profile).
* **Client** — dApp/wallet/app speaking this protocol.
* **IPFS** — add/cat operations; **CIDv0** is required for profile/index/chunk objects.

---

## 2. Identifiers & encodings

### 2.1 Ethereum address (normative)

* **Binary:** 20 bytes.
* **String form (for all protocol fields):** lowercase hex with `0x` prefix, exactly 42 characters.
  **Regex:** `^0x[a‑f0‑9]{40}$`

When the protocol compares addresses, use case‑insensitive semantics on input, but **writers MUST emit lowercase** in
all JSON fields governed by this spec. In JSON‑LD, these fields are mapped via the context to the `ethAddress` type.

### 2.2 CIDs (CIDv0 only)

* **Form:** base58btc, 46 characters, starts with `Qm`.
* **Mapping to digest:** CIDv0 = multihash `0x12 0x20 || <32‑byte sha2‑256 digest>` (function 0x12, length 0x20),
  base58btc‑encoded.
* **Digest32 length:** exactly 32 bytes.

### 2.3 Hex & sizes

* Hex strings MUST be `0x`‑prefixed, lowercase.
* **Nonce:** exactly 16 random bytes → 32 hex chars after `0x`.
* **Signature:** exactly 65 bytes (`r(32)||s(32)||v(1)`) → 130 hex chars after `0x`.
* **IPFS object size (hard cap):** any object fetched by readers (profile, index, chunk, payload) MUST be ≤ **8,388,608
  bytes** (8 MiB).

### 2.4 Time

* Unix seconds since epoch (UTC), 64‑bit signed integer range. In JSON‑LD, such fields are mapped via the context to the `unixSeconds` type.

---

## 3. On‑chain registry

ABI (normative):

```json
[
  {
    "type": "function",
    "name": "updateMetadataDigest",
    "inputs": [
      {
        "type": "bytes32",
        "name": "_metadataDigest"
      }
    ],
    "outputs": [],
    "stateMutability": "nonpayable"
  },
  {
    "type": "function",
    "name": "getMetadataDigest",
    "inputs": [
      {
        "type": "address",
        "name": "_avatar"
      }
    ],
    "outputs": [
      {
        "type": "bytes32"
      }
    ],
    "stateMutability": "view"
  }
]
```

Semantics:

* `getMetadataDigest(avatar)` → `bytes32 digest` (all zeros if unset).
* `updateMetadataDigest(digest32)` MUST be sent **from the avatar**.

    * **EOA avatar:** transaction from `avatar`.
    * **Safe avatar:** executed by the Safe (`execTransaction`) where `to = registry`,
      `data = updateMetadataDigest(digest32)`.

`digest32` is the **sha2‑256 digest** inside the profile’s CIDv0 (see §2.2).

---

## 4. Data structures (JSON‑LD wire format)

All JSON uses **camelCase** property names. Unknown properties MUST be ignored by readers unless stated otherwise.
All objects in this section **MUST** carry the JSON‑LD `@context` and `@type` shown.

### 4.1 Profile

```json
{
  "@context": "https://aboutcircles.com/contexts/circles‑profile/",
  "@type": "Profile",
  "previewImageUrl": "https://…",
  "imageUrl": "https://…",
  "name": "Alice",
  "description": "web3 dev",
  "namespaces": {
    "0xrecipientaddress000000000000000000000000": "QmHeadChunkCid",
    "0xdappoperatoraddr000000000000000000000000": "QmHeadChunkCid"
  },
  "signingKeys": {
    "0x<64‑hex‑fingerprint>": {
      "@type": "SigningKey",
      "publicKey": "0x04<128‑hex uncompressed secp256k1 (X||Y)>",
      "validFrom": 1718000000,
      "validTo": 1730000000,
      "revokedAt": 0
    }
  }
}
```

**Constraints (normative):**

* `namespaces` **keys MUST be valid Ethereum addresses** per §2.1 and **MUST be lowercase**.
* `namespaces` values MUST be **CIDv0** strings per §2.2.

### 4.2 Namespace index — `NameIndexDoc`

```json
{
  "@context": "https://aboutcircles.com/contexts/circles‑namespace/",
  "@type": "NameIndexDoc",
  "head": "Qm<chunk‑cid>",
  "entries": {
    "msg‑42": "Qm<chunk‑cid>",
    "prefs‑v1": "Qm<chunk‑cid>"
  }
}
```

### 4.3 Namespace chunk — `NamespaceChunk`

```json
{
  "@context": "https://aboutcircles.com/contexts/circles‑namespace/",
  "@type": "NamespaceChunk",
  "prev": "Qm<older‑chunk‑cid>|null",
  "links": [
    /* CustomDataLink … */
  ]
  /* links are appended; max length = 100 */
}
```

* **Maximum links per chunk (normative):** **100**. On insert beyond 100, rotate (see §7).

### 4.4 Link (signed envelope) — `CustomDataLink`

```json
{
  "@context": "https://aboutcircles.com/contexts/circles‑linking/",
  "@type": "CustomDataLink",
  "name": "msg‑42",
  "cid": "Qm<payloadCid>",
  "encrypted": false,
  "encryptionAlgorithm": null,
  "encryptionKeyFingerprint": null,
  "chainId": 100,
  "signerAddress": "0xavatarOrSafeaddress0000000000000000",
  "signedAt": 1724310000,
  "nonce": "0x0123456789abcdeffedcba9876543210",
  "signature": "0x<130‑hex r||s||v>"
}
```

**Replay fields are mandatory:** `chainId`, `signerAddress`, `signedAt`, `nonce`.

---

## 5. Canonicalisation (hash preimage)

Before hashing/signing a link, compute **canonical JSON bytes** of the link **with the `signature` property removed**.

Rules (normative):

1. Serialize the link to JSON‑LD; parse to a DOM and re‑emit canonically:

    * **Objects:** sort properties by Unicode code‑point of the property name (ascending). **Duplicate property names
      MUST raise an error.** Omit the `signature` property if present. `@context` and `@type` remain and are part of the preimage.
    * **Arrays:** preserve element order.
    * **Booleans / null:** emit literal tokens.
    * **Strings:** emit JSON strings.
    * **Numbers:**

        * If value fits **signed 64‑bit integer**, write as integer literal.
        * Else if value fits an IEEE‑754 **double** and round‑trips via the platform JSON writer, write the
          shortest‑round‑trip double literal.
        * Else **reject** the object as non‑canonical.
2. The result is the **payload bytes** (UTF‑8).

* **Payload hash**: `keccak256(payload bytes)`.

---

## 6. Signing & verification

### 6.1 EOA

* **Message:** `keccak256(payload bytes)`.
* **Signature constraints:**

    * **Low‑S required** (EIP‑2).
    * `v ∈ {27, 28}` on the wire.
* **Verify:** Recover address from `(hash, signature)` and compare to `signerAddress`.

### 6.2 Safe (ERC‑1271 SafeMessage path)

The link’s `signerAddress` is the **Safe**. The signature is produced with an **owner EOA** over the Safe’s
EIP‑712‑style message.

Hashes (normative):

```
domainTypeHash      = keccak256("EIP712Domain(uint256 chainId,address verifyingContract)")
safeMessageTypeHash = keccak256("SafeMessage(bytes message)")
domainSeparator     = keccak256( abi.encode(domainTypeHash, chainId, safeAddress) )
safeMsg             = keccak256( safeMessageTypeHash || keccak256(payload bytes) )
safeHash            = keccak256( 0x19 0x01 || domainSeparator || safeMsg )
```

Sign `safeHash` with the owner key → 65B signature.

**Verification flow (normative):**

1. If `signerAddress` has non‑empty code at `eth_getCode`, treat as contract.
2. **Primary attempt:** call **`isValidSignature(bytes data, bytes signature)`** on `signerAddress` with:

    * `data = payload bytes`
    * **`eth_call.from = signerAddress`** (required by Safe fallback routing)
    * Success if the first 4 return bytes equal `0x20c13b0b`.
3. If (2) returns non‑magic (without revert), you MAY retry once with `v` toggled `{27,28} ↔ {31,32}`.
4. Optionally try the `bytes32` overload: `isValidSignature(bytes32 hash, bytes signature)`, success if return equals
   `0x1626ba7e`. (Not the canonical Safe path.)
5. If the account is an EOA, use §6.1.

**Error semantics:**

* **EVM revert** during `eth_call` → treat the attempt as **invalid signature** (no exception).
* **Transport / RPC errors** (timeouts, 5xx, malformed) → **surface to caller** (do not map to “invalid”).

---

## 7. Namespaces & write semantics

A **namespace** is identified by `(ownerAvatar, namespaceKey)`.

**Namespace key rule (normative):** `namespaceKey` **MUST** be an Ethereum address per §2.1, and **MUST** be serialized
in lowercase in `profile.namespaces`.

**Conventions:**

* **Inbox/DM:** `namespaceKey = recipient address (lowercase)`.
* **dApp settings / operator channel:** `namespaceKey = operator address (EOA or Safe, lowercase)`.

### 7.1 Writer algorithm (normative)

* Maintain in memory: **head chunk** and **index**.
* **Insert:**

    * If `head.links.length == 100`, **rotate**:

        1. Pin current head → `closedCid`.
        2. For each link in the closed head, set `index.entries[link.name] = closedCid`.
        3. Start new head `{ "@context": "https://aboutcircles.com/contexts/circles‑namespace/", "@type": "NamespaceChunk", "prev": closedCid, "links": [] }`.
    * If the **logical name** already exists **in the head**, replace that element; else append.
* **Commit (with pin confirmation):**

    0. **Rebase (normative):** Immediately **before** serializing the profile JSON:

        1. Resolve the latest profile digest for `ownerAvatar` via the registry.
        2. Fetch that profile JSON by CIDv0.
        3. Re‑apply the staged `namespaces[namespaceKeyLower] = indexCid` mutations **onto that latest profile object**.
        4. Proceed with serialization/pinning of this **rebased** profile.
           *If the registry digest changes again between this step and the registry call below, last‑writer‑wins applies; writers MAY optionally retry once.*
    1. Pin the current head → `headCid`. **If the pin cannot be confirmed, the write MUST fail.**
    2. For each link in the head, set `index.entries[link.name] = headCid`.
    3. Set `index.head = headCid`.
    4. Serialize `index` (as JSON‑LD `NameIndexDoc`), compute CIDv0 (`indexCid`), **pin index**. **If the pin cannot be confirmed, the write MUST
       fail and the profile MUST NOT be mutated.**
    5. Update **(rebased)** `profile.namespaces[namespaceKeyLower] = indexCid`.
    6. Serialize profile JSON‑LD, pin, compute `digest32`. **If the pin cannot be confirmed, the publish MUST fail.**
    7. Call the registry (§3) with `digest32`.

**Size rule:** Any IPFS write that later must be read by clients MUST respect the **8 MiB** per‑object limit.

### 7.2 Atomic commit and multi‑namespace batching (normative)

**Atomicity (per publish):** A publish operation MUST be **all‑or‑nothing** across **all** objects it intends to mutate (all involved chunks, indices, and the profile). If any required pin or precondition fails, the publish MUST NOT change the profile digest.

**Batching across namespaces:** A client MAY stage updates for **multiple namespaces** belonging to the same `ownerAvatar` and publish them in **one** profile update, provided §7.1’s rules are satisfied for each namespace and the **atomicity** rule above is respected.

**Rationale (non‑normative):** Prevents partial publishes and aligns base writes with proposer‑bundle acceptance flows while reducing on‑chain updates.

---

## 8. Reading & verification

Given `senderAvatar` and `namespaceKey`:

1. **Resolve profile:** `digest32 = getMetadataDigest(senderAvatar)`. If zero → profile absent.
2. **Fetch profile** via IPFS using CIDv0 derived from `digest32`. Object MUST be JSON‑LD with `@context`/`@type` per §4.1.
3. **Validate namespace key:** `namespaceKey` MUST match §2.1 (lowercase address). Readers MUST ignore any `namespaces`
   entries whose keys are not valid addresses.
4. **Fetch index:** `idxCid = profile.namespaces[namespaceKeyLower]`. If missing → nothing to read.
5. **Walk chunks:** start at `cur = index.head`; iterate `prev` pointers. Index/chunks MUST be JSON‑LD per §4.2/§4.3.
6. **Verify each link (normative order):**

    1. **Canonical bytes:** compute canonical JSON‑LD of the link **with `signature` removed** (per §5), and its keccak.
    2. **Signature:** verify cryptographically (EOA or Safe) per §6. Drop the link on cryptographic failure (no exception).
    3. **Chain domain:** require `link.chainId == verifyingChainId`; otherwise **drop** (no error).
    4. **Replay scope (normative):** enforce duplicate‑nonce detection **only among links that passed (2) and (3)**,
       scoped to the tuple `(<namespaceOwner>, <namespaceKey>, <signerAddress>)`.
       Implementations MUST NOT treat the same nonce reused in a different tuple as a replay.
7. **Ordering (normative):** present links **newest‑first** by `signedAt` descending.
   **Tie‑break:** if `signedAt` is equal, higher array index within its chunk is considered newer.
8. **Aggregation guidance (non‑normative):** when aggregating across directions or mirrored copies, de‑duplicate *
   *byte‑identical links** using `keccak256(canonical link JSON without signature)`.

**Failure policy:** Drop cryptographically invalid links; throw on IPFS/RPC/transport failures.

**Note (non‑normative, early‑stop):** Readers **MUST NOT** early‑stop a chunk walk solely because a link’s `signedAt` falls outside a query window. Storage order is by **append/count, not time**; older chunks may contain **newer** `signedAt` values, and array order within a chunk is not time‑sorted. Correct, windowed scans require walking the full `head → prev → …` chain (or using a separate, explicitly‑trusted optimization).

---

## 9. Operator keys & accepting pre‑signed links (wallet flow)

When a dApp asks a user‑wallet to store a **pre‑signed** link in the user’s profile under the dApp’s namespace:

**Key publication:**

* **Fingerprint** = `keccak256(uncompressed 64‑byte pubkey)` (the 65‑byte uncompressed key without the leading `0x04`),
  encoded as `0x` + 64 hex.
* Operator publishes `signingKeys[fingerprint] = { "@type": "SigningKey", publicKey, validFrom, validTo?, revokedAt? }` in its own profile.

**Wallet acceptance (EOA operator, normative):**

1. `signedLink.signerAddress` MUST equal `namespaceKeyLower` (address rule).
2. Verify signature as EOA (low‑S, recovery).
3. Compute fingerprint from recovered public key; locate in operator’s `signingKeys`.
4. Enforce time bounds: `validFrom ≤ signedAt`, and if present `signedAt < validTo` and `signedAt < revokedAt`.
5. Persist into the user’s namespace via §7 and publish.

**Safe operator:** verify with ERC‑1271 bytes path (no fingerprint check), then persist and publish.

---

## 10. Safe publishing (profile updates via Safe)

Single‑owner flow (normative defaults):

```
to              = NameRegistry
value           = 0
data            = abi.encodeWithSelector(updateMetadataDigest(bytes32), digest32)
operation       = 0          // CALL
safeTxGas       = 150_000
baseGas         = 0
gasPrice        = 0
gasToken        = 0x0000000000000000000000000000000000000000
refundReceiver  = 0x0000000000000000000000000000000000000000
signatures      = single 65B owner signature (R||S||V) over getTransactionHash(...)
```

* Compute the hash with Safe’s `getTransactionHash(...)`.
* Optional **staticcall** to `execTransaction` for preflight.
* Send and wait for receipt with `status == 1`.

(Multi‑sig thresholds and modules follow standard Safe semantics and are out of scope here.)

---

## 11. Error handling (normative)

* **Cryptographic invalid** → drop the link.
* **Replay** per §8 tuple scope → drop the link.
* **Pinning failure:** failure to pin a CID that MUST be pinned (writer commit, index, profile, or mirror payload) → *
  *throw**; include the CID and pin target(s) in the error. **Writes are atomic:** partial mutations MUST NOT be
  published.
* **Malformed JSON** (profile/index/chunk) → throw; include the offending CID in the error.
* **Transport / RPC errors** → throw.
* **CID policy violated** (not CIDv0 where required) → reject/throw.
* **Missing JSON‑LD envelope** (`@context`/`@type`) on CPP objects → reject/throw.

---

## 12. Security & privacy (required behavior and guidance)

* **Low‑S** enforcement for EOA signatures (reject high‑S).
* **Chain domain:** `chainId` in links MUST reflect the chain where verification occurs.
* **Clock skew:** tolerate small future skew (e.g., ≤ 30 s) for `signedAt`.
* **Encryption:** If `encrypted=true`, payload format and key distribution are application‑defined; record
  `encryptionAlgorithm` and `encryptionKeyFingerprint` when used.
* **Availability / pinning (normative):**

    * **Writers MUST pin** every newly referenced CID (profile, index, chunk, payload) and **fail** the write if pinning
      cannot be confirmed.
    * **Mirrorers MUST pin** mirrored payload CIDs and SHOULD pin their own head/index updates.
    * Implementations SHOULD support multiple pin targets (local node, cluster, remote pinning service) and MAY consider
      a write successful once at least one target confirms the pin.
    * The **8 MiB** object limit applies equally to mirrored payloads and `mirror.v1` (§20.2).

---

## 13. JSON‑LD examples

### 13.1 Link (EOA)

```json
{
  "@context": "https://aboutcircles.com/contexts/circles‑linking/",
  "@type": "CustomDataLink",
  "name": "greeting‑001",
  "cid": "QmWmyoMoctfbA…",
  "encrypted": false,
  "chainId": 100,
  "signerAddress": "0x5abfec25f74cd88437631a7731906932776356f9",
  "signedAt": 1724310000,
  "nonce": "0x12ab34cd56ef78aa12ab34cd56ef78aa",
  "signature": "0x<130‑hex>"
}
```

### 13.2 Index

```json
{
  "@context": "https://aboutcircles.com/contexts/circles‑namespace/",
  "@type": "NameIndexDoc",
  "head": "QmXHead…",
  "entries": {
    "greeting‑001": "QmXHead…",
    "prefs‑v1": "QmPrevHead…"
  }
}
```

---

## 14. Sequence diagrams

### 14.1 EOA write → publish → read (verified)

```mermaid
sequenceDiagram
    autonumber
    participant App
    participant IPFS
    participant Registry
    participant RPC as Ethereum Node
    App ‑>> IPFS: Add payload JSON → CIDv0
    App ‑>> App: Build JSON‑LD link (nonce 16B, signedAt, chainId, signerAddress=EOA)
    App ‑>> App: Canonicalise (remove signature) → bytes, keccak
    App ‑>> App: ECDSA sign (low‑S) → 65B sig
    App ‑>> IPFS: Update JSON‑LD chunk/index, pin head & index (fail on pin error)
    App ‑>> Registry: getMetadataDigest(avatar)
    Registry ‑‑>> App: digest32 (latest)
    App ‑>> IPFS: Fetch latest JSON‑LD profile by CIDv0 (rebase)
    App ‑>> App: Re‑apply staged namespaces[...] to latest profile
    App ‑>> IPFS: Save rebased JSON‑LD profile, compute CIDv0 → digest32 (fail on pin error)
    App ‑>> Registry: updateMetadataDigest(digest32) (from EOA)
    Registry ‑‑>> App: receipt status=1

    App ‑>> Registry: getMetadataDigest(sender)
    Registry ‑‑>> App: digest32
    App ‑>> IPFS: Fetch profile → idx → chunk(s)
    App ‑>> App: Verify links, replay scoped per (owner, key, signer), order newest→oldest
```

### 14.2 Safe write + publish

```mermaid
sequenceDiagram
    autonumber
    participant Owner as Owner EOA
    participant App
    participant IPFS
    participant Safe as Gnosis Safe
    participant Registry
    participant RPC
    App ‑>> IPFS: Add payload → CIDv0
    App ‑>> App: Draft JSON‑LD link (signerAddress=Safe), canonicalise → bytes, keccak
    App ‑>> App: Compute Safe hash (domain(chainId,safe), SafeMessage(keccak))
    Owner ‑>> App: Sign safeHash → 65B sig
    App ‑>> IPFS: Update JSON‑LD chunk/index (fail on pin error)
    App ‑>> Registry: getMetadataDigest(avatar)
    Registry ‑‑>> App: digest32 (latest)
    App ‑>> IPFS: Fetch latest JSON‑LD profile by CIDv0 (rebase)
    App ‑>> App: Re‑apply staged namespaces[...] to latest profile
    App ‑>> Safe: execTransaction( to=Registry, data=updateMetadataDigest(digest32), …, signatures=ownerSig )
    Safe ‑>> Registry: updateMetadataDigest
    Registry ‑‑>> Safe: ok
    Safe ‑‑>> App: receipt status=1

    Note over App: Reader uses ERC‑1271(bytes) with eth_call.from = Safe
```

### 14.3 Wallet accepts operator EOA link

```mermaid
sequenceDiagram
    autonumber
    participant DApp
    participant Wallet
    participant IPFS
    DApp ‑>> Wallet: signedLink (signerAddress = dApp EOA)
    Wallet ‑>> IPFS: Load dApp profile (for signingKeys)
    Wallet ‑>> Wallet: Verify EOA sig + fingerprint validity at signedAt
    alt valid
        Wallet ‑>> IPFS: Insert into (user, dApp EOA) namespace, pin, publish
    else invalid
        Wallet ‑‑>> DApp: reject
    end
```

### 14.4 Direct mirror (“copy + pin”)

```mermaid
sequenceDiagram
    autonumber
    participant Sender
    participant Recipient
    participant IPFS
    participant Registry
    Sender ‑>> IPFS: Original link posted in (sender, recipient) namespace
    Recipient ‑>> Registry: Resolve sender profile → index → chunk → link
    Recipient ‑>> Recipient: Verify signature, tuple scope prevents replay collision
    Recipient ‑>> IPFS: Pin payload CID (required)
    Recipient ‑>> IPFS: Insert the **same** link bytes into (recipient, signer) namespace, pin head/index
    Recipient ‑>> Registry: Publish updated recipient profile (digest32)
```

---

## 15. Interop notes vs the C# reference

* **Chunk size:** exactly 100.
* **Namespace keys:** MUST be lowercase **addresses only**; non‑address keys are ignored by readers and MUST NOT be
  produced by writers.
* **CID policy:** CIDv0 only for profile, index, chunk; payloads SHOULD be CIDv0 to stay within the same acceptance
  rules.
* **Canonical numbers:** only `int64` or shortest‑round‑trip double; otherwise reject on signing.
* **EOA signatures:** low‑S enforced; high‑S rejected.
* **Safe verification:** prefer `isValidSignature(bytes,bytes)` with `from = <safe>`; allow one V‑toggle retry.
* **Ordering:** newest→oldest by `signedAt`, tie‑break by append index.
* **Replay scope:** duplicate‑nonce detection is per `(namespaceOwner, namespaceKey, signerAddress)`.
* **Error semantics:** invalid ⇒ drop; infra errors ⇒ throw; **pin failures ⇒ throw**.
* **JSON‑LD:** All CPP objects carry `@context`/`@type` as in §4 and must be emitted/validated accordingly.

---

## 16. Conformance checklist

1. Addresses match `^0x[a‑f0‑9]{40}$`; writers emit lowercase.
2. Profile JSON‑LD matches §4.1; `namespaces` keys are addresses; values are CIDv0.
3. Canonical payload bytes and keccak match the reference for test vectors (JSON‑LD with `signature` removed).
4. EOA signatures are low‑S; high‑S is rejected.
5. Safe signatures validate via ERC‑1271(bytes) with `from = <safe>`.
6. **Writer atomicity:** updates are atomic per §7.2; rotation semantics match §7.1; **rebase before serialize** is implemented.
7. **Batching:** multiple namespaces for one owner MAY be published in a single atomic update.
8. Readers verify links, **scope replay per tuple**, and order as §8.
9. All IPFS reads respect the **8 MiB** per‑object limit.
10. **Mirroring:** a byte‑for‑byte `CustomDataLink` signed by a third party can be inserted into `(recipient, signer)` and
    verifies under §6.
11. **Pinning (writes):** writers pin head/index/profile/payload; writes fail on pin failure.
12. **Pinning (mirrors):** mirrorers pin mirrored payloads (and SHOULD pin head/index).
13. **Order‑of‑checks:** readers perform signature → chain check → tuple‑scoped replay, in that order (§8.6). Readers do **not** early‑stop chunk walks based solely on `signedAt` window boundaries.
14. **JSON‑LD envelope present** on all CPP objects.

---

## 17. ABIs (normative excerpts)

**ERC‑1271:**

```json
[
  {
    "type": "function",
    "name": "isValidSignature",
    "stateMutability": "view",
    "inputs": [
      {
        "name": "_hash",
        "type": "bytes32"
      },
      {
        "name": "_signature",
        "type": "bytes"
      }
    ],
    "outputs": [
      {
        "name": "magicValue",
        "type": "bytes4"
      }
    ]
  },
  {
    "type": "function",
    "name": "isValidSignature",
    "stateMutability": "view",
    "inputs": [
      {
        "name": "_data",
        "type": "bytes"
      },
      {
        "name": "_signature",
        "type": "bytes"
      }
    ],
    "outputs": [
      {
        "name": "magicValue",
        "type": "bytes4"
      }
    ]
  }
]
```

**Gnosis Safe (subset):**

```json
[
  {
    "type": "function",
    "name": "nonce",
    "inputs": [],
    "outputs": [
      {
        "type": "uint256"
      }
    ],
    "stateMutability": "view"
  },
  {
    "type": "function",
    "name": "getTransactionHash",
    "inputs": [
      {
        "type": "address",
        "name": "to"
      },
      {
        "type": "uint256",
        "name": "value"
      },
      {
        "type": "bytes",
        "name": "data"
      },
      {
        "type": "uint8",
        "name": "operation"
      },
      {
        "type": "uint256",
        "name": "safeTxGas"
      },
      {
        "type": "uint256",
        "name": "baseGas"
      },
      {
        "type": "uint256",
        "name": "gasPrice"
      },
      {
        "type": "address",
        "name": "gasToken"
      },
      {
        "type": "address",
        "name": "refundReceiver"
      },
      {
        "type": "uint256",
        "name": "nonce"
      }
    ],
    "outputs": [
      {
        "type": "bytes32"
      }
    ],
    "stateMutability": "view"
  },
  {
    "type": "function",
    "name": "execTransaction",
    "inputs": [
      {
        "type": "address",
        "name": "to"
      },
      {
        "type": "uint256",
        "name": "value"
      },
      {
        "type": "bytes",
        "name": "data"
      },
      {
        "type": "uint8",
        "name": "operation"
      },
      {
        "type": "uint256",
        "name": "safeTxGas"
      },
      {
        "type": "uint256",
        "name": "baseGas"
      },
      {
        "type": "uint256",
        "name": "gasPrice"
      },
      {
        "type": "address",
        "name": "gasToken"
      },
      {
        "type": "address",
        "name": "refundReceiver"
      },
      {
        "type": "bytes",
        "name": "signatures"
      }
    ],
    "outputs": [
      {
        "type": "bool"
      }
    ],
    "stateMutability": "payable"
  }
]
```

**ERC‑1271 magic values:**

* bytes32: `0x1626ba7e`
* bytes:    `0x20c13b0b`

---

## 18. Implementation notes

* Do not hard‑code `chainId`; inject it from the signer/environment and stamp links accordingly.
* Enforce the **8 MiB** limit both at HTTP header pre‑check and while streaming (hard cap).
* Treat `namespaces` keys that are not addresses as invalid input; ignore when reading, never produce when writing.
* For Safe verification, always set `eth_call.from = signerAddress`.
* Prefer pinning to **multiple** targets for durability (local node + remote service).
* **Windowed reads (non‑normative):** The storage layout is **append‑ordered**, not time‑ordered. A link with `signedAt` outside a desired time window **does not** imply that the remaining tail is out‑of‑window. Readers that need a strict, trustless result **must** walk the full chain (`head → prev → ...`) or rely on a separate, explicitly‑trusted optimization layer.

---

## 19. Example use cases

1. **Inbox:** sender writes `ChatMessage` JSON into `(sender, recipient)` namespace, publishes profile; recipient reads
   and verifies. Recipient may **mirror** into `(recipient, sender)` for retention.

2. **Cross‑dApp preferences:** dApp (EOA or Safe) signs `prefs‑v1`; user wallet validates and stores under
   `(user, dAppAddress)`; any device reads it back.

3. **Notifications:** protocol account appends updates under `(protocol, user)`; clients verify without trusting
   servers; users mirror important notices for auditability.

---

## 20. Mirroring & Retention

### 20.1 Direct mirror (“copy + pin”, normative write rule)

* **Definition:** A **mirror** is the insertion of a *byte‑for‑byte identical* `CustomDataLink` (signed by the original
  signer) into the recipient’s namespace `(recipientAvatar, originalSignerAddress)`.
* **Validity:** Verification (§6) depends only on `signerAddress` inside the link; a namespace may contain links signed
  by third parties.
* **Write rule:** When mirroring, writers **MUST NOT** modify any field of the mirrored `CustomDataLink`.
* **Replay scope:** The tuple rule in §8 ensures that the mirror’s nonce does **not** collide with the original;
  implementations MUST scope duplicate‑nonce detection per `(<namespaceOwner>, <namespaceKey>, <signerAddress>)`.
* **Pinning (required):** On mirror, clients **MUST attempt to pin** the `cid` referenced by the mirrored link and *
  *MUST fail** the operation if pinning cannot be confirmed.
* **Index/profile updates:** Mirrorers SHOULD pin their updated head/index/profile CIDs as part of publishing.
* **Aggregation guidance:** UIs **SHOULD** de‑duplicate byte‑identical links across `(A,B)` and `(B,A)` using
  `keccak256(canonical link JSON without signature)`.

### 20.2 Attested mirror (optional)

An optional payload that records *what was seen, where, and when* for auditability.

**Payload shape (`mirror.v1` JSON):**

```json
{
  "type": "mirror.v1",
  "origSigner": "0x<address>",
  "origName": "inv‑2025‑00042",
  "origCid": "Qm<cidv0>",
  "origPayloadKeccak": "0x<64‑hex>",
  "origSignature": "0x<130‑hex>",
  "observedAt": 1724312345,
  "observedProfileDigest32": "0x<64‑hex>",
  "observedIndexCid": "Qm<cidv0>",
  "observedChunkCid": "Qm<cidv0>"
}
```

**Rules:**

* The attested mirror is posted as a **new link** signed by the **mirroring avatar** (EOA or Safe), typically under
  `(recipient, origSigner)`, with a `name` such as `"mirror/<origName>"` or `"mirror/<origPayloadKeccak>"`.
* Clients **SHOULD pin** the `mirror.v1` payload CID; the **8 MiB** limit applies.
* The fields capture both the original cryptographic facts and the chain‑observable pointers (profile digest, index CID,
  chunk CID) at the time of observation.
