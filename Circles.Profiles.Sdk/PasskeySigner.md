# Task: Implement a browser npm package `@org/safe-webauthn-signer`

## Goal

Expose a small TypeScript API that:

1. Builds the Safe EIP-712 **SafeMessage** hash from raw message bytes.
2. Starts a **WebAuthn assertion** using that hash as the challenge.
3. Turns the WebAuthn assertion into the ABI-encoded **`WebAuthn.Signature`** struct expected by
   `SafeWebAuthnSharedSigner`.
4. Wraps it into a Safe **contract signature envelope** (`v=0`, `r=validatorAddress`, `s=offset`) suitable for
   `isValidSignature(bytes,bytes)` and for Safe tx submission.
5. Optionally verifies against the Safe via ERC-1271 (read-only) for quick checks.

This package does **not** perform credential registration or Safe configuration. It assumes the Safe already stores a
WebAuthn signer.

---

## Deliverables

* Published-ready npm package (ESM) with TypeScript types.
* Minimal dependency surface, pinned versions, reproducible build.
* Unit tests for byte-level helpers and struct encoding.
* Example usage in the README.
* Clean code, no dead code, no swallowed exceptions.

---

## Public API

All functions are **browser-only**.

```ts
import type {Address, Hex} from 'viem';

export type CanonicalBytes = Uint8Array;

export type WebAuthnAssertionParts = {
    authenticatorData: Uint8Array;
    clientDataJSON: string;
    derSignature: Uint8Array; // ES256 DER
};

export type BuildSafeHashArgs = {
    chainId: number;
    safeAddress: Address;
    canonicalPayload: CanonicalBytes;
};

export declare function buildSafeHash(args: BuildSafeHashArgs): Uint8Array;

export type StartAssertionArgs = {
    safeHash: Uint8Array;
    publicKeyOverrides?: Partial<PublicKeyCredentialRequestOptions>;
};

export declare function startWebAuthnAssertion(
    args: StartAssertionArgs
): Promise<WebAuthnAssertionParts>;

export type EncodeWebAuthnStructArgs = {
    authenticatorData: Uint8Array;
    clientDataJSON: string;
    challengeBase64Url: string;
    derSignature: Uint8Array;
};

export declare function encodeWebAuthnStruct(
    args: EncodeWebAuthnStructArgs
): Uint8Array;

export declare function encodeSafeContractSignature(
    validatorAddress: Address,
    webauthnStructBytes: Uint8Array
): Uint8Array;

export type BuildSignatureArgs = {
    chainId: number;
    safeAddress: Address;
    validatorAddress: Address;
    canonicalPayload: CanonicalBytes;
    publicKeyOverrides?: Partial<PublicKeyCredentialRequestOptions>;
};

export declare function buildWebAuthnSafeSignature(
    args: BuildSignatureArgs
): Promise<Uint8Array>;

export type Verify1271Args = {
    rpcUrl: string;
    safeAddress: Address;
    messageBytes: CanonicalBytes;
    safeSignature: Uint8Array;
};

export declare function verify1271Bytes(
    args: Verify1271Args
): Promise<Hex>; // returns magic value (expect 0x20c13b0b)
```

---

## Non-goals

* No Node.js/WebAuthn polyfills. This is strictly browser.
* No multi-owner signature packing. This package builds **one** contract signature at a time.
* No Safe transaction submission flows.

---

## Package layout

```
safe-webauthn-signer/
  ├─ src/
  │   ├─ index.ts
  │   ├─ safeHash.ts
  │   ├─ webauthn.ts
  │   ├─ webauthnStruct.ts
  │   ├─ safeSignature.ts
  │   ├─ verify1271.ts
  │   ├─ errors.ts
  │   └─ utils.ts
  ├─ test/
  │   ├─ clientDataFields.test.ts
  │   ├─ safeSignatureEncoding.test.ts
  │   └─ safeHash.test.ts
  ├─ README.md
  ├─ package.json
  ├─ tsconfig.json
  ├─ tsup.config.ts
  ├─ LICENSE
  └─ .editorconfig
```

---

## Dependencies

Pin exact versions at implementation time (no carets). The agent should resolve latest stable versions and **lock**
them.

* `viem` – EIP-712 hashing + ABI encoding + RPC read.
* `@simplewebauthn/browser` – browser WebAuthn.
* `@noble/curves` – parse ES256 DER → `(r,s)` for P-256.
* `jose` – base64url encode/decode without padding.
* Dev: `typescript`, `tsup`, `vitest` (or `uvu`), `@types/*` as needed.

---

## Coding standards

* TypeScript strict mode on.
* Always use braces.
* Hoist complex conditions into named variables.
* Throw typed errors; no logging-only catches.
* No dead code.

---

## Implementation steps

1. **Project setup**

    * Initialize `package.json` with `"type": "module"`, `"sideEffects": false`, `"exports"` pointing to
      `./dist/index.js` with `"types"`.
    * Add `tsconfig.json` (ES2020 target, `"module": "ESNext"`, `"moduleResolution": "Bundler"`, `"strict": true`,
      `"lib": ["ES2020", "DOM"]`).
    * Add `tsup.config.ts` to build ESM only, sourcemaps on.
    * Add `scripts`: `build`, `test`, `lint` (if you add eslint), `prepare` (build on install).

2. **Errors**

    * Create error classes:

        * `ChallengeMismatchError`
        * `ClientDataFormatError`
        * `DerSignatureParseError`
        * `InvalidAddressError`

3. **Safe hash**

    * `buildSafeHash` uses `viem.hashTypedData` with:

        * `domain = { chainId, verifyingContract: safeAddress }`
        * `types = { SafeMessage: [{ name: 'message', type: 'bytes' }] }`
        * `message = { message: 0x… }`
    * Return `Uint8Array` (32 bytes).

```ts
// src/safeHash.ts
import {hashTypedData, type Address, type Hex, hexToBytes} from 'viem';

export type BuildSafeHashArgs = {
    chainId: number;
    safeAddress: Address;
    canonicalPayload: Uint8Array;
};

export function buildSafeHash({chainId, safeAddress, canonicalPayload}: BuildSafeHashArgs): Uint8Array {
    const messageHex: Hex = (`0x${Buffer.from(canonicalPayload).toString('hex')}`) as Hex;

    const safeHashHex = hashTypedData({
        domain: {chainId, verifyingContract: safeAddress},
        types: {SafeMessage: [{name: 'message', type: 'bytes'}]} as const,
        primaryType: 'SafeMessage',
        message: {message: messageHex},
    });

    return hexToBytes(safeHashHex);
}
```

4. **WebAuthn flow**

    * `startWebAuthnAssertion`:

        * Base64url-encode the 32-byte `safeHash` (no padding).
        * Call `@simplewebauthn/browser` `startAuthentication({ publicKey })`.
        * Extract `authenticatorData`, `clientDataJSON`, `signature` (DER).
        * Return normalized `WebAuthnAssertionParts`.

```ts
// src/webauthn.ts
import {startAuthentication} from '@simplewebauthn/browser';
import {base64url} from 'jose';

export type StartAssertionArgs = {
    safeHash: Uint8Array;
    publicKeyOverrides?: Partial<PublicKeyCredentialRequestOptions>;
};

export async function startWebAuthnAssertion(
    {safeHash, publicKeyOverrides}: StartAssertionArgs
): Promise<{
    authenticatorData: Uint8Array;
    clientDataJSON: string;
    derSignature: Uint8Array;
}> {
    const challenge = base64url.encode(safeHash);

    const assertion = await startAuthentication({
        publicKey: {
            challenge,
            userVerification: 'required',
            ...publicKeyOverrides,
        },
    });

    const authData = new Uint8Array(assertion.response.authenticatorData);
    const clientDataJSON = new TextDecoder().decode(assertion.response.clientDataJSON);
    const derSignature = new Uint8Array(assertion.response.signature);

    return {authenticatorData: authData, clientDataJSON, derSignature};
}
```

5. **`clientDataFields` extraction and DER parsing**

    * Implement `sliceClientDataFields(clientDataJSON, challengeB64u)` with strict prefix:

        * Prefix must be: `{"type":"webauthn.get","challenge":"<challenge>"`
        * If next char is `,`, capture substring up to final `}`.
        * If next char is `}`, `clientDataFields = ""`.
        * Throw `ClientDataFormatError` on any mismatch.
    * Parse DER ES256 using `@noble/curves/p256` → `Signature.fromDER(der)`; turn `(r,s)` into `bigint`.

```ts
// src/webauthnStruct.ts
import {p256} from '@noble/curves/p256';
import {encodeAbiParameters} from 'viem';
import {ClientDataFormatError, ChallengeMismatchError, DerSignatureParseError} from './errors.js';

function sliceClientDataFields(clientDataJSON: string, challengeB64u: string): string {
    const prefix = `{"type":"webauthn.get","challenge":"${challengeB64u}"`;
    const hasPrefix = clientDataJSON.startsWith(prefix);
    if (!hasPrefix) {
        throw new ClientDataFormatError('clientDataJSON does not start with expected prefix');
    }

    const after = prefix.length;
    const nextChar = clientDataJSON[after];
    const isClosed = nextChar === '}';
    const hasComma = nextChar === ',';

    if (isClosed) {
        return '';
    }

    if (!hasComma) {
        throw new ClientDataFormatError('Unexpected character after challenge');
    }

    const start = after + 1;
    const end = clientDataJSON.lastIndexOf('}');
    const hasClosing = end > start;
    if (!hasClosing) {
        throw new ClientDataFormatError('Missing closing brace');
    }

    return clientDataJSON.slice(start, end);
}

export type EncodeWebAuthnStructArgs = {
    authenticatorData: Uint8Array;
    clientDataJSON: string;
    challengeBase64Url: string;
    derSignature: Uint8Array;
};

export function encodeWebAuthnStruct(args: EncodeWebAuthnStructArgs): Uint8Array {
    const fields = sliceClientDataFields(args.clientDataJSON, args.challengeBase64Url);

    let sig;
    try {
        sig = p256.Signature.fromDER(args.derSignature);
    } catch {
        throw new DerSignatureParseError('Invalid DER signature');
    }

    const r = BigInt(sig.r);
    const s = BigInt(sig.s);

    const encoded = encodeAbiParameters(
        [
            {type: 'bytes'},
            {type: 'string'},
            {type: 'uint256'},
            {type: 'uint256'},
        ],
        [args.authenticatorData, fields, r, s],
    );

    // encoded is Hex; convert to Uint8Array for uniformity
    const hex = encoded.slice(2);
    const buf = new Uint8Array(hex.length / 2);
    for (let i = 0; i < buf.length; i++) {
        const j = i * 2;
        buf[i] = parseInt(hex.slice(j, j + 2), 16);
    }
    return buf;
}
```

6. **Safe contract signature envelope (single signer)**

    * Head (65 bytes):

        * `r` = 32-byte left-padded validator address.
        * `s` = 32-byte big-endian integer `65` (offset to dynamic tail).
        * `v` = `0x00`.
    * Tail:

        * 32-byte length, then ABI-encoded `WebAuthn.Signature` bytes.

```ts
// src/safeSignature.ts
import {pad, type Address, concat, type Hex} from 'viem';
import {InvalidAddressError} from './errors.js';

function assertAddress(addr: string): asserts addr is Address {
    const looksHex = addr.startsWith('0x') && addr.length === 42;
    if (!looksHex) {
        throw new InvalidAddressError('Invalid address');
    }
}

export function encodeSafeContractSignature(validatorAddress: Address, webauthnStructBytes: Uint8Array): Uint8Array {
    assertAddress(validatorAddress);

    const r = pad(validatorAddress, {size: 32});
    const s = pad('0x41', {size: 32}); // 65 decimal -> 0x41
    const v = new Uint8Array([0x00]);

    const lenHex = `0x${webauthnStructBytes.length.toString(16)}` as Hex;
    const len = pad(lenHex, {size: 32});
    const payloadHex = (`0x${Buffer.from(webauthnStructBytes).toString('hex')}`) as Hex;

    const tail = concat([len, payloadHex]);
    const head = concat([r, s, ('0x00' as Hex)]);
    const full = concat([head, tail]);

    // concat returns Hex; convert to Uint8Array
    const hex = full.slice(2);
    const buf = new Uint8Array(hex.length / 2);
    for (let i = 0; i < buf.length; i++) {
        const j = i * 2;
        buf[i] = parseInt(hex.slice(j, j + 2), 16);
    }
    return buf;
}
```

7. **Composition helper**

    * `buildWebAuthnSafeSignature` wires steps 3–6 together.

```ts
// src/index.ts (part)
export async function buildWebAuthnSafeSignature(args: BuildSignatureArgs): Promise<Uint8Array> {
    const safeHash = buildSafeHash({
        chainId: args.chainId,
        safeAddress: args.safeAddress,
        canonicalPayload: args.canonicalPayload,
    });

    const {authenticatorData, clientDataJSON, derSignature} = await startWebAuthnAssertion({
        safeHash,
        publicKeyOverrides: args.publicKeyOverrides,
    });

    const challengeBase64Url = (await import('jose')).base64url.encode(safeHash);

    const webauthnStruct = encodeWebAuthnStruct({
        authenticatorData,
        clientDataJSON,
        challengeBase64Url,
        derSignature,
    });

    const safeSig = encodeSafeContractSignature(args.validatorAddress, webauthnStruct);
    return safeSig;
}
```

8. **Optional: ERC-1271 read helper**

```ts
// src/verify1271.ts
import {createPublicClient, http, type Address, type Hex} from 'viem';

const ERC1271_ABI = [
    {
        type: 'function',
        name: 'isValidSignature',
        stateMutability: 'view',
        inputs: [
            {name: '_data', type: 'bytes'},
            {name: '_signature', type: 'bytes'},
        ],
        outputs: [{name: 'magicValue', type: 'bytes4'}],
    },
] as const;

export async function verify1271Bytes(args: {
    rpcUrl: string;
    safeAddress: Address;
    messageBytes: Uint8Array;
    safeSignature: Uint8Array;
}): Promise<Hex> {
    const client = createPublicClient({transport: http(args.rpcUrl)});

    const dataHex = `0x${Buffer.from(args.messageBytes).toString('hex')}` as Hex;
    const sigHex = `0x${Buffer.from(args.safeSignature).toString('hex')}` as Hex;

    const magic = await client.readContract({
        abi: ERC1271_ABI,
        address: args.safeAddress,
        functionName: 'isValidSignature',
        args: [dataHex, sigHex],
        account: args.safeAddress, // forces eth_call.from=<safe>
    });

    return magic;
}
```

9. **Exports**

```ts
// src/index.ts
export * from './safeHash.js';
export * from './webauthn.js';
export * from './webauthnStruct.js';
export * from './safeSignature.js';
export * from './verify1271.js';
export * from './errors.js';
```

10. **README (usage)**

* Show a minimal snippet:

```ts
import {
    buildWebAuthnSafeSignature,
    verify1271Bytes,
    buildSafeHash,
} from '@org/safe-webauthn-signer';

const safeSignature = await buildWebAuthnSafeSignature({
    chainId,
    safeAddress,
    validatorAddress, // SafeWebAuthnSharedSigner
    canonicalPayload, // Uint8Array of your message bytes
});

// Optional: check it on-chain
const magic = await verify1271Bytes({
    rpcUrl,
    safeAddress,
    messageBytes: canonicalPayload,
    safeSignature,
});
// expect '0x20c13b0b'
```

* Document **requirements**:

    * Safe uses `SafeWebAuthnSharedSigner` with a configured WebAuthn public key.
    * `navigator.credentials.get` available.
    * Pass your RP overrides via `publicKeyOverrides` (e.g., `rpId`, `allowCredentials`).

11. **Tests**

* `clientDataFields.test.ts`:

    * Cases: remainder empty (`}` right after challenge), remainder present (`,` then JSON), wrong prefix → throws,
      missing `}` → throws.

* `safeSignatureEncoding.test.ts`:

    * Given a fake validator address and a known payload (e.g., 3 bytes), check:

        * First 32 bytes equals left-padded address.
        * `s` equals `0x41` padded to 32 bytes.
        * Tail length equals payload length.
        * Total equals `65 + 32 + payloadLen`.

* `safeHash.test.ts`:

    * Build hash on a fixed input and assert hex length = 66 (0x + 64), and bytes length = 32. (Use snapshot for
      deterministic inputs.)

12. **Build & publish**

* `npm run build` should create `dist/`.
* Ensure `"exports"` includes `"./package.json"` and
  `".": { "types": "./dist/index.d.ts", "default": "./dist/index.js" }`.
* License MIT.

---

## Acceptance criteria

* The package builds to ESM and ships types.
* `buildWebAuthnSafeSignature` produces a bytes blob that the Safe accepts as a **contract signature** when the Safe is
  correctly configured with `SafeWebAuthnSharedSigner` and the user authenticates with the matching WebAuthn credential.
* `verify1271Bytes` returns `0x20c13b0b` for a valid signature, using `eth_call.from = <safe>`.
* Unit tests pass; no unhandled promise rejections; no dead code.

---

## Edge cases you must handle

* `clientDataJSON` doesn’t start with the exact prefix → throw `ClientDataFormatError`.
* Challenge inside `clientDataJSON` doesn’t match our base64url of `safeHash` → throw `ChallengeMismatchError`.
* DER signature fails to parse → throw `DerSignatureParseError`.
* Any address not `0x`-prefixed or not length 42 → throw `InvalidAddressError`.

---

## What’s omitted (brief)

* Multi-owner signature collation & sorting in the Safe signatures blob.
* Registration/configuration path for `SafeWebAuthnSharedSigner` (DELEGATECALL authoring).
* Browser UX flows (credential picker UI, error banners, retries).
