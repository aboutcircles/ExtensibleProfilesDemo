/**
 * Interface for interacting with IPFS.
 */
export interface IIpfsStore {
  /**
   * Adds JSON content to IPFS.
   * 
   * @param json JSON content to add
   * @param pin Whether to pin the content
   * @returns The CID of the added content
   */
  addJsonAsync(json: string, pin?: boolean): Promise<string>;
  
  /**
   * Adds binary data to IPFS.
   * 
   * @param bytes Binary data to add
   * @param pin Whether to pin the content
   * @returns The CID of the added content
   */
  addBytesAsync(bytes: Uint8Array, pin?: boolean): Promise<string>;
  
  /**
   * Adds content to IPFS (generic method)
   * @param content The content to add
   * @returns The CID of the added content
   */
  addAsync(content: Uint8Array | string): Promise<string>;
  
  /**
   * Retrieves content from IPFS as a byte array
   * @param cid The CID to retrieve
   * @returns The content as a byte array
   */
  getAsync(cid: string): Promise<Uint8Array>;
  
  /**
   * Pins content in IPFS to ensure persistence
   * @param cid The CID to pin
   */
  pinAsync(cid: string): Promise<void>;
  
  /**
   * Retrieves content from IPFS.
   * 
   * @param cid The CID to retrieve
   * @returns A stream of the content
   */
  catAsync(cid: string): Promise<ReadableStream<Uint8Array>>;
  
  /**
   * Retrieves content from IPFS as a string.
   * 
   * @param cid The CID to retrieve
   * @returns The content as a string
   */
  catStringAsync(cid: string): Promise<string>;
  
  /**
   * Calculates the CID of the given data.
   * 
   * @param bytes The data to calculate the CID for
   * @returns The calculated CID
   */
  calcCidAsync(bytes: Uint8Array): Promise<string>;
  
  /**
   * Disposes of resources.
   */
  dispose(): void;
}