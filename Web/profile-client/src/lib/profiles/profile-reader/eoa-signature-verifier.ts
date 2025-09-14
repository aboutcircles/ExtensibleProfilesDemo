// src/lib/profiles/profile-reader/eoa-signature-verifier.ts
import { SigningKey, computeAddress, hexlify } from "ethers";
import { keccak_256 } from "@noble/hashes/sha3";
import { SECP_N_OVER_2 } from "$lib/profiles/consts";
import { bytesToBigInt } from "$lib/profiles/profile-reader/utils";
import type { Hex } from "$lib/profiles/profile-reader/utils";

/**
 * Verifies EOA ECDSA signatures over a 32-byte Keccak hash.
 * Enforces EIP-2 (low-S). No chain calls required.
 */
export class EoaSignatureVerifier {
    /**
     * Synchronous verification over a 32-byte hash.
     */
    verify(hash32: Uint8Array, signerAddress: Hex | string, signature65: Uint8Array): boolean {
        if (!(hash32 instanceof Uint8Array)) {
            throw new Error("hash must be a Uint8Array");
        }
        const is32 = hash32.length === 32;
        if (!is32) {
            throw new Error(`hash must be 32 bytes, got ${hash32.length}`);
        }

        if (!(signature65 instanceof Uint8Array)) {
            throw new Error("signature must be a Uint8Array");
        }
        const is65 = signature65.length === 65;
        if (!is65) {
            throw new Error(`signature must be 65 bytes, got ${signature65.length}`);
        }

        const addrEmpty = !signerAddress || String(signerAddress).trim().length === 0;
        if (addrEmpty) {
            throw new Error("Empty address");
        }

        // EIP-2: reject non-canonical S (and s != 0)
        const sBytes = signature65.subarray(32, 64);
        const sBI = bytesToBigInt(sBytes);
        const sIsZero = sBI === 0n;
        const sIsHigh = sBI > SECP_N_OVER_2;
        const sCanonical = !sIsZero && !sIsHigh;
        if (!sCanonical) {
            return false;
        }

        // Normalize v to {27,28}; accept {0,1} too (no other variants).
        const vRaw = signature65[64];
        const v = (vRaw === 27 || vRaw === 28) ? vRaw
            : (vRaw === 0 || vRaw === 1) ? vRaw + 27
                : -1;
        const vValid = v === 27 || v === 28;
        if (!vValid) {
            return false;
        }

        const r = hexlify(signature65.subarray(0, 32));
        const s = hexlify(signature65.subarray(32, 64));

        try {
            const pub = SigningKey.recoverPublicKey(hash32, { r, s, v });
            const recovered = computeAddress(pub);
            return recovered.toLowerCase() === String(signerAddress).toLowerCase();
        } catch {
            return false;
        }
    }

    /**
     * Convenience for readers that only have the canonical payload bytes.
     * EOAs sign the keccak of those bytes.
     */
    verifyOverBytes(payloadBytes: Uint8Array, signerAddress: Hex | string, signature65: Uint8Array): boolean {
        if (!(payloadBytes instanceof Uint8Array)) {
            throw new Error("payloadBytes must be a Uint8Array");
        }
        const h = keccak_256(payloadBytes);
        return this.verify(h, signerAddress, signature65);
    }

    /* ─────────────────── async aliases to fit existing call sites ─────────────────── */

    async verifyAsync(
        hash32: Uint8Array,
        signerAddress: Hex | string,
        signature65: Uint8Array,
        _signal: AbortSignal | undefined = undefined
    ): Promise<boolean> {
        return this.verify(hash32, signerAddress, signature65);
    }

    async verify1271WithBytesAsync(
        payloadBytes: Uint8Array,
        signerAddress: Hex | string,
        signature65: Uint8Array,
        _signal: AbortSignal | undefined = undefined
    ): Promise<boolean> {
        return this.verifyOverBytes(payloadBytes, signerAddress, signature65);
    }
}
