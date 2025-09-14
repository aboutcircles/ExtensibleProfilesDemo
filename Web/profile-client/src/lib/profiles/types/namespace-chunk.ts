import type {CustomDataLink} from "$lib/profiles/types/custom-data-link";

export interface NamespaceChunk {
    prev?: string | null; // older chunk CID
    links: CustomDataLink[];
}