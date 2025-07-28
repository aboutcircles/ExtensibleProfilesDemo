// src/CidConverter.ts
import { base58btc } from 'multiformats/bases/base58';

/**
 * Utility for converting between CID and digest byte arrays.
 * Handles **CID‑v0** (base58btc “Qm…”) exactly like the C# helper.
 */
export class CidConverter {
  /** multihash header for sha2‑256 → 0x12 0x20 */
  private static readonly MH_PREFIX = new Uint8Array([0x12, 0x20]);

  /* ------------------------------------------------------------------ */
  /* digest → CID‑v0                                                    */
  /* ------------------------------------------------------------------ */

  /**
   * Convert a 32‑byte digest to a CID‑v0 string (Qm…).
   */
  public static digestToCid(digest32: Uint8Array): string {
    if (!digest32) throw new Error('Digest cannot be null');
    if (digest32.length !== 32) {
      throw new Error('Digest must be exactly 32 bytes');
    }

    // 34 bytes = multihash prefix (2) + digest (32)
    const mh = new Uint8Array(34);
    mh.set(this.MH_PREFIX, 0);
    mh.set(digest32, 2);

    // baseEncode → pure base58btc **without** the leading “z”
    return base58btc.baseEncode(mh);
  }

  /* ------------------------------------------------------------------ */
  /* CID‑v0 → digest                                                    */
  /* ------------------------------------------------------------------ */

  /**
   * Convert a CID‑v0 string (Qm…) to its underlying 32‑byte digest.
   */
  public static cidToDigest(cid: string): Uint8Array {
    if (!cid) throw new Error('CID cannot be null or empty');

    let mh: Uint8Array;

    // CID‑v0 never carries the multibase prefix – tolerate both forms
    if (cid.startsWith('Qm')) {
      mh = base58btc.baseDecode(cid);
    } else if (cid.startsWith('zQm')) {
      mh = base58btc.decode(cid);          // prefixed variant (rare)
    } else {
      throw new Error('Invalid CIDv0 format: not a sha2‑256 multihash');
    }

    // validate header
    if (
        mh.length !== 34 ||
        mh[0] !== this.MH_PREFIX[0] ||
        mh[1] !== this.MH_PREFIX[1]
    ) {
      throw new Error('Invalid CIDv0 format: not a sha2‑256 multihash');
    }

    return mh.slice(2);                    // strip header → 32‑byte digest
  }
}
