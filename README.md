# Circles extensible profiles PoC

Circles Profiles SDK lets you give every address on‑chain a portable, tamper‑proof identity card—stored on IPFS,
notarised by a 32‑byte hash on Gnosis Chain, and easy to read or extend from any C#, TypeScript, or JavaScript project.

You can use it to build things like:

* **A server‑less wallet‑to‑wallet inbox or notification feed** – each message is a signed, replay‑protected IPFS blob, verifiable by any client without trusting a backend.
* **Cross‑dApp user preferences that sync automatically across devices** – themes, RPC endpoints, or feature flags written by the dApp, approved in‑wallet, and readable everywhere.

---

## 1 · Bird’s‑eye view

| Layer                   | Purpose                                                                                | Shipped here                                                         |
| ----------------------- | -------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| **Registry (Solidity)** | `avatar → SHA‑256 digest` of the current profile document (one 32‑byte slot per user). | `NameRegistry` client (direct EOA calls or Safe → `execTransaction`) |
| **IPFS (any daemon)**   | Persists immutable blobs: profile JSON, namespace indices/chunks, arbitrary user data. | `IpfsStore` (8 MiB cap, CID‑v0 only)                                 |
| **SDK**                 | High‑level primitives in C#, TypeScript and JS.                                        | this repo                                                            |
| **Demo / Gateway**      | CLI helper (`dotnet run …`) and an ActivityPub facade.                                 | `ExtensibleProfilesDemo`, `Circles.ActivityPubGateway`               |

---

## 2 · Core data model

```text
Profile  (one per user, mutable, IPFS CID pinned)
├─ namespaces : { key → index‑CID }     // arbitrary keys, usually recipient addresses
├─ signingKeys: { fp  → public‑key }    // rotating long‑term keys
└─ misc fields : name, description, images, …
```

### Namespace → append‑only log

```
index‑doc  (tiny)
  ├─ head     → newest chunk‑CID
  └─ entries  : logical‑name → owning‑chunk‑CID

chunk (≤ 100 links, immutable)
  ├─ prev     → older chunk‑CID | null
  └─ links[]  → CustomDataLink
```

### `CustomDataLink` → signed envelope

| field                        | note                                      |
| ---------------------------- | ----------------------------------------- |
| `name`                       | logical ID (e.g. `msg‑42`)                |
| `cid`                        | payload (any IPFS object)                 |
| `signerAddress`              | EOA **or** Safe that vouches for the link |
| `signedAt / nonce / chainId` | replay protection                         |
| `signature`                  | 65‑byte `r s v` (lower‑case hex)          |

The JSON is canonicalised (RFC 8785, **`signature` dropped**) before hashing
and signing.

* **EOA** → plain ECDSA, enforced **low‑S** (EIP‑2)
* **Safe / contract** → ERC‑1271 with graceful `bytes32` / `bytes` fallback

---

## 3 · What the SDK gives you

### ✔ Write

```csharp
IIpfsStore      ipfs = new IpfsStore();
INameRegistry   reg  = new NameRegistry(privKey, "<rpc>");
var store            = new ProfileStore(ipfs, reg);

var profile = await store.FindAsync(myAddress) ?? new Profile { Name = "Alice" };

var signer  = new DefaultLinkSigner();                // or SafeLinkSigner
var writer  = await NamespaceWriter.CreateAsync(profile, recipient, ipfs, signer);

await writer.AddJsonAsync("msg‑1", "{\"txt\":\"gm\"}", privKey);
await store.SaveAsync(profile, privKey);              // pins JSON + updates registry
```

### ✔ Read (on‑the‑fly sig checks)

```csharp
var profCid  = await reg.GetProfileCidAsync(sender);
var profile  = JsonSerializer.Deserialize<Profile>(await ipfs.CatStringAsync(profCid))!;

var idxCid   = profile.Namespaces[myAddress.ToLowerInvariant()];
var idx      = await Helpers.LoadIndex(idxCid, ipfs);

var verifier = new DefaultSignatureVerifier(new EthereumChainApi(web3, 100));
var reader   = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

await foreach (var link in reader.StreamAsync())
    Console.WriteLine($"{link.Name}  →  {link.Cid}");
```

### ✔ Safe support

* **`SafeLinkSigner`** – link’s `signerAddress` **is** the Safe, but the proof
  is generated with the owner EOA and passes on‑chain `isValidSignature`.
* **`GnosisSafeExecutor`** – convenience for single‑owner Safes to push profile
  updates without manual multisig UX.

---

## 4 · Guarantees, integrity & foot‑guns

### 4‑a  What the SDK guarantees – and what it doesn’t

| Theme                          | ✅ SDK guarantees                                                                                                           | ⚠️ Implementor must handle                                                          |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| **Cryptographic authenticity** | RFC 8785 canonical JSON, ECDSA low‑S, ERC‑1271 (`bytes32` + `bytes` fallback).                                             | Use a reliable RPC – pruned or rate‑limited nodes break `eth_getCode` / `eth_call`. |
| **Replay & tamper protection** | `nonce`, `signedAt`, `chainId` checked; duplicate nonces rejected; duplicate JSON keys rejected.                           | If you mirror links to your DB you must enforce the same nonce window.              |
| **Operator key lifecycle**     | `AcceptSignedLinkAsync` validates EOA fingerprints against operator’s `signingKeys` and `validFrom / validTo / revokedAt`. | Keep the operator profile current when rotating or revoking keys.                   |
| **Chunk/index consistency**    | Atomic commit (pin head‑chunk → compute index CID → update profile → pin index). Unit‑tests cover rotation/bulk.           | Manually editing pinned JSON can leave dangling CIDs – SDK won’t heal that.         |
| **Download size**              | Hard‑caps IPFS reads at **8 MiB** (header & stream).                                                                       | Store bigger blobs elsewhere and link them.                                         |
| **Safe happy‑path**            | Single‑owner Safe v1.3.0 handled end‑to‑end.                                                                               | Threshold > 1, modules, future Safe ABIs need custom code.                          |
| **Data availability**          | None.                                                                                                                      | Run a pin service/cluster or accept that blobs may disappear.                       |
| **Privacy / ACL**              | None – payloads are public.                                                                                                | Encrypt before `AddJsonAsync`, set `encrypted=true`, manage keys externally.        |
| **Gas payment**                | Not covered.                                                                                                               | Ensure the key calling `updateMetadataDigest` has funds.                            |

### 4‑b  Common foot‑guns

1. **Forgetting to pin** – `AddJsonAsync` pins, but `AttachExistingCidAsync` assumes *you* pinned the CID.
2. **Clock skew** – `signedAt` is trusted for ordering; keep device clocks within ±30 s.
3. **Cross‑chain confusion** – sign on the intended `chainId`; mixing nonces across chains breaks replay‑protection.
4. **Address case‑mismatch** – namespaces are lower‑cased; keep UI/API look‑ups the same.
5. **Assuming verified ⇒ sane** – signature ≠ business‑logic validation. Always sanity‑check JSON payloads before acting.

---

## 5 · Repo layout

```
/Circles.Profiles.Sdk            C# reference implementation + NUnit tests
/js                              Isomorphic TS/JS port (ESM & CJS bundles)
/ExtensibleProfilesDemo          Minimal CLI – create, send, inbox, link
/Circles.ActivityPubGateway      Maps profiles & namespaces → ActivityPub
/Circles.RealSafeE2E             Real‑chain Safe e2e tests against Gnosis Chain
```

---

## 6 · Running the tests

```bash
# .NET
dotnet test                  # needs .NET 9 SDK+

# TypeScript
pnpm install
pnpm vitest
```

The C# suite uses an in‑memory IPFS stub; Safe e2e’s hit the public Gnosis RPC –
export `PRIVATE_KEY` with a funded account first.

---

## 7 · Usage examples

### Example 1 – New profile

```csharp
var store   = new ProfileStore(ipfs, reg);
var alice   = new Profile { Name = "Alice", Description = "web3 dev" };

await store.SaveAsync(alice, privKey);
```

---

### Example 2 – Alice → Bob (message)

```csharp
var writer = await NamespaceWriter.CreateAsync(alice, bobAddr, ipfs, signer);

await writer.AddJsonAsync("greeting‑001", "{\"txt\":\"Hello Bob! 👋\"}", alicePriv);
await store.SaveAsync(alice, alicePriv);
```

---

### Example 3 – Bob validates incoming links

```csharp
var idxCid   = aliceProfile.Namespaces[bobAddr.ToLowerInvariant()];
var idx      = await Helpers.LoadIndex(idxCid, ipfs);
var reader   = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

await foreach (var link in reader.StreamAsync())
    Console.WriteLine(await ipfs.CatStringAsync(link.Cid));
```

---

### Example 4 – Publishing via Safe

```csharp
var safeSigner = new SafeLinkSigner(safeAddr, chainApi);
var writer     = await NamespaceWriter.CreateAsync(profile, recipient, ipfs, safeSigner);

await writer.AddJsonAsync("announcement", "{\"txt\":\"v1 live\"}", ownerPriv);
await store.SaveAsync(profile, ownerPriv);   // wrapped in execTransaction underneath
```

---

### Example 5 – JS/TS – iterate links

```ts
const profileCid = await registry.getProfileCid(sender);
const profile = JSON.parse(await ipfs.catString(profileCid));

const idxCid = profile.namespaces[recipient.toLowerCase()];
const idx = await Helpers.loadIndex(idxCid, ipfs);

const reader = new DefaultNamespaceReader(idx.head, ipfs, verifier);
for await (const link of reader.stream()) console.log(link);
```

---

### Example 6 – **dApp settings stored in the user profile**

A dApp wants **user‑specific settings** available on every device **without** running its own backend.
It writes into the user’s profile under the dApp’s *operator namespace* (its Safe address).
The wallet validates the link against the dApp’s published signing‑keys and commits it.

#### 6‑a  dApp crafts the link

```csharp
var settings = new { theme = "dark", rpcUrl = "https://rpc.gnosis.io" };
var cid      = await ipfs.AddJsonAsync(JsonSerializer.Serialize(settings), pin:true);

var safeSigner  = new SafeLinkSigner("0xDappSafe", chainApi);
var draft       = new CustomDataLink
{
    Name      = "prefs‑v1",
    Cid       = cid,
    ChainId   = Helpers.DefaultChainId,
    SignedAt  = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    Nonce     = CustomDataLink.NewNonce(),
    Encrypted = false
};

var signedLink = safeSigner.Sign(draft, safeOwnerPrivKey);
// hand `signedLink` to the user (WalletConnect, deeplink, etc.)
```

#### 6‑b  Wallet verifies + stores

```csharp
var dappProfile = await profileStore.FindAsync("0xDappSafe")!; // contains signingKeys

var writer = await NamespaceWriter.CreateAsync(userProfile,
               "0xDappSafe", ipfs, new DefaultLinkSigner());

await writer.AcceptSignedLinkAsync(signedLink, dappProfile);
await profileStore.SaveAsync(userProfile, userPrivKey);
```

#### 6‑c  Any device reads the settings

```csharp
var idxCid = userProfile.Namespaces["0xdappsafe"];
var idx    = await Helpers.LoadIndex(idxCid, ipfs);
var reader = new DefaultNamespaceReader(idx.Head, ipfs, verifier);

var latest = await reader.GetLatestAsync("prefs‑v1");
var raw    = await ipfs.CatStringAsync(latest!.Cid);

var prefs  = JsonSerializer.Deserialize<Dictionary<string,object>>(raw);
// prefs["theme"] == "dark"
```

✅ No servers, no accounts table – just IPFS, a tiny on‑chain slot and this SDK.

---

## 8 · License

MIT
