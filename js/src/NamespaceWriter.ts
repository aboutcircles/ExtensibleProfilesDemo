import { CustomDataLink } from './interfaces/CustomDataLink';
import { IIpfsStore } from './interfaces/IIpfsStore';
import { ILinkSigner } from './interfaces/ILinkSigner';
import { INamespaceWriter } from './interfaces/INamespaceWriter';
import { NameIndexDoc } from './interfaces/NameIndexDoc';
import { NamespaceChunk } from './interfaces/NamespaceChunk';
import { Profile } from './interfaces/Profile';
import { Helpers } from './utils/Helpers';

/**
 * Writes to one (ownerAvatar, namespaceKey) log.
 * If the same logicalName is written again, the newer link replaces
 * the older entry inside the head chunk.
 */
export class NamespaceWriter implements INamespaceWriter {
  private readonly ownerProfile: Profile;
  private readonly nsKeyLower: string;
  private readonly ipfs: IIpfsStore;
  private readonly signer: ILinkSigner;
  
  private index: NameIndexDoc = { head: '', entries: {} };
  private head: NamespaceChunk = { prev: null, links: [] };
  
  private constructor(
    ownerProfile: Profile,
    namespaceKey: string,
    ipfs: IIpfsStore,
    signer: ILinkSigner
  ) {
    this.ownerProfile = ownerProfile;
    this.nsKeyLower = namespaceKey.toLowerCase();
    this.ipfs = ipfs;
    this.signer = signer;
  }
  
  /**
   * Asynchronously loads the existing index/chunk state and returns a ready-to-use writer.
   * 
   * @param ownerProfile The owner's profile
   * @param namespaceKey The namespace key
   * @param ipfs The IPFS store
   * @param signer The link signer
   * @returns A namespace writer
   */
  public static async createAsync(
    ownerProfile: Profile,
    namespaceKey: string,
    ipfs: IIpfsStore,
    signer: ILinkSigner
  ): Promise<NamespaceWriter> {
    const writer = new NamespaceWriter(ownerProfile, namespaceKey, ipfs, signer);
    
    if (ownerProfile.namespaces && ownerProfile.namespaces[writer.nsKeyLower]) {
      const idxCid = ownerProfile.namespaces[writer.nsKeyLower];
      writer.index = await Helpers.loadIndex(idxCid, ipfs);
      writer.head = await Helpers.loadChunk(writer.index.head, ipfs);
    }
    
    return writer;
  }
  
  /**
   * Adds JSON content to the namespace.
   * 
   * @param name Logical name for the content
   * @param json JSON content to add
   * @param pk Private key for signing
   * @returns The created link
   */
  public async addJsonAsync(
    name: string,
    json: string,
    pk: string
  ): Promise<CustomDataLink> {
    const cid = await this.ipfs.addJsonAsync(json, true);
    return this.attachExistingCidAsync(name, cid, pk);
  }
  
  /**
   * Attaches an existing CID to the namespace.
   * 
   * @param name Logical name for the content
   * @param cid IPFS CID to attach
   * @param pk Private key for signing
   * @returns The created link
   */
  public async attachExistingCidAsync(
    name: string,
    cid: string,
    pk: string
  ): Promise<CustomDataLink> {
    if (!name || name.trim() === '') throw new Error('Name is required');
    if (!cid || cid.trim() === '') throw new Error('CID is required');
    if (!pk || pk.trim() === '') throw new Error('Private key is required');
    
    const draft: CustomDataLink = {
      name,
      cid,
      chainId: Helpers.DefaultChainId,
      signedAt: Math.floor(Date.now() / 1000),
      nonce: this.generateNonce(),
      encrypted: false
    };
    
    const signed = this.signer.sign(draft, pk);
    
    await this.persistAsync([signed]);
    return signed;
  }
  
  /**
   * Adds multiple JSON contents to the namespace.
   * 
   * @param items Array of name-json pairs
   * @param pk Private key for signing
   * @returns Array of created links
   */
  public async addJsonBatchAsync(
    items: Array<[string, string]>,
    pk: string
  ): Promise<ReadonlyArray<CustomDataLink>> {
    if (!pk || pk.trim() === '') {
      throw new Error('Private key is required');
    }
    
    const links: CustomDataLink[] = [];
    
    for (const [name, json] of items) {
      if (!name || name.trim() === '') {
        throw new Error('At least one of the items in the list doesn\'t have a name');
      }
      
      const cid = await this.ipfs.addJsonAsync(json, true);
      
      links.push({
        name,
        cid,
        chainId: Helpers.DefaultChainId,
        signedAt: Math.floor(Date.now() / 1000),
        nonce: this.generateNonce(),
        encrypted: false
      });
    }
    
    const signedLinks = links.map(link => this.signer.sign(link, pk));
    await this.persistAsync(signedLinks);
    return signedLinks;
  }
  
  /**
   * Attaches multiple existing CIDs to the namespace.
   * 
   * @param items Array of name-cid pairs
   * @param pk Private key for signing
   * @returns Array of created links
   */
  public async attachCidBatchAsync(
    items: Array<[string, string]>,
    pk: string
  ): Promise<ReadonlyArray<CustomDataLink>> {
    if (!pk || pk.trim() === '') {
      throw new Error('Private key is required');
    }
    
    const links = items.map(([name, cid]) => {
      if (!name || name.trim() === '') {
        throw new Error('Name is required');
      }
      if (!cid || cid.trim() === '') {
        throw new Error('CID is required');
      }
      
      return {
        name,
        cid,
        chainId: Helpers.DefaultChainId,
        signedAt: Math.floor(Date.now() / 1000),
        nonce: this.generateNonce(),
        encrypted: false
      };
    });
    
    const signedLinks = links.map(link => this.signer.sign(link, pk));
    await this.persistAsync(signedLinks);
    return signedLinks;
  }
  
  /**
   * Persists links to IPFS and updates the namespace.
   * 
   * @param newLinks Links to persist
   */
  private async persistAsync(newLinks: CustomDataLink[]): Promise<void> {
    for (const link of newLinks) {
      // Rotate if full
      if (this.head.links.length >= Helpers.ChunkMaxLinks) {
        const closedCid = await Helpers.saveChunk(this.head, this.ipfs);
        
        for (const l of this.head.links) {
          this.index.entries[l.name] = closedCid;
        }
        
        this.head = { prev: closedCid, links: [] };
      }
      
      // Up-sert
      const idx = this.head.links.findIndex(
        l => l.name.toLowerCase() === link.name.toLowerCase()
      );
      
      if (idx >= 0) {
        this.head.links[idx] = link;
      } else {
        this.head.links.push(link);
      }
    }
    
    // Flush
    const headCid = await Helpers.saveChunk(this.head, this.ipfs);
    
    for (const l of this.head.links) {
      this.index.entries[l.name] = headCid;
    }
    
    this.index.head = headCid;
    
    const indexCid = await Helpers.saveIndex(this.index, this.ipfs);
    
    if (!this.ownerProfile.namespaces) {
      this.ownerProfile.namespaces = {};
    }
    
    this.ownerProfile.namespaces[this.nsKeyLower] = indexCid;
  }
  
  /**
   * Generates a new random nonce.
   * 
   * @returns A random nonce string
   */
  private generateNonce(): string {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    return '0x' + Array.from(bytes)
      .map(b => b.toString(16).padStart(2, '0'))
      .join('');
  }
}
