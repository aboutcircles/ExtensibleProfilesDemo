import type { CustomDataLink } from "$lib/profiles/types/custom-data-link";

export function writeCanonicalJson(value: unknown, skipSignature: boolean, out: string[]): void {
    if (value === null) { out.push("null"); return; }
    const t = typeof value;

    if (t === "string") { out.push(JSON.stringify(value)); return; }
    if (t === "number") {
        const n = value as number;
        const isInt = Number.isSafeInteger(n);
        out.push(isInt ? String(n) : JSON.stringify(n));
        return;
    }
    if (t === "boolean") { out.push(value ? "true" : "false"); return; }

    if (Array.isArray(value)) {
        out.push("[");
        for (let i = 0; i < value.length; i++) {
            if (i > 0) out.push(",");
            writeCanonicalJson(value[i], skipSignature, out);
        }
        out.push("]");
        return;
    }

    if (t === "object") {
        const obj = value as Record<string, unknown>;
        const keys = Object.keys(obj).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
        const seen = new Set<string>();
        out.push("{");
        let first = true;
        for (const k of keys) {
            // Drop signature regardless of casing to be robust
            if (skipSignature && (k === "Signature" || k === "signature")) continue;
            if (seen.has(k)) { throw new Error(`duplicate property "${k}"`); }
            seen.add(k);
            if (!first) out.push(",");
            first = false;
            out.push(JSON.stringify(k), ":");
            writeCanonicalJson(obj[k], skipSignature, out);
        }
        out.push("}");
        return;
    }

    throw new Error(`unsupported JSON type: ${t}`);
}

type CSharpLinkShape = {
    Name: string;
    Cid: string;
    Encrypted: boolean;
    EncryptionAlgorithm: string | null;
    EncryptionKeyFingerprint: string | null;
    ChainId: number;
    SignerAddress: string;
    SignedAt: number;
    Nonce: string;
    Signature: string;
};

function toCSharpLinkShape(link: CustomDataLink): CSharpLinkShape {
    return {
        Name: link.name,
        Cid: link.cid,
        Encrypted: link.encrypted,
        EncryptionAlgorithm: link.encryptionAlgorithm ?? null,
        EncryptionKeyFingerprint: link.encryptionKeyFingerprint ?? null,
        ChainId: link.chainId,
        SignerAddress: link.signerAddress,
        SignedAt: link.signedAt,
        Nonce: link.nonce,
        Signature: link.signature,
    };
}

export function canonicaliseWithoutSignature(link: CustomDataLink): Uint8Array {
    const csharpShape = toCSharpLinkShape(link);
    const chunks: string[] = [];
    writeCanonicalJson(csharpShape, true, chunks);
    const str = chunks.join("");
    return new TextEncoder().encode(str);
}