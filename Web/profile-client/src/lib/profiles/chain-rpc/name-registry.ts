import type { ChainApi } from "$lib/profiles/interfaces/chain-api";
import type { Hex } from "$lib/profiles/profile-reader/utils";
import { Interface, getBytes } from "ethers";
import { CidConverter } from "$lib/profiles/profile-reader/cid-converter";
import { createLogger } from "$lib/log";

// Lowercase to satisfy your ETH_ADDR_RX
const log = createLogger("chain:registry");

export const NAME_REGISTRY_ADDRESS: Hex = "0xa27566fd89162cc3d40cb59c87aaaa49b85f3474";

const REGISTRY_IFACE = new Interface([
    "function getMetadataDigest(address) view returns (bytes32)"
]);

function isAllZero(b: Uint8Array): boolean {
    for (let i = 0; i < b.length; i++) {
        const nonZero = b[i] !== 0;
        if (nonZero) {
            return false;
        }
    }
    return true;
}

export async function getMetadataDigest32(
    chain: ChainApi,
    avatar: Hex,
    opts?: { signal?: AbortSignal }
): Promise<Uint8Array | null> {
    const calldata = REGISTRY_IFACE.encodeFunctionData("getMetadataDigest", [avatar]) as Hex;
    log.debug("getMetadataDigest32 call", { avatar, calldataLen: calldata.length });

    const out = await chain.call(NAME_REGISTRY_ADDRESS, calldata, undefined, opts?.signal);
    const reverted = out === "0x";
    if (reverted) {
        log.warn("getMetadataDigest32 reverted", { avatar });
        return null;
    }

    try {
        const [digestHex] = REGISTRY_IFACE.decodeFunctionResult("getMetadataDigest", out) as readonly string[];
        const digest = getBytes(digestHex);
        const wrongSize = digest.length !== 32;
        if (wrongSize) {
            throw new Error(`getMetadataDigest returned ${digest.length} bytes (want 32)`);
        }
        const zero = isAllZero(digest);
        if (zero) {
            log.info("getMetadataDigest32 zero digest", { avatar });
            return null;
        }
        log.info("getMetadataDigest32 ok", { avatar });
        return digest;
    } catch (e: any) {
        const raw = getBytes(out);
        const tooShort = raw.length < 32;
        if (tooShort) {
            const msg = `getMetadataDigest returned ${raw.length} bytes (want 32)`;
            log.error("getMetadataDigest32 short", { avatar, error: msg });
            throw new Error(msg);
        }
        const digest = raw.slice(raw.length - 32);
        const zero = isAllZero(digest);
        if (zero) {
            log.info("getMetadataDigest32 zero digest(fallback)", { avatar });
            return null;
        }
        log.info("getMetadataDigest32 ok(fallback)", { avatar });
        return digest;
    }
}

export async function getProfileCid(
    chain: ChainApi,
    avatar: Hex,
    opts?: { signal?: AbortSignal }
): Promise<string | null> {
    const digest = await getMetadataDigest32(chain, avatar, opts);
    const missing = digest === null;
    if (missing) {
        log.info("getProfileCid: none", { avatar });
        return null;
    }
    const cid = CidConverter.digestToCid(digest);
    log.info("getProfileCid: ok", { avatar, cid });
    return cid;
}
