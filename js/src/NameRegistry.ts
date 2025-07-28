import { INameRegistry } from './interfaces/INameRegistry';
import { ethers } from 'ethers';

/**
 * Implementation of the INameRegistry interface for blockchain interaction
 */
export class NameRegistry implements INameRegistry {
  private provider: ethers.JsonRpcProvider;
  private contractAddress: string;
  private contractAbi: string[];

  /**
   * Create a new NameRegistry instance
   * @param rpcUrl - URL of the JSON-RPC endpoint
   * @param contractAddress - Address of the name registry contract
   */
  constructor(rpcUrl: string, contractAddress: string) {
    this.provider = new ethers.JsonRpcProvider(rpcUrl);
    this.contractAddress = contractAddress;
    
    // Simplified ABI for the name registry contract
    this.contractAbi = [
      'function getProfileCid(string avatar) view returns (string)',
      'function updateProfileCid(string avatar, bytes32 metadataDigest) returns (string)'
    ];
  }

  /**
   * Get profile CID for an avatar
   * @param avatar - The avatar name
   * @returns Promise that resolves to the profile CID or null if not found
   */
  public async getProfileCid(avatar: string): Promise<string | null> {
    try {
      const contract = new ethers.Contract(
        this.contractAddress, 
        this.contractAbi, 
        this.provider
      );
      
      const cid = await contract.getProfileCid(avatar);
      return cid || null;
    } catch (error) {
      console.error('Failed to get profile CID:', error);
      return null;
    }
  }

  /**
   * Update profile CID for an avatar
   * @param avatar - The avatar name
   * @param metadataDigest32 - 32-byte digest of the metadata
   * @returns Promise that resolves to the transaction hash or null on failure
   */
  public async updateProfileCid(
    avatar: string, 
    metadataDigest32: Uint8Array
  ): Promise<string | null> {
    try {
      // For browser usage, we need a wallet to sign transactions
      // This requires the user to connect their wallet
      const signer = this.provider.getSigner();
      
      const contract = new ethers.Contract(
        this.contractAddress, 
        this.contractAbi, 
        await signer
      );
      
      // Convert the digest to bytes32 format
      const digest = ethers.hexlify(metadataDigest32);
      
      // Send the transaction
      const tx = await contract.updateProfileCid(avatar, digest);
      
      // Wait for the transaction to be mined
      const receipt = await tx.wait();
      
      return receipt.hash;
    } catch (error) {
      console.error('Failed to update profile CID:', error);
      return null;
    }
  }
}
