/**
 * A profile-level index of namespace chunks.
 * The index is stored in the profile's "namespaces" map.
 */
export interface NameIndexDoc {
  /**
   * CID of the head chunk
   */
  head: string;
  
  /**
   * Map of logical names to chunk CIDs
   */
  entries: Record<string, string>;
}
