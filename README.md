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



## Known problems & decisions

### Quick risk map

| Area                      | Issue                                                              | Severity |
| ------------------------- | ------------------------------------------------------------------ | -------- |
| Crypto / Canonicalization | Non‑RFC‑8785 number handling                                       | **High** |
| Replay / Domain           | `ChainId` hard‑coded at write‑time                                 | **High** |
| Consistency / Concurrency | Partial commit & last‑write‑wins                                   | **High** |
| Verification API          | Safe “bytes” path easy to miss outside reader                      | **High** |
| Reader Semantics          | Ordering relies solely on `signedAt`                               | **Med**  |
| Error Semantics           | I/O errors misreported as “invalid sig” in one call path           | **Med**  |
| Storage I/O               | IPFS client lacks timeouts/backoff                                 | **Med**  |
| Protocol Hygiene          | CIDv0 policy not enforced end‑to‑end                               | **Med**  |
| Security / Safe Ops       | Safe owners: deployer **and** user (threshold=1) in defaults/tests | **Med**  |
| Dev Experience            | Verification throws vs returns false mismatch                      | **Low**  |
| Misc                      | Comments drift, decimal for money, CT ignored in one call          | **Low**  |


### 1) Canonical JSON number handling is not RFC‑8785 safe (**High**)

* **Context**: `CanonicalJson.WriteNumber` normalizes via `double/decimal`.
* **Cause**: Big integers and some decimals cannot be represented exactly by IEEE‑754 doubles or .NET decimals; serialization may vary across runtimes.
* **Effect**: The canonical byte sequence diverges cross‑platform ⇒ signatures don’t match between implementations; verified reads can fail for otherwise valid data.
* **Suggestion**: Restrict to **int64** and round‑trippable doubles (emit with `G17`); reject everything else (`JsonException`). Add tests for edge values (2^53±1, bigints, subnormals).
* **Status**: Open.

---

### 2) Chain domain: `CustomDataLink.ChainId` hard‑coded to 100 (**High**)

* **Context**: `NamespaceWriter` stamps `ChainId = Helpers.DefaultChainId` (100, Gnosis).
* **Cause**: Chain context isn’t sourced from signer or environment during link creation.
* **Effect**: Links minted off‑Gnosis carry a wrong domain value → weakens replay scoping, confuses tooling, and can break cross‑chain reasoning.
* **Suggestion**: Make **chain id authoritative** at signing time (preferred). Either inject `IChainApi`/chainId into the signer and override `link.ChainId` there, or feed `chainId` into the writer at creation. Ensure readers compare against that domain when they need to.
* **Status**: Open.

---

### 3) Partial commit & dangling references in `NamespaceWriter` (**High**)

* **Context**: `PersistAsync` pins the head chunk, computes `indexCid` via **hash‑only**, updates `profile.namespaces[...]` with that CID, then pins the index JSON.
* **Cause**: Profile is mutated **before** the index is actually pinned.
* **Effect**: If pinning the index fails, you have a profile that references a **dangling index CID**. Crashes between steps can also leave orphaned pointers/history.
* **Suggestion**: Pin new head ⇒ build index JSON ⇒ **pin index** ⇒ only then mutate profile and (optionally) pin profile. Consider a durable “temp + replace” for profile writes.
* **Status**: Open.

---

### 4) Last‑write‑wins: orphaned heads under concurrency (**High**)

* **Context**: Two writers start from same `indexCid`, commit different heads, and publish; last publish wins.
* **Cause**: Registry has a single digest per avatar and no atomic “update if current”.
* **Effect**: The earlier head becomes unreachable from the profile (still in IPFS, but lost to normal readers).
* **Suggestion**:

    * **Best**: Upgrade registry with `updateIfCurrent(bytes32 expected, bytes32 next)` (true CAS).
    * **Stopgap**: Client‑side optimistic check (read current digest; if it doesn’t match your base, abort and surface a **Conflict**). This **detects** many races but **cannot prevent** a race between read and inclusion in a later block. Throwing a `ConflictException` is reasonable; document it as best‑effort.
* **Status**: Open.

---

### 5) Verification API foot‑gun for Safe links (**High**)

* **Context**: `ISignatureVerifier.VerifyAsync` takes a 32‑byte hash. Safe links typically validate via **ERC‑1271(bytes)**, not `bytes32`. The reader handles the “bytes” fallback; external callers may forget.
* **Cause**: API contract encourages “hash‑only” flow; Safe needs payload bytes (or internal branching).
* **Effect**: Valid Safe links can be reported invalid in code paths that don’t use the reader.
* **Suggestion**: Either (a) change interface to accept **payload bytes** and handle EOA/Safe internally, or (b) keep shape but make `DefaultSignatureVerifier.VerifyAsync` automatically attempt the **bytes** variant for contracts when bytes32 fails. Document that Safe requires bytes + `eth_call.from = <safe>`.
* **Status**: Open.

---

### 6) Reader ordering relies solely on `signedAt` (**Medium**)

* **Context**: `DefaultNamespaceReader.StreamAsync` sorts per chunk by `SignedAt` desc.
* **Cause**: Timestamp‑only ordering.
* **Effect**: Manipulable ordering; ties can reorder across devices; UX feels inconsistent. Your tests demonstrate the desired behavior (append order first).
* **Suggestion**: Iterate per chunk in **append order** (newest appended last ⇒ traverse from end to start); use `signedAt` only as a secondary heuristic. Keep `newerThanUnixTs` filter.
* **Status**: Open.

---

### 7) Distinguish invalid signatures vs infrastructure errors (**Medium**)

* **Context**: In one overload, `EthereumChainApi.CallIsValidSignatureAsync` catches **all** `RpcResponseException` and maps to “reverted”.
* **Cause**: Over‑broad catch.
* **Effect**: Network/RPC issues masquerade as “invalid signature”; the reader silently drops links rather than surfacing an I/O problem.
* **Suggestion**: Treat **EVM revert** as invalid (return `Reverted=true`). **Rethrow** transport/timeouts/malformed responses so the reader **throws** on I/O errors. This matches your stated preference.
* **Status**: Open.

---

### 8) Safe ERC‑1271 handler requires `eth_call.from = <safe>` (**Medium**)

* **Context**: Safe v1.4.x `isValidSignature(bytes)` is reached via the fallback handler; some providers require `from` = the Safe.
* **Cause**: Not all code paths enforce `callFrom` for the **bytes** variant.
* **Effect**: Verifications pass/fail depending on RPC provider.
* **Suggestion**: Default to calling **bytes** with `from = <safe>` (use the overload that accepts `callFrom`); keep `bytes32` as a fallback only where appropriate.
* **Status**: Open.

---

### 9) IPFS client lacks timeouts & retries (**Medium**)

* **Context**: `IpfsStore`’s raw `HttpClient` lacks per‑request timeout/backoff.
* **Cause**: Defaults.
* **Effect**: Hangs / long stalls on slow or dead gateways; bad UX and unclear failures.
* **Suggestion**: Add a sensible request timeout (e.g., 10s), limited retries with jitter, and surface errors promptly. Consider circuit‑breaking if a gateway is flaky.
* **Status**: Open.

---

### 10) CIDv0 policy inconsistently enforced (**Medium**)

* **Context**: Reads (`CatAsync`) validate `Qm…` CIDv0; writes (`AddBytesAsync/AddJsonAsync`) don’t force CIDv0 from the IPFS daemon.
* **Cause**: No `cid-version=0` enforcement or post‑write check.
* **Effect**: If the daemon returns CIDv1 (`bafy...`), that CID could be stored into the profile; **reads will reject it**, breaking consistency.
* **Suggestion**: Enforce CIDv0 on **both** sides. Either configure the client to request v0, or validate post‑write and reject if not CIDv0. Keep `CidConverter` consistent with the policy.
* **Status**: Open.

---

### 11) Safe deployment: owner set includes deployer (**Medium**, security/ops)

* **Context**: `SafeHelper.DeploySafe141OnGnosisAsync` is used with owners `[deployer, userOwner]`, threshold = 1.
* **Cause**: Testing / convenience, but docs call it “single‑owner.”
* **Effect**: The deployer remains a co‑owner with full power (threshold=1). This is fine in test suites but **dangerous** if reused in real flows.
* **Suggestion**: Make this trade‑off explicit. For production: ensure **single owner** by default (only the user’s EOA) or raise threshold accordingly; never leave deployer as co‑owner.
* **Status**: Open.

---

### 12) Nonce replay window is process‑local & global (**Medium**)

* **Context**: `NonceRegistry` tracks the last N nonces globally.
* **Cause**: Simplicity.
* **Effect**: Restarts drop history; collisions across different signers are treated as replays; an attacker can spam unique nonces to churn the window (soft DoS).
* **Suggestion**: Track `(signer, nonce)` instead of just nonce. Optionally require monotonic `signedAt` per `(signer, logicalName)` to strengthen without coordination.
* **Status**: Open.

---

### 13) Reader silently drops invalids, which is fine; but throws should be used for I/O (**Policy confirmation)**

* **Context**: Behavior you want is “skip invalid links; **throw** on infra.”
* **Cause**: Ambiguity in current handling of RPC errors (see #7).
* **Effect**: Mixed semantics today.
* **Suggestion**: Adopt the split above and document it in the interface summary; propagate exceptions out of the reader for I/O, return only verified links.
* **Status**: Open.

---

### 14) Unit‑test mismatch: invalid EOA signature “throws” vs “returns false” (**Low**)

* **Context**: A test expects an exception on invalid EOA sig; production verifier returns `false`.
* **Cause**: Diverging expectations.
* **Effect**: Confusing test signals; unclear contract for callers.
* **Suggestion**: Normalize on **“invalid ⇒ false, programmer error ⇒ throw”**. Update tests accordingly.
* **Status**: Open.

---

### 15) Profile shape validation is permissive (**Medium**)

* **Context**: `ProfileStore.FindAsync` deserializes and returns objects without structural checks or normalization.
* **Cause**: Simple deserialization path.
* **Effect**: Bad casing in namespace keys, non‑CID values, or malformed signing keys can sneak into runtime and blow up later or be accepted by some clients and not others.
* **Suggestion**: Add a small post‑parse validator:

    * `schemaVersion` in an allow‑list (e.g., “1.1”).
    * `namespaces` keys must be lower‑case; values must be CIDv0.
    * `signingKeys` fingerprints are `0x` + 64 hex; public keys present.
      Prefer **fail‑closed** in the SDK; optionally offer a “repair + quarantine report” path at the UI/wallet layer.
* **Status**: Open.

---

### 16) Safe `v` toggle logic is eager (**Low**)

* **Context**: `SafeSignatureVerifier` flips V 27/28 ↔ 31/32 in a generic way.
* **Cause**: Compatibility.
* **Effect**: Adds extra node round‑trip on garbage inputs; could hide provider quirks.
* **Suggestion**: Only toggle after a **non‑magic, non‑revert** response; skip on transport errors.
* **Status**: Open.

---

### 17) Hard‑coded addresses / versions (**Low‑Med**)

* **Context**: `SafeHelper` (ProxyFactory, SafeSingleton, FallbackHandler), `NameRegistryConsts`, etc., are constants.
* **Cause**: Convenience for Gnosis mainline.
* **Effect**: Brittle across chain upgrades, forks, or custom deployments.
* **Suggestion**: Externalize into config/env; validate at startup (e.g., sanity check code size/signature).
* **Status**: Open.

---

### 18) Money via `double`; CT ignored in one path; comments drift (**Low**)

* **Context**:

    * `SafeHelper.FundAsync(double xDai)`: binary FP for value.
    * `EthereumChainApi.GetCodeAsync` ignores the `CancellationToken`.
    * Comments: “single‑owner” wording vs two‑owner default; `NamespaceChunk` comment references an `index` property that no longer exists.
* **Effect**: Minor correctness/maintainability nits.
* **Suggestion**:

    * Use `decimal` or `BigInteger` for value.
    * Thread CT through.
    * Update comments to match code.
* **Status**: Open.

---

### 19) CID size cap: legacy stream overload not overridden (**Low**)

* **Context**: `LimitedReadStream` caps `ReadAsync(Memory<byte>)` but not `ReadAsync(byte[],…)`.
* **Effect**: Low risk in current .NET JSON pipeline (uses `Memory<byte>`), but not watertight if future codepaths call the old overload.
* **Suggestion**: Override the legacy overload to enforce the same cap.
* **Status**: Open.

---

### 20) Data growth / retention (acknowledged meta‑note) (**Low‑Med**)

* **Context**: No GC for old chunks; history grows forever.
* **Effect**: Larger profiles and slower cold reads over time.
* **Suggestion**: Accept for now; later, add pruning/compaction tools or indexing snapshots per namespace.
* **Status**: Open.

---

## About the “previous index CID + client CAS” idea

* **Good for UX**: It **detects** stale edits early and avoids many accidental overwrites.
* **Not reliable**: It **cannot** prevent races between the read and the tx inclusion (TOCTOU). Two writers may pass the check and still clobber each other; last tx mined wins.
* **Perf impact**: Negligible (one extra `eth_call` + a comparison).
* **Practical stance**: Implement it as an **optional best‑effort** guard that throws a `ConflictException` on mismatch; let apps handle retries/merges. For correctness, plan a contract upgrade with a true **on‑chain CAS** method.

---

## Suggested prioritization

1. **Canonical JSON numbers** (blocker for cross‑runtime compatibility).
2. **ChainId sourcing** for links (correct replay domain).
3. **Reader I/O semantics** (throw on infra; skip only invalid).
4. **Writer atomicity** (pin index before profile mutation).
5. **Reader ordering** (append order primary).
6. **Verification API ergonomics** (Safe bytes path automatic).
7. **IPFS timeouts/retries**.
8. **CIDv0 policy** consistency.
9. **Safe owner policy** in defaults/tests (avoid deployer as co‑owner outside tests).
10. Tests & docs alignment.

---

## Acceptance checks per fix (condensed)

* **Canonical numbers**: same canonical bytes across .NET/JS/Node for a matrix of tricky values.
* **ChainId**: links carry the actual chain; regression tests for multi‑chain.
* **I/O semantics**: inject RPC gateway failures → reader throws; invalid signature → link skipped.
* **Atomicity**: kill IPFS during index pin → profile must **not** reference the unpinned CID.
* **Ordering**: deterministic output on identical `signedAt` based on append order.
* **Verification**: Safe links validate through the generic `ISignatureVerifier` without special‑casing by the caller.
* **CID policy**: add returns CIDv0; reader accepts; CIDv1 input rejected.

---

# 📝 Meta-notes

* **CIDv0 only:** intentional design decision.
* **Writer concurrency races:** accepted for now. Last-write-wins until locking is added.
* **Contract/EOA misclassification:** rare edge case; acceptable for now.
* **Schema evolution:** need to enforce additive-only changes, but not urgent.
* **Data retention:** no GC for old chunks, so storage grows forever. Acceptable in scope now.