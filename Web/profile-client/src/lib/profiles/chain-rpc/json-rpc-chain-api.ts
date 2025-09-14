import type { ChainApi, SignatureCallResult } from "$lib/profiles/interfaces/chain-api";
import type { Hex } from "$lib/profiles/profile-reader/utils";
import { Interface, JsonRpcProvider, getBytes } from "ethers";
import { createLogger } from "$lib/log";

const ERC1271_IFACE = new Interface([
    "function isValidSignature(bytes,bytes) view returns (bytes4)",
    "function isValidSignature(bytes32,bytes) view returns (bytes4)",
]);

function isCallRevert(e: unknown): boolean {
    const msg = String((e as any)?.message ?? "");
    const code = String((e as any)?.code ?? "");
    const fromMessage = msg.toLowerCase().includes("execution reverted") || msg.toLowerCase().includes("revert");
    const fromCode = code === "CALL_EXCEPTION";
    return fromMessage || fromCode;
}

const log = createLogger("chain:rpc");

export class JsonRpcChainApi implements ChainApi {
    private readonly provider: JsonRpcProvider;
    public readonly chainId: bigint;

    constructor(rpcUrl: string, chainId: bigint) {
        const missingRpcUrl = !rpcUrl;
        if (missingRpcUrl) {
            throw new Error("rpcUrl required");
        }
        this.provider = new JsonRpcProvider(rpcUrl);
        this.chainId = chainId;
        log.info("JsonRpcChainApi constructed", { rpcUrl, chainId: String(chainId) });
    }

    async getCode(address: Hex, opts?: { signal?: AbortSignal }): Promise<Hex> {
        // ethers provider does not take AbortSignal; ignoring opts?.signal intentionally
        const start = Date.now();
        try {
            const code = await this.provider.getCode(address as string);
            const hasCode = !!code && code.length > 1;
            const out = (hasCode ? code : "0x") as Hex;
            log.debug("getCode", { address, hasCode, codeLen: code?.length ?? 0, ms: Date.now() - start });
            return out;
        } catch (e: any) {
            log.error("getCode error", { address, error: String(e?.message ?? e), ms: Date.now() - start });
            throw e;
        }
    }

    async call(to: Hex, data: Hex, from?: Hex, signal?: AbortSignal): Promise<Hex> {
        // ethers provider does not take AbortSignal; ignoring 'signal' intentionally
        const start = Date.now();
        try {
            const result = await this.provider.call({
                to: to as string,
                data: data as string,
                from: (from as string | undefined),
            });
            const hasResult = !!result;
            const out = (hasResult ? result : "0x") as Hex;
            log.debug("call", { to, from, dataLen: (data?.length ?? 0), reverted: false, ms: Date.now() - start });
            return out;
        } catch (e) {
            const reverted = isCallRevert(e);
            if (reverted) {
                log.warn("call reverted", { to, from, dataLen: (data?.length ?? 0), ms: Date.now() - start });
                return "0x" as Hex;
            }
            log.error("call error", { to, from, dataLen: (data?.length ?? 0), error: String((e as any)?.message ?? e), ms: Date.now() - start });
            throw e;
        }
    }

    async callIsValidSignatureBytes(
        contract: Hex,
        data: Uint8Array,
        signature: Uint8Array,
        opts?: { from?: Hex; signal?: AbortSignal }
    ): Promise<SignatureCallResult> {
        // ethers provider does not take AbortSignal; ignoring opts?.signal intentionally
        const calldata = ERC1271_IFACE.encodeFunctionData("isValidSignature(bytes,bytes)", [data, signature]) as Hex;
        const start = Date.now();
        try {
            const out = await this.provider.call({
                to: contract as string,
                data: calldata as string,
                from: (opts?.from as string | undefined),
            });
            const ret = getBytes(out);
            const ok = ret.length >= 4;
            log.debug("1271 bytes", { contract, from: opts?.from, dataLen: data.length, sigLen: signature.length, ok, ms: Date.now() - start });
            return { reverted: false, returnData: ret };
        } catch (e) {
            const reverted = isCallRevert(e);
            if (reverted) {
                log.warn("1271 bytes reverted", { contract, from: opts?.from, dataLen: data.length, sigLen: signature.length, ms: Date.now() - start });
                return { reverted: true, returnData: new Uint8Array() };
            }
            log.error("1271 bytes error", { contract, from: opts?.from, error: String((e as any)?.message ?? e), ms: Date.now() - start });
            throw e;
        }
    }

    async callIsValidSignatureBytes32(
        contract: Hex,
        hash32: Uint8Array,
        signature: Uint8Array,
        opts?: { from?: Hex; signal?: AbortSignal }
    ): Promise<SignatureCallResult> {
        const badHashLength = !(hash32 instanceof Uint8Array) || hash32.length !== 32;
        if (badHashLength) {
            throw new Error("hash32 must be 32 bytes");
        }
        // ethers provider does not take AbortSignal; ignoring opts?.signal intentionally
        const calldata = ERC1271_IFACE.encodeFunctionData("isValidSignature(bytes32,bytes)", [hash32, signature]) as Hex;
        const start = Date.now();
        try {
            const out = await this.provider.call({
                to: contract as string,
                data: calldata as string,
                from: (opts?.from as string | undefined),
            });
            const ret = getBytes(out);
            const ok = ret.length >= 4;
            log.debug("1271 bytes32", { contract, from: opts?.from, ok, ms: Date.now() - start });
            return { reverted: false, returnData: ret };
        } catch (e) {
            const reverted = isCallRevert(e);
            if (reverted) {
                log.warn("1271 bytes32 reverted", { contract, from: opts?.from, ms: Date.now() - start });
                return { reverted: true, returnData: new Uint8Array() };
            }
            log.error("1271 bytes32 error", { contract, from: opts?.from, error: String((e as any)?.message ?? e), ms: Date.now() - start });
            throw e;
        }
    }
}
