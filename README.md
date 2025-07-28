# Circles Profiles – Proof‑of‑Concept SDK

*(schema v 1.1, .NET 9.0)*

Circles Profiles turn an ordinary **EOA** (or **Smart‑Contract Wallet**) address on **Gnosis Chain** (chain‑id `100`,
0x64) into a user‑controlled, append‑only store that lives **mostly off‑chain** on **IPFS**.

The SDK shipped in this repository handles the full round‑trip:

* serialising profile & link objects to canonical JSON (RFC 8785 ‑compatible),
* pinning payloads to IPFS,
* publishing a `bytes32` digest in the on‑chain **Name‑Registry**,
* verifying signatures (EOA & ERC‑1271 contracts),
* reading / writing *namespaces* that behave like mini‑logs or DM inboxes.

---

## 1. Repository topology

| Folder / project              | Assembly                        | Purpose                                                                                              | Target |
|-------------------------------|---------------------------------|------------------------------------------------------------------------------------------------------|--------|
| `Circles.Profiles.Models`     | **Circles.Profiles.Models**     | *Immutable* DTOs – the canonical JSON surfaces shared across language boundaries. Zero deps.         | net9.0 |
| `Circles.Profiles.Interfaces` | **Circles.Profiles.Interfaces** | Abstraction layer (pure interfaces & records). Keeps the SDK decoupled from storage / chain clients. | net9.0 |
| `Circles.Profiles.Sdk`        | **Circles.Profiles.Sdk**        | Reference implementation: IPFS HTTP 0.18 + on‑chain registry access via **Nethereum 6.0‑preview**.   | net9.0 |
| `Circles.Profiles.Sdk.Tests`  | *(test project)*                | \~250 deterministic NUnit 3.15 assertions covering crypto, chunking, edge‑cases.                     | net9.0 |
| `ExtensibleProfilesDemo`      | **ExtensibleProfilesDemo**      | Minimal CLI showcasing key‑mgmt, profile CRUD, messaging, inbox walk.                                | net9.0 |

> Build determinism: `dotnet build -c Release` (no extra feeds).
> All tests: `dotnet test`.

---

## 2. Chain & storage dependencies

### 2.1 Name‑Registry (on‑chain)

| Property | Value                                                                      |
|----------|----------------------------------------------------------------------------|
| Network  | Gnosis Chain / chain‑id `100`                                              |
| Address  | `0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474`                               |
| ABI      | two functions, embedded verbatim in `Circles.Profiles.Sdk/NameRegistry.cs` |

| Function                                 | State mut.   | Notes                                 |
|------------------------------------------|--------------|---------------------------------------|
| `getMetadataDigest(address)` → `bytes32` | `view`       | returns all‑zero when unset           |
| `updateMetadataDigest(bytes32)`          | `nonpayable` | caller **must** be the avatar address |

Required JSON‑RPC methods: `eth_call`, `eth_sendRawTransaction`, `eth_getTransactionReceipt`, `eth_chainId`,
`eth_blockNumber`, `net_version`.

### 2.2 IPFS

* HTTP API (`/api/v0/*`) only – default base `http://127.0.0.1:5001`.
* `IpfsStore.Add*Async(…, pin: true)` sets `pin=true`, `wrap=false`.
* `CalcCidAsync` uses `only-hash=true` (no network).

---

## 3. Data‑model (canonical JSON)

### 3.1 `Profile`

```jsonc
{
  "schemaVersion": "1.1",
  "previewImageUrl": "https://…",     // optional
  "imageUrl": "ipfs://…",             // optional – large avatar, not used by SDK
  "name": "Alice",
  "description": "demo user",
  "namespaces": {                     // key = lower‑cased namespace‑key
    "bob": "QmIndexCid"
  },
  "signingKeys": {                    // key = 4‑byte fingerprint
    "0xdeadbeef": {
      "publicKey": "0x04…",           // uncompressed secp256k1
      "validFrom": 1712197610
    }
  }
}
```

### 3.2 `NameIndexDoc`

```jsonc
{
  "head": "QmNewestChunkCid",
  "entries": {
    "logicalName": "QmChunkCid"       // “owning chunk” for O(1) random access
  }
}
```

### 3.3 `NamespaceChunk`

```jsonc
{
  "prev": "QmOlderChunkCidOrNull",
  "links": [ /* CustomDataLink … newest appended last */ ]
}
```

Constant `Helpers.ChunkMaxLinks == 100`; overflow starts a fresh chunk and links `prev`.

### 3.4 `CustomDataLink`

| Field                               | In hash? | Filled by                        |
|-------------------------------------|----------|----------------------------------|
| `name` *(string)*                   | ✔︎       | caller                           |
| `cid` *(string)*                    | ✔︎       | caller or `IpfsStore`            |
| `encrypted` *(bool)*                | ✔︎       | caller *(placeholder)*           |
| `encryptionAlgorithm` *(?str)*      | ✔︎       | caller                           |
| `encryptionKeyFingerprint` *(?str)* | ✔︎       | caller                           |
| `chainId` *(int64)*                 | ✔︎       | `Helpers.DefaultChainId` (= 100) |
| `signerAddress` *(string)*          | ✔︎       | set by signer                    |
| `signedAt` *(Unixs)*                | ✔︎       | `DateTimeOffset.UtcNow`          |
| `nonce` *(0x + 16 B random)*        | ✔︎       | `CustomDataLink.NewNonce()`      |
| `signature` *(0x + 65 B)*           | ✘        | set by signer                    |

**Canonical hash:**
`hash = keccak256( RFC 8785‑canonical‑JSON(link WITHOUT signature) )`

---

## 4. Public API (C# interfaces)

```csharp
// pure IPFS
public interface IIpfsStore {
    Task<string> AddJsonAsync(string json, bool pin = true, CancellationToken ct = default);
    Task<Stream> CatAsync(string cid, CancellationToken ct = default);
    Task<string> CalcCidAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default);
}

// on‑chain registry
public interface INameRegistry {
    Task<string?> GetProfileCidAsync(string avatar, CancellationToken ct = default);
    Task<string?> UpdateProfileCidAsync(string avatar, byte[] metadataDigest32, CancellationToken ct = default);
}

// profile CRUD (pin + publish only)
public interface IProfileStore {
    Task<Profile?> FindAsync(string avatar, CancellationToken ct = default);
    Task<(Profile prof, string cid)> SaveAsync(Profile p, string signerPriv, CancellationToken ct = default);
}

// namespace (append‑only log)
public interface INamespaceWriter {
    Task<CustomDataLink> AddJsonAsync(string name, string json, string signerPrivKey, CancellationToken ct = default);
    Task<IReadOnlyList<CustomDataLink>> AddJsonBatchAsync(IEnumerable<(string name,string json)> items, string priv, CancellationToken ct = default);
    /* …AttachExistingCid* variants omitted for brevity … */
}

// crypto helpers
public interface ILinkSigner { CustomDataLink Sign(CustomDataLink link, string privKeyHex); }
public interface ISignatureVerifier { Task<bool> VerifyAsync(byte[] hash,string signer,byte[] sig,CancellationToken ct=default); }
public interface IChainApi { /* GetCodeAsync, CallIsValidSignatureAsync, GetSafeNonceAsync … */ }
```

Reference implementations live in `Circles.Profiles.Sdk`:

* **IpfsStore** – thin wrapper over `Ipfs.Http.IpfsClient`.
* **NameRegistry** – Nethereum `FunctionMessage` abstraction.
* **DefaultLinkSigner** – plain secp256k1 + EIP‑55 address.
* **SafeLinkSigner** – produces ERC‑1271‑compatible signatures for **Gnosis Safe** contracts while still using the
  *owner* key.
* **DefaultSignatureVerifier** – EOA recovery + 1271 fallback (`bytes32` first, then `bytes`), rejects malleable
  *high‑S* signatures.
* **EthereumChainApi** – read‑only RPC helper (`eth_call`, code, Safe nonce).

---

## 5. Runtime guarantees & error handling

| Area                         | Behaviour in code                                                                                               |
|------------------------------|-----------------------------------------------------------------------------------------------------------------|
| **Chunk rotation**           | atomic: closing chunk is pinned & its CID written to index before head reset.                                   |
| **Partial writes / crashes** | profile‑level index is updated **after** head chunk CID is persisted – readers never observe dangling pointers. |
| **High‑S signatures**        | explicitly rejected (`DefaultSignatureVerifier`).                                                               |
| **Invalid JSON in chunk**    | `Helpers.LoadChunk` throws `InvalidDataException` with offending CID in message.                                |
| **Cancellation**             | every async public method accepts `CancellationToken`.                                                          |
| **Thread‑safety**            | `NamespaceWriter` is **not** thread‑safe – wrap in locks if used concurrently.                                  |
| **Exceptions**               | • cryptographic misuse → `ArgumentException` / `InvalidOperationException`                                      |
|                              | • HTTP / RPC → propagate `HttpRequestException` / `RpcResponseException`                                        |
|                              | • CID validation → `ArgumentException`.                                                                         |

---

## 6. Build / test / lint matrix

| Command                     | Expected result      |
|-----------------------------|----------------------|
| `dotnet build -warnaserror` | succeeds, 0 warnings |
| `dotnet test`               | all tests pass       |

---

## 7. Demonstration CLI (`ExtensibleProfilesDemo`)

| Verb                                      | Purpose                                                           |
|-------------------------------------------|-------------------------------------------------------------------|
| `keygen --alias foo`                      | create & store new secp256k1 key                                  |
| `keyls`, `keyuse --alias foo`             | list / select key                                                 |
| `create --name n --description d`         | initialise a profile & publish CID                                |
| `send --to addr --type text --text "hi"`  | write *CustomDataLink* into recipient namespace, update profile   |
| `inbox --me addr --trust csv`             | read new messages since last timestamp                            |
| `link --ns addr --name logical --cid Qm…` | attach existing CID                                               |
| `smoke`                                   | scripted ping‑pong demo (requires aliases `alice`,`bob`,`charly`) |

Hard‑coded RPC endpoint defaults to `https://rpc.aboutcircles.com`; override via `RPC_URL`.

---

## 8. Known limitations (PoC scope)

* **Encryption pipeline missing** – `encrypted=true` flag is written but SDK offers no cryptography helpers yet.
* **No NuGet package** – reference via git‑submodule or project‑reference.
* **`INamespaceReader`** interface exists but has **no** production implementation yet.
* **Gas strategy** – default Nethereum behaviour, no fee‑escalation logic.
* **Single‑thread writer** – external synchronisation required for concurrent calls.

---

## 9. License

MIT.
