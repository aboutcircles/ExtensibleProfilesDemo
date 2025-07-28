/**
 * Wire‑level representation of a signed namespace entry.
 * Mirrors the C# `CustomDataLink` record 1 : 1.
 */
export interface CustomDataLink {
    /** Case‑insensitive logical name */
    name: string;

    /** Payload CID (CID‑v0, Base58btc, “Qm…”) */
    cid: string;

    /** Native chain ID (e.g. 100 for Gnosis) */
    chainId: number;

    /** Unix time (seconds) when the link was signed */
    signedAt: number;

    /** 16‑byte random nonce, hex‑encoded, *with* “0x” prefix */
    nonce: string;

    /** `true` if `cid` points to encrypted bytes */
    encrypted: boolean;

    /** Address reported as the signer (Safe or EOA) */
    signerAddress?: string;

    /** 65‑byte hex – r || s || v, lower‑case, `0x`‑prefixed */
    signature?: string;

    /** Allow future extensions */
    [key: string]: unknown;
}
