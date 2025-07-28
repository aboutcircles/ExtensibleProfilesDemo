import { CustomDataLink } from './CustomDataLink';

/**
 * A chunk of namespace data.
 * Contains links and a reference to the previous chunk.
 */
export interface NamespaceChunk {
  /**
   * CID of the previous chunk, or null if this is the first chunk
   */
  prev: string | null;
  
  /**
   * Links contained in this chunk
   */
  links: CustomDataLink[];
}
