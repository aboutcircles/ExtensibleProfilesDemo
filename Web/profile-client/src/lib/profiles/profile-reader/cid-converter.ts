import {base58btcEncode} from "$lib/profiles/profile-reader/utils";
export const CidConverter = {
    digestToCid(digest32: Uint8Array): string {
        if (!(digest32 instanceof Uint8Array) || digest32.length !== 32) {
            throw new Error("digest must be 32 bytes");
        }
        const mh = new Uint8Array(34);
        mh[0] = 0x12; // sha2-256 code
        mh[1] = 0x20; // 32 bytes
        mh.set(digest32, 2);
        return base58btcEncode(mh);
    },
};