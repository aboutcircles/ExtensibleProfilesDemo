# Circles Profiles – Proof‑of‑Concept SDK

*(schema v 1.1, .NET 9.0)*

Circles Profiles combine three independent layers:

1. **Profile document** – a JSON object describing an avatar, pinned to IPFS.  
   Its CID (CID‑v0, sha2‑256) is transformed to a 32‑byte digest and published on‑chain in the
   **Name‑Registry** (`updateMetadataDigest`).  
   Only the avatar address (EOA or Gnosis Safe) is authorised to perform the update.

2. **Namespaces** – append‑only logs identified by the pair  
   **(owner avatar, namespace key)**.  
   Each write produces a `CustomDataLink` containing the payload CID, metadata and a signature.  
   The links are stored in fixed‑size *chunks* (100 links) that are chained via `prev` pointers; a
   profile‑level `NameIndexDoc` ensures O (1) lookup of the most recent link for any logical name.
   A writer attached to the key `recipientAvatar` therefore serves as an **outbox** for the owner and
   as an **inbox** for the recipient; no direct network communication is involved.

3. **Cryptography** – every link is signed.  
   `DefaultLinkSigner` signs with an EOA key; `SafeLinkSigner` creates a 65‑byte blob that satisfies
   `isValidSignature` on a Gnosis Safe (ERC‑1271).  
   `DefaultSignatureVerifier` checks EOAs by public‑key recovery and contracts by the two ERC‑1271
   variants (`bytes32`, then `bytes`).

All data except the 32‑byte digest stays off‑chain; the on‑chain footprint is one storage slot per
avatar.

---

## 1. Repository topology

| Folder / project              | Assembly                        | Purpose                                                                                 | Target |
|-------------------------------|---------------------------------|-----------------------------------------------------------------------------------------|--------|
| `Circles.Profiles.Models`     | **Circles.Profiles.Models**     | DTOs that define the canonical JSON schema.                                             | net9.0 |
| `Circles.Profiles.Interfaces` | **Circles.Profiles.Interfaces** | Pure interfaces and records that decouple storage and chain access.                     | net9.0 |
| `Circles.Profiles.Sdk`        | **Circles.Profiles.Sdk**        | Reference implementation: IPFS HTTP 0.18 and on‑chain registry access via Nethereum     | net9.0 |
| `Circles.Profiles.Sdk.Tests`  | *(test project)*                | Deterministic NUnit 3.15 test‑suite covering crypto, chunking and edge‑cases.           | net9.0 |
| `ExtensibleProfilesDemo`      | **ExtensibleProfilesDemo**      | Minimal CLI demonstrating key management, profile CRUD, messaging, and inbox traversal. | net9.0 |

Build: `dotnet build -c Release`  
Tests: `dotnet test`

---

## 2. Chain and storage dependencies

### 2.1 Name‑Registry

| Property         | Value                                              |
|------------------|----------------------------------------------------|
| Network          | Gnosis Chain (chain‑id `100`)                      |
| Contract address | `0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474`       |
| ABI              | Embedded in `Circles.Profiles.Sdk/NameRegistry.cs` |

| Function                                 | State mutability | Notes                        |
|------------------------------------------|------------------|------------------------------|
| `getMetadataDigest(address)` → `bytes32` | `view`           | Returns all‑zero when unset  |
| `updateMetadataDigest(bytes32)`          | `nonpayable`     | Caller must equal the avatar |

Required RPC methods: `eth_call`, `eth_sendRawTransaction`, `eth_getTransactionReceipt`, `eth_chainId`,
`eth_blockNumber`, `net_version`.

### 2.2 IPFS

* HTTP API (`/api/v0/*`), default base `http://127.0.0.1:5001`.
* `IpfsStore.Add*Async(..., pin: true)` performs `pin=true`, `wrap=false`.
* `IpfsStore.CalcCidAsync` uses `only-hash=true`.

---

## 3. Data‑model

### 3.1 `Profile`

```jsonc
{
  "schemaVersion": "1.1",
  "previewImageUrl": "data:...",
  "imageUrl": "ipfs://…",
  "name": "Alice",
  "description": "demo user",
  "namespaces": {
    "bob": "QmIndexCid"
  },
  "signingKeys": {
    "0xdeadbeef": {
      "publicKey": "0x04…",
      "validFrom": 1712197610
    }
  }
}
````

### 3.2 `NameIndexDoc`

```jsonc
{
  "head": "QmNewestChunkCid",
  "entries": {
    "logicalName": "QmChunkCid"
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

`Helpers.ChunkMaxLinks == 100`; a chunk that reaches this limit is closed and a new head chunk is created.

### 3.4 `CustomDataLink`

| Field                               | Included in hash | Filled by                                 |
|-------------------------------------|------------------|-------------------------------------------|
| `name` *(string)*                   | ✔︎               | caller                                    |
| `cid` *(string)*                    | ✔︎               | caller or `IpfsStore`                     |
| `encrypted` *(bool)*                | ✔︎               | caller                                    |
| `encryptionAlgorithm` *(?str)*      | ✔︎               | caller                                    |
| `encryptionKeyFingerprint` *(?str)* | ✔︎               | caller                                    |
| `chainId` *(int64)*                 | ✔︎               | constant `Helpers.DefaultChainId` (`100`) |
| `signerAddress` *(string)*          | ✔︎               | set by signer                             |
| `signedAt` *(Unixs)*                | ✔︎               | `DateTimeOffset.UtcNow`                   |
| `nonce` *(0x + 16 B random)*        | ✔︎               | `CustomDataLink.NewNonce()`               |
| `signature` *(0x + 65 B)*           | ✘                | set by signer                             |

`hash = keccak256( RFC 8785‑canonical‑JSON(link WITHOUT signature) )`

---

## 4. Public API (C# interfaces)

```csharp
// IPFS abstraction
public interface IIpfsStore {
    Task<string> AddJsonAsync(string json, bool pin = true, CancellationToken ct = default);
    Task<Stream> CatAsync(string cid, CancellationToken ct = default);
    Task<string> CalcCidAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default);
}

// On‑chain name‑registry
public interface INameRegistry {
    Task<string?> GetProfileCidAsync(string avatar, CancellationToken ct = default);
    Task<string?> UpdateProfileCidAsync(string avatar, byte[] digest32, CancellationToken ct = default);
}

// Profile CRUD
public interface IProfileStore {
    Task<Profile?> FindAsync(string avatar, CancellationToken ct = default);
    Task<(Profile prof, string cid)> SaveAsync(Profile p, string signerPrivKey, CancellationToken ct = default);
}

// Namespace writer (append‑only log)
public interface INamespaceWriter {
    Task<CustomDataLink> AddJsonAsync(string name, string json, string priv, CancellationToken ct = default);
    Task<IReadOnlyList<CustomDataLink>> AddJsonBatchAsync(IEnumerable<(string name,string json)> items, string priv, CancellationToken ct = default);
    /* AttachExistingCid* variants omitted for brevity */
}

// Cryptography helpers
public interface ILinkSigner        { CustomDataLink Sign(CustomDataLink link, string privKeyHex); }
public interface ISignatureVerifier { Task<bool> VerifyAsync(byte[] hash, string signer, byte[] sig, CancellationToken ct = default); }
public interface IChainApi          { /* GetCodeAsync, CallIsValidSignatureAsync, GetSafeNonceAsync, Id */ }
```

Reference implementations are provided in `Circles.Profiles.Sdk`.

---

## 5. Runtime guarantees and error handling

| Area                   | Behaviour in code                                                                                                        |
|------------------------|--------------------------------------------------------------------------------------------------------------------------|
| Chunk rotation         | When the head chunk reaches `ChunkMaxLinks`, it is pinned and its CID recorded in the index before a new head is created |
| Partial writes         | The profile‑level index is updated after the new head chunk CID is secured                                               |
| Signature malleability | `DefaultSignatureVerifier` rejects signatures with high‑S values                                                         |
| Invalid JSON in chunk  | `Helpers.LoadChunk` throws `InvalidDataException` and embeds the CID in the exception message                            |
| Cancellation           | All public async methods accept `CancellationToken` parameters                                                           |
| Thread‑safety          | `NamespaceWriter` is not thread‑safe; external synchronisation is required for concurrent callers                        |
| Exception propagation  | Cryptographic misuse → `ArgumentException` / `InvalidOperationException`; HTTP/RPC errors propagate unchanged            |

---

## 6. Build, test and lint

| Command                     | Expected result           |
|-----------------------------|---------------------------|
| `dotnet build -warnaserror` | succeeds without warnings |
| `dotnet test`               | all tests pass            |

---

## 7. Demonstration CLI (`ExtensibleProfilesDemo`)

_Note: Generated keys must be funded before you can successfully call `create` or `send`._

| Verb                                      | Functionality                                                    |
|-------------------------------------------|------------------------------------------------------------------|
| `keygen --alias foo`                      | Generate and store a secp256k1 key                               |
| `keyls`, `keyuse --alias foo`             | List keys and set the current key                                |
| `create --name n --description d`         | Create a profile and publish its CID                             |
| `send --to addr --type text --text "hi"`  | Write a link into the recipient namespace and update the profile |
| `inbox --me addr --trust csv`             | Read new messages since the last timestamp                       |
| `link --ns addr --name logical --cid Qm…` | Attach an existing CID                                           |
| `smoke`                                   | Run a scripted demo (requires aliases `alice`, `bob`, `charly`)  |

Default RPC endpoint: `https://rpc.aboutcircles.com` (override via `RPC_URL`).

---

## 8. Known limitations

* Encryption helpers are not implemented (`encrypted` flag is present but unused)
* No published NuGet package (use project references or a git submodule)
* Gas price handling relies on default Nethereum behaviour
* `NamespaceWriter` must be serialised by the caller for concurrent writes

---

## 9. License

MIT