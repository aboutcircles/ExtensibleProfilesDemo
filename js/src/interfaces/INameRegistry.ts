/**
 * On-chain name-registry (avatar → profile CID).
 */
export interface INameRegistry {
  /**
   * Get profile CID for an avatar
   * @param avatar - The avatar name
   * @returns Promise that resolves to the profile CID or null if not found
   */
  getProfileCid(avatar: string): Promise<string | null>;
  
  /**
   * Update profile CID for an avatar
   * @param avatar - The avatar name
   * @param metadataDigest32 - 32-byte digest of the metadata
   * @returns Promise that resolves to the updated profile CID or null on failure
   */
  updateProfileCid(avatar: string, metadataDigest32: Uint8Array): Promise<string | null>;
}
