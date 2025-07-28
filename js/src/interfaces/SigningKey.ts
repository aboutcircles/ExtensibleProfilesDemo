/**
 * A signing key entry in a profile.
 */
export interface SigningKey {
  /**
   * Public key (hex-encoded)
   */
  publicKey: string;
  
  /**
   * Unix timestamp when the key became valid
   */
  validFrom: number;
  
  /**
   * Unix timestamp when the key expires (optional)
   */
  validUntil?: number;
}
