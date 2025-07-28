import { CustomDataLink } from './interfaces/CustomDataLink';
import { IIpfsStore } from './interfaces/IIpfsStore';
import { INamespaceReader } from './interfaces/INamespaceReader';
import { ISignatureVerifier } from './interfaces/ISignatureVerifier';
import { CanonicalJson } from './CanonicalJson';
import { Sha3 } from './Sha3';
import { Helpers } from './utils/Helpers';

/**
 * Streams a namespace with on-the-fly signature verification.
 * Links failing verification are silently skipped.
 */
export class DefaultNamespaceReader implements INamespaceReader {
  private readonly ipfs: IIpfsStore;
  private readonly verifier: ISignatureVerifier;
  private readonly headCid: string | null;
  
  /**
   * Creates a new DefaultNamespaceReader.
   * 
   * @param headCid The CID of the head chunk
   * @param ipfs The IPFS store
   * @param verifier The signature verifier
   */
  constructor(
    headCid: string | null,
    ipfs: IIpfsStore,
    verifier: ISignatureVerifier
  ) {
    this.headCid = headCid;
    this.ipfs = ipfs;
    this.verifier = verifier;
  }
  
  /**
   * Gets the latest link with the given logical name.
   * 
   * @param logicalName The logical name to look for
   * @returns The latest link, or null if not found
   */
  public async getLatestAsync(
    logicalName: string
  ): Promise<CustomDataLink | null> {
    for await (const link of this.streamAsync(0)) {
      if (link.name.toLowerCase() === logicalName.toLowerCase()) {
        return link;
      }
    }
    
    return null;
  }
  
  /**
   * Streams all links newer than the given timestamp.
   * 
   * @param newerThanUnixTs Optional timestamp to filter by
   * @returns Async iterable of links
   */
  public async* streamAsync(
    newerThanUnixTs: number = 0
  ): AsyncIterable<CustomDataLink> {
    let currentCid = this.headCid;
    
    while (currentCid) {
      const chunk = await Helpers.loadChunk(currentCid, this.ipfs);
      
      // Process links in descending order of timestamp
      // For links with the same timestamp, preserve original order
      const sortedLinks = [...chunk.links].sort((a, b) => {
        const timestampDiff = b.signedAt - a.signedAt;
        return timestampDiff !== 0 ? timestampDiff : 0; // Keep original order if timestamps match
      });
      
      for (const link of sortedLinks) {
        if (link.signedAt <= newerThanUnixTs) continue;
        
        if (await this.verify(link)) {
          yield link;
        }
      }
      
      currentCid = chunk.prev;
    }
  }
  
  /**
   * Verifies a link's signature.
   * 
   * @param link The link to verify
   * @returns True if the signature is valid, false otherwise
   */
  private async verify(link: CustomDataLink): Promise<boolean> {
    if (!link.signature || !link.signerAddress) {
      return false;
    }
    
    const hash = Sha3.keccak256Bytes(
      CanonicalJson.canonicaliseWithoutSignature(link)
    );
    
    const signature = Buffer.from(link.signature.startsWith('0x')
      ? link.signature.slice(2)
      : link.signature, 'hex');
    
    return await this.verifier.verifyAsync(
      hash,
      link.signerAddress,
      signature
    );
  }
}
