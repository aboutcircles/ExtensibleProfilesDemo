/**
 * Interface for signature verification
 * Matches the C# ISignatureVerifier interface
 */
export interface ISignatureVerifier {
  /**
   * Verifies a signature.
   * 
   * @param hash The hash that was signed
   * @param signerAddress The address of the signer
   * @param signature The signature bytes
   * @returns Promise resolving to true if signature is valid, false otherwise
   */
  verifyAsync(
    hash: Uint8Array, 
    signerAddress: string, 
    signature: Uint8Array
  ): Promise<boolean>;
}