// src/lib/profiles/verification/default-signature-verifier.ts
import type { ChainApi } from "$lib/profiles/interfaces/chain-api";
import { canonicaliseWithoutSignature } from "$lib/profiles/profile-reader/canonical-json";
import { hexToBytes } from "$lib/profiles/profile-reader/utils";
import { EoaSignatureVerifier } from "$lib/profiles/profile-reader/eoa-signature-verifier";
import { createLogger } from "$lib/log";
import type { CustomDataLink } from "$lib/profiles/types/custom-data-link";

export interface VerifyReport {
    ok: boolean;
    path: "eoa" | "none";
    detail?: string;
}

const log = createLogger("verify:default");

export async function verifyLinkDefault(
    link: CustomDataLink,
    _chain: ChainApi, // kept for call-site compatibility; unused
    _opts?: { signal?: AbortSignal }
): Promise<VerifyReport> {
    const payloadBytes = canonicaliseWithoutSignature(link);
    const sigBytes = hexToBytes(link.signature);
    const signer = link.signerAddress;

    log.info("EOA verify start", { signer, hasSig: sigBytes.length === 65 });

    try {
        const eoa = new EoaSignatureVerifier();
        const ok = eoa.verifyOverBytes(payloadBytes, signer, sigBytes);
        if (ok) {
            log.info("EOA verify ok", { path: "eoa", signer });
            return { ok: true, path: "eoa" };
        }
        log.info("EOA verify failed", { signer });
        return { ok: false, path: "none" };
    } catch (e: any) {
        const detail = String(e?.message ?? e);
        log.error("EOA verify error", { signer, error: detail });
        return { ok: false, path: "none", detail };
    }
}
