import { create } from 'kubo-rpc-client'                // ← no “type …” import
import { IIpfsStore }   from './interfaces/IIpfsStore'

/**
 * HTTP‑based IPFS helper with RFC‑compatible limits.
 * Uses the modern **kubo‑rpc‑client** package (successor of ipfs‑http‑client).
 */
export class IpfsStore implements IIpfsStore {
    /** concrete client type = ReturnType<typeof create> */
    private readonly client: ReturnType<typeof create>

    private static readonly MAX_SIZE   = 8 * 1024 * 1024          // 8 MiB
    private static readonly CID_V0_RX = /^Qm[1-9A-HJ-NP-Za-km-z]{44}$/

    constructor (apiUrl: string = 'http://localhost:5001') {
        this.client = create({ url: apiUrl })               // ← same API surface
    }

    /* ------------------------------------------------------------------ */
    /* add                                                                */
    /* ------------------------------------------------------------------ */

    public async addJsonAsync (json: string, pin = true): Promise<string> {
        const { cid } = await this.client.add(json, { pin })
        return cid.toString()                               // kubo returns CID obj
    }

    public async addBytesAsync (bytes: Uint8Array, pin = true): Promise<string> {
        const { cid } = await this.client.add(bytes, { pin })
        return cid.toString()
    }

    public async addAsync (content: Uint8Array | string): Promise<string> {
        return typeof content === 'string'
            ? this.addJsonAsync(content, true)
            : this.addBytesAsync(content, true)
    }

    /* ------------------------------------------------------------------ */
    /* read / stream                                                      */
    /* ------------------------------------------------------------------ */

    public async getAsync (cid: string): Promise<Uint8Array> {
        this.validateCid(cid)

        const chunks: Uint8Array[] = []
        let size = 0

        for await (const chunk of this.client.cat(cid)) {
            size += chunk.length
            if (size > IpfsStore.MAX_SIZE) throw new Error('IPFS read exceeds 8 MiB limit')
            chunks.push(chunk)
        }
        return this.concat(chunks)
    }

    public async catAsync (cid: string): Promise<ReadableStream<Uint8Array>> {
        this.validateCid(cid)
        const asyncIt = this.client.cat(cid)[Symbol.asyncIterator]()
        let size = 0

        return new ReadableStream<Uint8Array>({
            async pull (controller) {
                const { value, done } = await asyncIt.next()
                if (done) { controller.close(); return }

                size += value.length
                if (size > IpfsStore.MAX_SIZE) {
                    controller.error(new Error('IPFS read exceeds 8 MiB limit'))
                    return
                }
                controller.enqueue(value)
            }
        })
    }

    public async catStringAsync (cid: string): Promise<string> {
        return new TextDecoder().decode(await this.getAsync(cid))
    }

    /* ------------------------------------------------------------------ */
    /* misc                                                               */
    /* ------------------------------------------------------------------ */

    public async calcCidAsync (bytes: Uint8Array): Promise<string> {
        const { cid } = await this.client.add(bytes, { onlyHash: true, pin: false })
        return cid.toString()
    }

    public async pinAsync (cid: string): Promise<void> {
        this.validateCid(cid)
        await this.client.pin.add(cid)
    }

    public dispose (): void {
        /* nothing to tear down for kubo‑rpc‑client */
    }

    /* ------------------------------------------------------------------ */
    /* helpers                                                            */
    /* ------------------------------------------------------------------ */

    private validateCid (cid: string): void {
        if (!IpfsStore.CID_V0_RX.test(cid)) throw new Error(`Invalid CIDv0 format: ${cid}`)
    }

    private concat (parts: Uint8Array[]): Uint8Array {
        const len = parts.reduce((a, b) => a + b.length, 0)
        const out = new Uint8Array(len)
        let off = 0
        for (const p of parts) { out.set(p, off); off += p.length }
        return out
    }
}
