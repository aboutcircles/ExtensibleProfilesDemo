export interface IpfsStore {
    cat(cid: string, opts?: { signal?: AbortSignal; maxBytes?: number }): Promise<Uint8Array>;
    catString(cid: string, opts?: { signal?: AbortSignal; maxBytes?: number }): Promise<string>;
}
