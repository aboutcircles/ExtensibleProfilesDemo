import { IProfileStore } from './interfaces/IProfileStore';
import { INameRegistry } from './interfaces/INameRegistry';
import { IIpfsStore } from './interfaces/IIpfsStore';
import { Sha3 } from './Sha3';
import { CanonicalJson } from './CanonicalJson';

/**
 * Implementation of IProfileStore using INameRegistry and IIpfsStore
 */
export class ProfileStore implements IProfileStore {
  private nameRegistry: INameRegistry;
  private ipfsStore: IIpfsStore;
  
  /**
   * Create a new ProfileStore instance
   * @param nameRegistry - Implementation of INameRegistry
   * @param ipfsStore - Implementation of IIpfsStore
   */
  constructor(nameRegistry: INameRegistry, ipfsStore: IIpfsStore) {
    this.nameRegistry = nameRegistry;
    this.ipfsStore = ipfsStore;
  }
  
  /**
   * Get a profile by avatar name
   * @param avatar - Avatar name
   * @returns Promise resolving to the profile data as a JSON object or null if not found
   */
  public async getProfileAsync(avatar: string): Promise<Record<string, any> | null> {
    try {
      // Get the profile CID from the name registry
      const cid = await this.nameRegistry.getProfileCid(avatar);
      
      if (!cid) {
        return null;
      }
      
      // Download the profile data from IPFS
      const profileData = await this.ipfsStore.getAsync(cid);
      
      // Parse the JSON data
      const text = new TextDecoder().decode(profileData);
      return JSON.parse(text);
    } catch (error) {
      console.error('Failed to get profile:', error);
      return null;
    }
  }
  
  /**
   * Update a profile for an avatar
   * @param avatar - Avatar name
   * @param profile - Profile data as a JSON object
   * @returns Promise resolving to true if update was successful, false otherwise
   */
  public async updateProfileAsync(
    avatar: string, 
    profile: Record<string, any>
  ): Promise<boolean> {
    try {
      // Convert the profile to canonical JSON
      const canonicalJson = CanonicalJson.stringify(profile);
      
      // Convert the JSON to a byte array
      const profileBytes = new TextEncoder().encode(canonicalJson);
      
      // Calculate the digest
      const digest = Sha3.keccak256Bytes(profileBytes);
      
      // Upload the profile to IPFS
      await this.ipfsStore.addAsync(profileBytes);
      
      // Update the profile CID in the name registry
      const txHash = await this.nameRegistry.updateProfileCid(avatar, digest);
      
      return !!txHash;
    } catch (error) {
      console.error('Failed to update profile:', error);
      return false;
    }
  }
}
