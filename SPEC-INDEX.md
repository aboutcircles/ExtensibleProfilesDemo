# Circles Profiles

Imagine every Ethereum address has a small, public notebook. Not a social profile in the “tweety” sense—more like a place where you can pin signed notes that anyone can verify from scratch. That’s **Circles Profiles**. One profile document per address; a few shelves inside it (called **namespaces**); and on those shelves, little signed **links** pointing to content on IPFS. Readers don’t need to trust your server, your API, or your cache. They fetch, verify, and decide.

The system is simple on purpose. There’s a tiny on-chain registry that holds a 32-byte digest of your current profile (so people can always find the latest one). The real payloads live on IPFS. Your signed links are the glue between “this address said something” and “here’s the content that was meant.” Because the registry only points to the profile’s digest, you can rotate, batch updates, and keep the on-chain footprint minimal.

Think about how you’d actually use it. You (an EOA or a Safe) want to publish something another party can trust: a preference blob for a dApp, a message to a friend, a receipt, an attestation—whatever JSON you like. You push that JSON to IPFS, get a CID, wrap it in a **link** with a `name`, `cid`, `chainId`, `signedAt`, `nonce`, and `signerAddress`, then sign the canonical bytes (the link minus the signature, normalized in a deterministic way). You drop that link into the right shelf inside your profile and publish the updated profile atomically. From then on, anyone can resolve your profile, walk to that shelf, and verify the link from first principles. No surprises, no magical server.

A quick picture: Alice has a profile. Inside, there’s a shelf labeled with Bob’s address; that’s where Alice keeps things meant for Bob. When Alice posts “msg-42,” it’s just a signed pointer to an IPFS CID. Bob (or anyone) resolves Alice’s profile via the registry, finds the Bob shelf, fetches the chunk(s), and checks the signature. If the signer is an EOA, we do standard ECDSA recovery with **low-S**; if it’s a Safe, we call **ERC-1271 (bytes)** and insist the call is made **from** the Safe address (a quirk that matters). Either way, the link stands or falls on cryptography, not on trust.

This scales to operators too. A dApp (EOA or Safe) can publish pre-signed links as proposals; your wallet validates the operator’s keys (or Safe signature) and either accepts the whole package atomically—dropping it into your `(you, operator)` shelf unchanged—or rejects it. That’s the **Proposer Pattern**. It keeps the “write path” honest and auditable while letting apps suggest stuff for you to store.

And once lots of addresses are publishing under an operator shelf, you’ll want to read across many of them in a deterministic order. That’s where the **Aggregator** comes in: it reads with the same strict rules, records where each link was found (chunk + index), sorts with a stable total order (by time, then by array index within a chunk, then by address and hash), and de-dups mirrors. Feeds, dashboards, catalogs—same engine.

If you’re still with me, you already have the shape of it: one profile per address, address-keyed shelves inside, signed links on those shelves, content on IPFS, and a small on-chain pointer to the latest profile. That’s the whole trick. The rest is tidy engineering: canonical JSON, chunk rotation, atomic publishes, and strict read/verify rules. When you need the formal stuff, the base spec is here: **[Circles Profiles Protocol](./protocol-spec.md)**. Start there when you’re ready to implement.

---

## How it actually clicks together

### Core pieces (you’ll touch these first)

* **Profile**: one JSON per address, stored on IPFS. The on-chain **registry** stores its 32-byte digest (`getMetadataDigest`, `updateMetadataDigest`). **Profile/index/chunk MUST be CIDv0.** Payloads **should** be CIDv0 too. Max object size anywhere: **8 MiB**. See [CPP §2–§4](./protocol-spec.md#2-identifiers--encodings).
* **Namespace (shelf)**: `(ownerAvatar, namespaceKey)`. The key is **always a lowercase address**; writers must not produce non-address keys; readers ignore them. See [CPP §7](./protocol-spec.md#7-namespaces--write-semantics).
* **Link (sticky-note)**: `{ name, cid, chainId, signerAddress, signedAt, nonce, signature }`. Sign the **canonical JSON bytes without `signature`**. EOA: low-S; `v ∈ {27,28}`. Safe: ERC-1271 **bytes** path with **`eth_call.from = signerAddress`**, allow one V-toggle retry. See [CPP §5–§6](./protocol-spec.md#5-canonicalisation-hash-preimage).

### Writing (publisher)

* Append links to the **head chunk**, rotating at **100**.
* On publish, **pin** head → update & **pin** index → **rebase** the latest profile → update `namespaces[...]` → **pin** profile → call registry. **All-or-nothing**; fail if any pin or precondition fails. See [CPP §7–§7.2](./protocol-spec.md#72-atomic-commit-and-multi-namespace-batching-normative).

### Reading (consumer)

* Resolve profile via registry → fetch profile → read `namespaces[key]` → walk `head → prev → …`.
* Verify **in order**: signature → chainId → tuple-scoped replay on `(owner, namespaceKey, signerAddress)`.
* Present **newest-first** by `signedAt`, tie-break by higher array index within a chunk. Don’t early-stop by time alone. See [CPP §8](./protocol-spec.md#8-reading--verification).

### App-to-wallet flow (Proposer Pattern)

* Operators send **proposal packages** (one or many pre-signed links) with optional constraints.
* Wallets validate operator keys (`signingKeys` in operator’s profile) or Safe via ERC-1271, enforce constraints, check replays, then accept **atomically** (unchanged links) or reject. See **[Proposer Pattern](./proposer-pattern.md)**.

### Reading across many addresses (Aggregator)

* Inputs: `operator`, `avatars[]`, `chainId`, `{ start, end }`.
* Verifies like CPP, records provenance (`chunkCid`, `indexInChunk`), sorts with a **stable total order**, and de-dups mirrors by the canonical-bytes keccak. See **[Aggregator](./aggregator-pattern.md)**.

### Tiny e-commerce on top (optional)

* One link per product: `product/<id>` under `(seller, operator)`; payload is `product.v1` (URIs for images/stock, price number).
* Reduce Aggregator output by type/name, exclude tombstones, keep winners by the stable order. See **[Product Catalog](./product-catalog.md)**.

---

## Guardrails you probably want on day one

* Lowercase addresses everywhere; non-address keys are invalid to write and ignored on read.
* **CIDv0** for profile/index/chunk; payloads **should** be CIDv0.
* Enforce **low-S** for EOA; use ERC-1271 **bytes** with `from = signer` for Safes; one V-toggle retry is allowed.
* **8 MiB** maximum per fetched object (profile, index, chunk, payload).
* Replay scope is **only** `(owner, namespaceKey, signerAddress)` by `nonce`.
* Writes are **atomic** and **must pin** all new CIDs.
* Tolerate small future skew (≈ ≤ 30 s) for `signedAt`. See [CPP §6, §7, §8, §11–§12](./protocol-spec.md#6-signing--verification).

---

## Where to read the strict version

* **Base protocol (CPP):** data shapes, signing, rotation, atomicity, read rules — [protocol-spec.md](./protocol-spec.md)
* **Proposer Pattern:** proposal packages, key publication, acceptance — [proposer-pattern.md](./proposer-pattern.md)
* **Aggregator:** deterministic reads at scale — [aggregator-pattern.md](./aggregator-pattern.md)
* **Product Catalog:** one-link-per-product on top — [product-catalog.md](./product-catalog.md)