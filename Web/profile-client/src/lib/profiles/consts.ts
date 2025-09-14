export const SECP_N = BigInt(
    "0xfffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141"
);
export const SECP_N_OVER_2 = SECP_N >> 1n;
export const DEFAULT_MAX_JSON = 8 * 1024 * 1024;
export const CIDV0_RX = /^Qm[1-9A-HJ-NP-Za-km-z]{44}$/; // 46 chars
export const ETH_ADDR_RX = /^0x[0-9a-f]{40}$/;
export const B58_ALPHABET = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";