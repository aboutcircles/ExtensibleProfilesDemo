export interface NameIndexDoc {
    head: string; // CIDv0 of newest chunk
    entries: Record<string, string>; // logicalName → owning chunk CIDv0
}