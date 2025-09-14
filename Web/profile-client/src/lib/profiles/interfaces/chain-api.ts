import type {Hex} from "$lib/profiles/profile-reader/utils";

export interface SignatureCallResult {
    reverted: boolean;
    returnData: Uint8Array; // empty on revert
}

export interface ChainApi {
    readonly chainId: bigint;
    getCode(address: Hex, opts?: { signal?: AbortSignal }): Promise<Hex>;
    callIsValidSignatureBytes(
        contract: Hex,
        data: Uint8Array,
        signature: Uint8Array,
        opts?: { from?: Hex; signal?: AbortSignal }
    ): Promise<SignatureCallResult>;
    callIsValidSignatureBytes32(
        contract: Hex,
        hash32: Uint8Array,
        signature: Uint8Array,
        opts?: { from?: Hex; signal?: AbortSignal }
    ): Promise<SignatureCallResult>;
    call(to: Hex, data: Hex, from?: Hex, signal?: AbortSignal): Promise<Hex>;
}