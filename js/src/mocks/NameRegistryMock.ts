import { INameRegistry } from '../interfaces/INameRegistry';

/**
 * Mock implementation of INameRegistry for testing
 */
export class NameRegistryMock implements INameRegistry {
  private profileCids: Map<string, string> = new Map();
  
  /**
   * Create a mock with a predefined profile CID for an avatar
   * @param avatar - Avatar name
   * @param cid - Profile CID
   * @returns NameRegistryMock instance
   */
  public static withProfileCid(avatar: string, cid: string): NameRegistryMock {
    const mock = new NameRegistryMock();
    mock.profileCids.set(avatar, cid);
    return mock;
  }
  
  /**
   * Get profile CID for an avatar
   * @param avatar - The avatar name
   * @returns Promise that resolves to the profile CID or null if not found
   */
  public async getProfileCid(avatar: string): Promise<string | null> {
    return this.profileCids.get(avatar) || null;
  }
  
  /**
   * Update profile CID for an avatar
   * @param avatar - The avatar name
   * @param metadataDigest32 - 32-byte digest of the metadata
   * @returns Promise that resolves to the transaction hash "TX-MOCK"
   */
  public async updateProfileCid(
    avatar: string, 
    metadataDigest32: Uint8Array
  ): Promise<string | null> {
    // In the mock implementation, we don't need to use the digest
    // Just return a mock transaction hash
    return "TX-MOCK";
  }
}
