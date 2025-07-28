import { SignatureCallResult } from './SignatureCallResult';

/**
 * Minimal read-only blockchain API needed by ISignatureVerifier.
 */
export interface IChainApi {
  /**
   * Gets the chain ID.
   */
  getChainId(): number;
  
  /**
   * Returns the contract code at the given address.
   * 
   * @param address The address to check
   * @returns The contract code as a 0x-prefixed hex string
   */
  getCodeAsync(address: string): Promise<string>;
  
  /**
   * Executes the ERC-1271 isValidSignature call via eth_call.
   * Never throws on "execution reverted" - that is surfaced via SignatureCallResult.reverted.
   * 
   * @param address The contract address
   * @param abi The ABI to use
   * @param dataOrHash The data or hash to verify
   * @param signature The signature
   * @returns The result of the call
   */
  callIsValidSignatureAsync(
    address: string,
    abi: any[],
    dataOrHash: Uint8Array,
    signature: Uint8Array
  ): Promise<SignatureCallResult>;
  
  /**
   * Gets the nonce of a Safe.
   * 
   * @param safeAddress The Safe address
   * @returns The nonce
   */
  getSafeNonceAsync(safeAddress: string): Promise<bigint>;
}