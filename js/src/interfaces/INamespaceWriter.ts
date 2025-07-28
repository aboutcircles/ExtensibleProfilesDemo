import { CustomDataLink } from './CustomDataLink';

/**
 * Interface for writing to a namespace.
 */
export interface INamespaceWriter {
  /**
   * Adds JSON content to the namespace.
   * 
   * @param name Logical name for the content
   * @param json JSON content to add
   * @param pk Private key for signing
   * @returns The created link
   */
  addJsonAsync(name: string, json: string, pk: string): Promise<CustomDataLink>;
  
  /**
   * Attaches an existing CID to the namespace.
   * 
   * @param name Logical name for the content
   * @param cid IPFS CID to attach
   * @param pk Private key for signing
   * @returns The created link
   */
  attachExistingCidAsync(name: string, cid: string, pk: string): Promise<CustomDataLink>;
  
  /**
   * Adds multiple JSON contents to the namespace.
   * 
   * @param items Array of name-json pairs
   * @param pk Private key for signing
   * @returns Array of created links
   */
  addJsonBatchAsync(items: Array<[string, string]>, pk: string): Promise<ReadonlyArray<CustomDataLink>>;
  
  /**
   * Attaches multiple existing CIDs to the namespace.
   * 
   * @param items Array of name-cid pairs
   * @param pk Private key for signing
   * @returns Array of created links
   */
  attachCidBatchAsync(items: Array<[string, string]>, pk: string): Promise<ReadonlyArray<CustomDataLink>>;
}
