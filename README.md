# Circles Profiles – Proof‑of‑Concept SDK (v1.1 schema)

Circles Profiles turn a plain EOA address on **Gnosis Chain** into a living, self‑describing “mini‑database”.  
Everything heavier than 32 bytes lives off‑chain on **IPFS**. A single on‑chain registry entry tells the world where to start reading.  
From there you can:

* attach signed data blobs (CIDs),
* organise them by *namespace* (think “DM inboxes” or “app buckets”), and
* verify provenance offline – every link is ECDSA‑signed.

In other words: you get extensible, user‑controlled metadata without new smart‑contracts or heavy gas costs.  
The code here is a **reference implementation** plus a CLI demo – good enough to prototype, not yet production‑hardened.

## 1. Repository Topology

| Folder | Assembly | Purpose | Build Target |
|--------|----------|---------|--------------|
| `Circles.Profiles.Models` | `Circles.Profiles.Models` | immutable DTOs that define the JSON shape persisted to IPFS and exchanged across language boundaries. No runtime dependencies outside BCL. | `net9.0` |
| `Circles.Profiles.Interfaces` | `Circles.Profiles.Interfaces` | abstraction layer (service and helper interfaces only). Used to decouple SDK from specific storage / registry back‑ends. | `net9.0` |
| `Circles.Profiles.Sdk` | `Circles.Profiles.Sdk` | reference implementation: IPFS HTTP 0.18 client + Gnosis Name‑Registry interaction via *Nethereum* 6.0‑preview. | `net9.0` |
| `Circles.Profiles.Sdk.Tests` | n/a (test) | NUnit 3.15 functional and unit tests (≈ 250 assertions, deterministic by design). | `net9.0` |
| `ExtensibleProfilesDemo` | `ExtensibleProfilesDemo` | CLI demonstrating end‑to‑end message exchange using SDK. UX intentionally minimal (plain stdout). | `net9.0` |

> **Build determinism:** all projects compile with `dotnet build -c Release` under .NET 9.0.  
> **No external NuGet feeds** are needed beyond `nuget.org`.

---

## 2. External System Contract

### 2.1 Name‑Registry (Solidity 0.8)

* **Network**  Gnosis Chain (a.k.a. Gnosis Mainnet / former xDai)  
* **Address**  `0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474`  
* **ABI**  embedded verbatim in `Circles.Profiles.Sdk/NameRegistry.cs`, constant `Abi.ContractAbi` (2 functions).  
* **Required JSON‑RPC methods** – `eth_call`, `eth_sendRawTransaction`, `eth_getTransactionReceipt`, `eth_chainId`, `eth_blockNumber`, `net_version`.  

| Function | Visibility | State mutability | Gas impact | Notes |
|----------|------------|------------------|-----------|-------|
| `getMetadataDigest(address)` | `external view` | `view` | ≈ 2 600 gas | returns `bytes32` (zero‑filled when unset). |
| `updateMetadataDigest(bytes32)` | `external` | `nonpayable` | 35 000–40 000 gas (EIP‑1559 baseline) | caller **must** be the avatar address. |

### 2.2 IPFS

* The SDK only targets the HTTP API (`/api/v0/...`).  
* Default base URL is `http://127.0.0.1:5001`; override via `IpfsStore` ctor argument.  
* **Pinning semantics**: every `Add*Async(*, pin: true)` call sets both `pin=true` and `wrap=false`. Caller is responsible for cluster replication and GC pin‑protection.  
* `CalcCidAsync` uses `only-hash=true` to avoid network overhead.

---

## 3. Data Model (JSON, RFC 8785 canonical form)

### 3.1 `Profile`

```jsonc
{
  "schemaVersion": "1.1",          // frozen constant
  "previewImageUrl": "https://…",  // optional, not used by SDK
  "name": "Alice",                 // UTF‑8, <=64 bytes recommended
  "description": "Demo",           // UTF‑8, <=280 bytes recommended
  "namespaces": {                  // string -> CID (NameIndexDoc)
    "bob": "Qm…"
  },
  "signingKeys": {                 // fingerprint -> SigningKey
    "0xdeadbeef": {
      "publicKey": "0x04…",        // uncompressed secp256k1
      "validFrom": 1712197610      // Unix s
    }
  }
}
````

### 3.2 `NameIndexDoc`

```jsonc
{
  "head": "Qm…",                   // CID of latest NamespaceChunk
  "entries": {
    "logicalName": "QmChunkCid"    // owner chunk for random access
  }
}
```

### 3.3 `NamespaceChunk`

```jsonc
{
  "prev": "QmOlderChunkOrNull",
  "links": [ CustomDataLink, … ]   // newest element appended last
}
```

*Constant*: `Helpers.ChunkMaxLinks == 100`. On overflow a new chunk is started and `prev` linked.

### 3.4 `CustomDataLink` (signable payload)

| Field                      | Type                  | Value source                      | In canonicalised hash?                          |
| -------------------------- | --------------------- | --------------------------------- | ----------------------------------------------- |
| `name`                     | string                | caller                            | **yes**                                         |
| `cid`                      | string                | caller or `IpfsStore.Add*` return | **yes**                                         |
| `encrypted`                | bool                  | caller                            | **yes**                                         |
| `encryptionAlgorithm`      | string?               | caller                            | **yes**                                         |
| `encryptionKeyFingerprint` | string?               | caller                            | **yes**                                         |
| `signerAddress`            | string                | populated during `Sign()`         | **yes**                                         |
| `signedAt`                 | int64 Unix s          | `DateTimeOffset.UtcNow`           | **yes**                                         |
| `nonce`                    | `0x` + 16 byte random | `CustomDataLink.NewNonce()`       | **yes**                                         |
| `signature`                | `0x` + 65 byte ECDSA  | populated during `Sign()`         | **no** (explicitly omitted from canonical JSON) |

**Signing algorithm** (`DefaultLinkSigner`):

```
hash = keccak256( canonicalJson(link --without--> signature) )
sig  = secp256k1_sign( hash, privKey )
```

Signature verification fails if:

* Signature is mal‑formed (`ExtractECDSASignature` throws).
* Recovered pubkey’s EIP‑55 address ≠ `signerAddress`.

---

## 4. Public API Surface (C#)

### 4.1 `IIpfsStore`

*Atomicity & ordering guarantees*: none; caller orchestrates commit order.
*Fault tolerance*: all methods propagate `HttpRequestException` and `TaskCanceledException` as‑is.

### 4.2 `INameRegistry`

*`UpdateProfileCidAsync`* requires avatar to sign the tx; the SDK derives the avatar from `EthECKey(priv).GetPublicAddress()` and injects it into `FunctionMessage.FromAddress`.

### 4.3 `INamespaceWriter`

```
Task<CustomDataLink> AddJsonAsync(name, json, priv)
Task<CustomDataLink> AttachExistingCidAsync(name, cid, priv)
Task<IReadOnlyList<CustomDataLink>> AddJsonBatchAsync(items, priv)
Task<IReadOnlyList<CustomDataLink>> AttachCidBatchAsync(items, priv)
```

All mutators are idempotent on caller crash: partially written chunks are never referenced by the profile‑level index.

---

## 5. Error Handling Strategy

1. **Cryptographic failures** – throw `ArgumentException` or `InvalidOperationException`.
2. **Remote IO (IPFS / RPC)** – bubble the underlying `HttpRequestException`.
3. **CID validation** – `CidConverter.CidToDigest` throws `ArgumentException` on length or prefix mismatch.
4. **Thread‑safety** – `NamespaceWriter` is *not* thread‑safe; guard with external locks if concurrent writes are possible.
5. **Cancellation** – every async method exposes `CancellationToken ct` (default `default`). No silent swallowing.

---

## 6. Build, Test, Lint Matrix

| Command                             | Expectation                                                                     |
|-------------------------------------|---------------------------------------------------------------------------------|
| `dotnet build -warnaserror`         | succeeds with 0 warnings (SDK, demo, tests).                                    |
| `dotnet test`                       | all tests pass.                                                                 |
| `dotnet format --verify-no-changes` | no diff (repository is style‑clean).                                            |
| `ipfs swarm peers`                  | non‑zero required **only** for cross‑node visibility; local tests work offline. |

---

## 7. Limitations (by design, PoC)

* **No NuGet packaging** – reference via git submodule or project reference.
* **Encryption placeholders** – `encrypted=true` is written but the SDK provides **no** encryption helper yet.
* **Hard‑coded RPC URL in demo** – override via env `RPC_URL`.
* **Assumes secp256k1 keys** – elliptic curve algorithm is not abstracted.
* **No gas price tuning** – Nethereum defaults; adjust `Web3.TransactionManager` as needed.

---

## 8. Migration & Compatibility Rules

| Change vector               | Backwards compatible?                  | Mitigation                            |
| --------------------------- | -------------------------------------- | ------------------------------------- |
| Increase `schemaVersion`    | **No** – clients must gate on version. | bump major, provide converter.        |
| Increase `ChunkMaxLinks`    | **Yes** – readers ignore chunk length. | none.                                 |
| Add optional fields to DTOs | **Yes**                                | JSON unknown‑field tolerance enabled. |
| Re‑encode Canonical JSON    | **No** – would break signatures.       | freeze function.                      |

---

## 9. License

MIT