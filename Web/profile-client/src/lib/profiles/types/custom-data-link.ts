import type {Hex} from "$lib/profiles/profile-reader/utils";

export interface CustomDataLink {
    name: string;
    cid: string; // payload CID
    encrypted: boolean;
    encryptionAlgorithm?: string | null;
    encryptionKeyFingerprint?: string | null;

    chainId: number;
    signerAddress: Hex; // EOA or Safe
    signedAt: number;   // unix secs
    nonce: Hex;         // 16 random bytes as 0x + 32 hex
    signature: Hex;     // 65B r||s||v as 0x + 130 hex
}
