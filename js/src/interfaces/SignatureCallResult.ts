/**
 * Result of an ERC-1271 isValidSignature pre-flight.
 * - reverted: The EVM reverted (i.e. "invalid signature")
 * - returnData: Raw ABI-encoded value (empty when reverted)
 */
export interface SignatureCallResult {
  /**
   * Whether the call reverted
   */
  reverted: boolean;
  
  /**
   * The return data (if any)
   */
  returnData: Uint8Array;
}
