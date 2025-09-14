import {B58_ALPHABET, ETH_ADDR_RX} from "$lib/profiles/consts";

export type Hex = `0x${string}`;

export function ensureLowerAddress(addr: string): Hex {
    const a = addr.toLowerCase();
    const ok = ETH_ADDR_RX.test(a);
    if (!ok) {
        throw new Error(`invalid address: ${addr}`);
    }
    return a as Hex;
}

export function strip0x(h: string): string {
    const has = h.startsWith("0x") || h.startsWith("0X");
    return has ? h.slice(2) : h;
}

export function hexToBytes(h: Hex | string): Uint8Array {
    const s = strip0x(h);
    if (s.length % 2 !== 0) {
        throw new Error(`hex length must be even: ${h}`);
    }
    const out = new Uint8Array(s.length / 2);
    for (let i = 0; i < out.length; i++) {
        out[i] = parseInt(s.slice(i * 2, i * 2 + 2), 16);
    }
    return out;
}

export async function safeText(res: Response): Promise<string> {
    try {
        return await res.text();
    } catch {
        return "<no-body>";
    }
}

export function concatBytes(...arrays: Uint8Array[]): Uint8Array {
    const len = arrays.reduce((acc, a) => acc + a.length, 0);
    const out = new Uint8Array(len);
    let off = 0;
    for (const a of arrays) {
        out.set(a, off);
        off += a.length;
    }
    return out;
}

export function base58btcEncode(bytes: Uint8Array): string {
    if (bytes.length === 0) {
        return "";
    }

    // count leading zeros
    let zeros = 0;
    while (zeros < bytes.length && bytes[zeros] === 0) {
        zeros++;
    }

    // work on a copy we can mutate
    const input = bytes.slice();
    const out: number[] = [];

    // division loop: base256 → base58
    let start = zeros;
    while (start < input.length) {
        let carry = 0;
        for (let i = start; i < input.length; i++) {
            const x = (carry << 8) | input[i];
            input[i] = (x / 58) | 0;
            carry = x % 58;
        }
        out.push(carry);

        // skip new leading zeros produced by division
        while (start < input.length && input[start] === 0) {
            start++;
        }
    }

    // leading zeros are represented as '1'
    let str = "";
    for (let i = 0; i < zeros; i++) str += "1";
    for (let i = out.length - 1; i >= 0; i--) str += B58_ALPHABET[out[i]];
    return str;
}

export function bytesToBigInt(b: Uint8Array): bigint {
    let x = 0n;
    for (let i = 0; i < b.length; i++) {
        x = (x << 8n) | BigInt(b[i]);
    }
    return x;
}