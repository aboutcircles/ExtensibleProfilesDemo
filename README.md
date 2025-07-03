# Circles Extensible Profile Demo

*A lean toolkit for writing signed, append‑only profile data (and messages) to IPFS and anchoring the latest profile CID on‑chain.*

---

## Core concepts

### Namespace

A **namespace** is the pair `(ownerAvatar, namespaceKey)` – imagine “Alice → Bob”. Each owner can maintain many independent logs (one per recipient, application, etc.) inside their single `Profile` object.

### Chunk

Links are appended to a **chunk** (up to `Helpers.ChunkMaxLinks` items). When full, a new chunk is opened and the previous one becomes immutable. Chunks form a singly‑linked list (newest ➜ oldest) so you can stream backwards without scanning the whole history.

### Index

Each namespace also has a tiny **index**:
`link‑name → owning‑chunk‑CID` plus a `head` pointer to the newest chunk. This gives O(1) lookup by name without copying data.

### CustomDataLink

Every payload reference is a **link**:

* `cid` – IPFS block that holds your JSON/bytes
* `name` – logical identifier (e.g. `msg‑42`)
* `signedAt` – seconds epoch
* `signature` – ECDSA secp256k1 over canonical JSON (RFC 8785)

The signature lets any client verify integrity without another web3 call.

---

## Example: using the CLI

```bash
# 1. create keys (once)
dotnet run --project ExtensibleProfilesDemo -- keygen --alias alice

# 2. create Alice's profile on‑chain
dotnet run --project ExtensibleProfilesDemo -- create \ 
  --key alice --name "Alice" --description "cool dev"

# 3. send Bob a message
dotnet run --project ExtensibleProfilesDemo -- send \ 
  --key alice --from 0xAlice --to 0xBob --type chat --text "hi bob"

# 4. read Alice's inbox (messages from trusted senders)
dotnet run --project ExtensibleProfilesDemo -- inbox \ 
  --key alice --me 0xAlice --trust 0xBob,0xCharly
```

For a complete round‑trip demo run:

```bash
dotnet run --project ExtensibleProfilesDemo -- smoke
```

which spins up three keys, pings messages around, and prints each user’s inbox.

---

MIT licensed – PRs welcome.

