import type {SigningKey} from "$lib/profiles/profile-reader/signing-key";

export interface Profile {
    schemaVersion: string; // e.g. "1.1"
    previewImageUrl?: string | null;
    imageUrl?: string | null;
    name: string;
    description: string;
    namespaces: Record<string, string>; // key: lowercase address, value: CIDv0 head-of-index
    signingKeys: Record<string, SigningKey>; // key: fingerprint 0x + 64 hex
}