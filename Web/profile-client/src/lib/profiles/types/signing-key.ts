import type {Hex} from "$lib/profiles/profile-reader/utils";

export interface SigningKey {
    publicKey: Hex;
    validFrom: number;
    validTo?: number | null;
    revokedAt?: number | null;
}