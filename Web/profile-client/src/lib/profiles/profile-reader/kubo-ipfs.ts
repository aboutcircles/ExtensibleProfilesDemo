import {concatBytes, safeText} from "$lib/profiles/profile-reader/utils";
import type {IpfsStore} from "$lib/profiles/interfaces/ipfs-store";
import {CIDV0_RX, DEFAULT_MAX_JSON} from "$lib/profiles/consts";
import { createLogger } from "$lib/log";

const log = createLogger("ipfs:kubo");

export class KuboIpfs implements IpfsStore {
    private readonly base: string;
    private readonly max: number;

    private static readonly cidV0 = CIDV0_RX;

    constructor(opts?: { apiBase?: string; maxBytes?: number }) {
        this.base = (opts?.apiBase ?? "http://127.0.0.1:5001") + "/api/v0/";
        this.max = opts?.maxBytes ?? DEFAULT_MAX_JSON;
        log.info("KuboIpfs constructed", { base: this.base, maxBytes: this.max });
    }

    async cat(cid: string, opts?: { signal?: AbortSignal; maxBytes?: number }): Promise<Uint8Array> {
        if (!KuboIpfs.cidV0.test(cid)) {
            throw new Error(`CID must be CIDv0 (Qm… 46 chars): ${cid}`);
        }
        const max = opts?.maxBytes ?? this.max;
        const started = Date.now();
        const res = await fetch(this.base + "cat?arg=" + encodeURIComponent(cid), {
            method: "POST",
            signal: opts?.signal,
        });
        log.debug("ipfs.cat response", { cid, status: res.status, ms: Date.now() - started });
        if (!res.ok) {
            throw new Error(`ipfs cat failed (${res.status}): ${await safeText(res)}`);
        }
        const len = res.headers.get("Content-Length");
        const declared = len ? Number(len) : undefined;
        const exceeds = typeof declared === "number" && declared > max;
        if (exceeds) {
            throw new Error(
                `IPFS response advertises ${declared} bytes – exceeds hard cap of ${max} bytes`
            );
        }
        const reader = res.body?.getReader();
        if (!reader) {
            // Fallback
            const buf = new Uint8Array(await res.arrayBuffer());
            if (buf.length > max) {
                throw new Error(`IPFS response exceeds hard cap of ${max} bytes`);
            }
            log.info("ipfs.cat ok (no-stream)", { cid, bytes: buf.length });
            return buf;
        }
        const chunks: Uint8Array[] = [];
        let received = 0;
        while (true) {
            const {done, value} = await reader.read();
            if (done) {
                break;
            }
            const chunk = value ?? new Uint8Array();
            received += chunk.length;
            if (received > max) {
                reader.cancel().catch(() => {
                });
                throw new Error(`IPFS response exceeds hard cap of ${max} bytes`);
            }
            chunks.push(chunk);
        }
        const all = concatBytes(...chunks);
        log.info("ipfs.cat ok (stream)", { cid, bytes: all.length, chunks: chunks.length });
        return all;
    }

    async catString(cid: string, opts?: { signal?: AbortSignal; maxBytes?: number }): Promise<string> {
        const bytes = await this.cat(cid, opts);
        return new TextDecoder("utf-8", {fatal: true}).decode(bytes);
        // fatal: true will throw on invalid UTF-8; do not swallow
    }
}