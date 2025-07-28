import { IIpfsStore } from '../interfaces/IIpfsStore';
import { NameIndexDoc } from '../interfaces/NameIndexDoc';
import { NamespaceChunk } from '../interfaces/NamespaceChunk';

/**
 * Low‑level helpers shared by Namespace* classes.
 */
export class Helpers {
    public static readonly ChunkMaxLinks  = 100;
    public static readonly DefaultChainId = 100;        // Gnosis Chain (0x64)

    /* ------------------------------------------------------------------ */
    /* random                                                             */
    /* ------------------------------------------------------------------ */

    public static randomBytes(len: number): Uint8Array {
        if (len <= 0) throw new Error('length must be positive');
        const buf = new Uint8Array(len);
        crypto.getRandomValues(buf);
        return buf;
    }

    /* ------------------------------------------------------------------ */
    /* IPFS helpers                                                       */
    /* ------------------------------------------------------------------ */

    public static async loadChunk(
        cid: string | null,
        ipfs: IIpfsStore
    ): Promise<NamespaceChunk> {
        if (!cid || cid.trim() === '') {
            return { prev: null, links: [] };
        }

        const raw = await ipfs.catStringAsync(cid);
        const chunk = JSON.parse(raw) as NamespaceChunk;

        if (!chunk || !Array.isArray(chunk.links)) {
            throw new Error(`Invalid NamespaceChunk JSON in ${cid}`);
        }
        return chunk;
    }

    public static async loadIndex(
        cid: string | null,
        ipfs: IIpfsStore
    ): Promise<NameIndexDoc> {
        if (!cid || cid.trim() === '') {
            throw new Error('CID is missing');
        }

        try {
            const raw = await ipfs.catStringAsync(cid);
            return JSON.parse(raw) as NameIndexDoc;
        } catch {
            return { head: '', entries: {} };
        }
    }

    public static async saveChunk(
        chunk: NamespaceChunk,
        ipfs: IIpfsStore
    ): Promise<string> {
        return ipfs.addJsonAsync(JSON.stringify(chunk), true);
    }

    public static async saveIndex(
        idx: NameIndexDoc,
        ipfs: IIpfsStore
    ): Promise<string> {
        return ipfs.addJsonAsync(JSON.stringify(idx), true);
    }
}
