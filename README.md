# Circles Profiles SDK

> Self‑contained building blocks for **extensible, signed user profiles** that live on IPFS and are discoverable through
> a tiny on‑chain registry.

---

## 1 ‑ Bird’s‑eye view

| Layer                   | Purpose                                                                                | Shipped here                                                            |
|-------------------------|----------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| **Registry (Solidity)** | `avatar → SHA‑256 digest` of the current profile document. One 32‑byte slot per user.  | `NameRegistry` client<br>(direct EOA calls or Safe → `execTransaction`) |
| **IPFS (any daemon)**   | Persists immutable blobs: profile JSON, namespace indices/chunks, arbitrary user data. | `IpfsStore` (8 MiB cap, CID‑v0 only)                                    |
| **SDK**                 | High‑level primitives in C#, TypeScript and JS.                                        | this repo                                                               |
| **Demo / Gateway**      | CLI helper (`dotnet run ...`) and an ActivityPub facade.                               | `ExtensibleProfilesDemo`, `Circles.ActivityPubGateway`                  |

---

## 2 ‑ Core data model

```text
Profile (one per user, mutable, IPFS CID pinned)
├─ namespaces : { key → index‑CID }     // arbitrary keys, usually recipient addresses
├─ signingKeys: { fp  → public‑key }    // rotating long‑term keys
└─ misc fields: name, description, images …
```

### Namespace → append‑only log

```
index‑doc (tiny)
  ├─ head     → newest chunk‑CID
  └─ entries  : logical‑name → owning‑chunk‑CID

chunk (≤100 links, immutable)
  ├─ prev     → older chunk‑CID | null
  └─ links[]  → CustomDataLink
```

### CustomDataLink → signed envelope

| field                          | note                                      |   |   |   |                     |
|--------------------------------|-------------------------------------------|---|---|---|---------------------|
| `name`                         | logical identifier (e.g. `msg‑42`)        |   |   |   |                     |
| `cid`                          | payload (any IPFS object)                 |   |   |   |                     |
| `signerAddress`                | EOA **or** Safe that vouches for the link |   |   |   |                     |
| `signedAt`, `nonce`, `chainId` | replay protection                         |   |   |   |                     |
| `signature`                    | 65‑byte \`r                               |   | s |   | v\`, lower‑case hex |

The JSON is canonicalised (RFC 8785, `signature` field omitted) before hashing and signing.
Signatures:

* **EOA** → regular ECDSA, enforced *low‑S* (EIP‑2).
* **Contract / Safe** → ERC‑1271 with graceful fallback between `bytes32` and `bytes` variants.

---

## 3 ‑ What the SDK gives you

### ✔ Write

```csharp
IIpfsStore   ipfs   = new IpfsStore();
INameRegistry reg    = new NameRegistry(privKey, "<rpc‑url>");
var store           = new ProfileStore(ipfs, reg);

var profile = await store.FindAsync(myAddress) ?? new Profile { Name = "Alice" };

var signer  = new DefaultLinkSigner();             // or SafeLinkSigner
var writer  = await NamespaceWriter.CreateAsync(profile, recipient, ipfs, signer);

await writer.AddJsonAsync("msg‑1", "{\"txt\":\"gm\"}", privKey);
await store.SaveAsync(profile, privKey);           // pins JSON + updates registry
```

### ✔ Read (with on‑the‑fly signature checks)

```csharp
var registry = new NameRegistry(privKey, "<rpc>");
var profCid  = await registry.GetProfileCidAsync(sender);
var profJson = await ipfs.CatStringAsync(profCid);
var profile  = JsonSerializer.Deserialize<Profile>(profJson)!;

var idxCid   = profile.Namespaces[myAddress.ToLowerInvariant()];
var idx      = await Helpers.LoadIndex(idxCid, ipfs);
var verifier = new DefaultSignatureVerifier(new EthereumChainApi(web3, 100));
var reader   = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

await foreach (var link in reader.StreamAsync())
    Console.WriteLine($"{link.Name}  →  {link.Cid}");
```

### ✔ Safe support

* `SafeLinkSigner` crafts links whose `signerAddress` is the Safe itself yet
  are signed with an owner’s EOA key and **pass on‑chain `isValidSignature`**.
* `GnosisSafeExecutor` wraps `execTransaction` for single‑owner Safes so the CLI
  (and tests) can publish profile digests without manual multisig steps.

---

## 4 ‑ Integrity & limits

* Canonical JSON **rejects duplicate properties** and non‑finite numbers.
* IPFS download hard‑cap: **8 MiB** per blob (checked before and during stream).
* CID checker is intentionally strict: **CID‑v0 (`Qm…`, Base58btc) only**.

---

## 5 ‑ Repository layout

```
/Circles.Profiles.Sdk         C# reference implementation + unit tests (NUnit)
/js                           Isomorphic TS/JS port (Esm & CJS bundles)
/ExtensibleProfilesDemo       Minimal CLI – create, send, inbox, link, smoke‑test
/Circles.ActivityPubGateway   Maps profiles + namespaces → ActivityPub outboxes
/Circles.RealSafeE2E          Real Safe end‑to‑end tests against Gnosis Chain
```

---

## 6 ‑ Running the tests

```bash
# .NET
dotnet test                                # ≥ .NET 9 SDK

# TypeScript
pnpm install
pnpm vitest
```

The suite spins up an in‑memory IPFS stub for unit tests and—optionally—
talks to a real go‑IPFS daemon for the E2Es.
Safe tests target the public Gnosis RPC; set `PRIVATE_KEY` to a funded
account before running them.

---

## 7 – Usage Examples

Below are practical scenarios showing how the SDK can be used in common workflows:

### Example 1: Create a New Profile and Publish It

This example demonstrates how to initialize a profile, add metadata, and store it on IPFS and the on-chain registry.

```csharp
var ipfs = new IpfsStore();
var registry = new NameRegistry(privKey, "<rpc-url>");
var store = new ProfileStore(ipfs, registry);

var newProfile = new Profile
{
    Name = "Alice",
    Description = "Web3 enthusiast and developer",
    Namespaces = new Dictionary<string, string>(),
    SigningKeys = new Dictionary<string, string>()
};

await store.SaveAsync(newProfile, privKey);
```

---

### Example 2: Sending Signed Data to Another User's Namespace

This example demonstrates how Alice can append data (like messages or payloads) to Bob's namespace securely.

```csharp
var signer = new DefaultLinkSigner();
var writer = await NamespaceWriter.CreateAsync(aliceProfile, bobAddress, ipfs, signer);

var messageJson = "{\"txt\":\"Hello Bob! 👋\"}";
await writer.AddJsonAsync("greeting-001", messageJson, alicePrivKey);

// Update the profile to pin new data and update the registry
await store.SaveAsync(aliceProfile, alicePrivKey);
```

---

### Example 3: Reading and Validating Data from a Namespace

Bob retrieves and validates incoming data from Alice’s namespace.

```csharp
var profCid = await registry.GetProfileCidAsync(aliceAddress);
var aliceJson = await ipfs.CatStringAsync(profCid);
var aliceProfile = JsonSerializer.Deserialize<Profile>(aliceJson)!;

var idxCid = aliceProfile.Namespaces[bobAddress.ToLowerInvariant()];
var idx = await Helpers.LoadIndex(idxCid, ipfs);
var verifier = new DefaultSignatureVerifier(new EthereumChainApi(web3, 100));
var reader = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

await foreach (var link in reader.StreamAsync())
{
    var data = await ipfs.CatStringAsync(link.Cid);
    Console.WriteLine($"{link.Name}: {data}");
}
```

---

### Example 4: Using the SDK with a Gnosis Safe

Shows how to sign and publish updates to a profile using a Gnosis Safe.

```csharp
var signer = new SafeLinkSigner(ownerPrivKey, safeAddress, web3);
var writer = await NamespaceWriter.CreateAsync(profile, recipientAddress, ipfs, signer);

await writer.AddJsonAsync("announcement", "{\"txt\":\"New announcement from the team!\"}", ownerPrivKey);

var executor = new GnosisSafeExecutor(web3, safeAddress, ownerPrivKey);
await store.SaveAsync(profile, executor);
```

---

### Example 5: Listing all messages from a user’s namespace (JavaScript/TypeScript SDK)

Shows how to read profile data and iterate over namespace entries using the JS SDK.

```typescript
import {
    IpfsStore,
    NameRegistry,
    ProfileStore,
    Helpers,
    DefaultNamespaceReader,
    EthereumChainApi,
    DefaultSignatureVerifier
} from '@circles-profiles/sdk';

const ipfs = new IpfsStore();
const registry = new NameRegistry(privateKey, "<rpc-url>");

const profileCid = await registry.getProfileCid(senderAddress);
const profileJson = await ipfs.catString(profileCid);
const profile = JSON.parse(profileJson);

const namespaceCid = profile.namespaces[recipientAddress.toLowerCase()];
const index = await Helpers.loadIndex(namespaceCid, ipfs);

const verifier = new DefaultSignatureVerifier(new EthereumChainApi(web3, 100));
const reader = new DefaultNamespaceReader(index.head, ipfs, verifier);

for await (const link of reader.stream()) {
    const data = await ipfs.catString(link.cid);
    console.log(link.name, data);
}
```

---

## 8 ‑ License

MIT
