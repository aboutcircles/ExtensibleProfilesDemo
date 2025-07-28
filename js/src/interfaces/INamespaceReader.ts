import { CustomDataLink } from './CustomDataLink';

/**
 * Interface for reading from a namespace.
 */
export interface INamespaceReader {
  /**
   * Gets the latest link with the given logical name.
   * 
   * @param logicalName The logical name to look for
   * @returns The latest link, or null if not found
   */
  getLatestAsync(logicalName: string): Promise<CustomDataLink | null>;
  
  /**
   * Streams all links newer than the given timestamp.
   * 
   * @param newerThanUnixTs Optional timestamp to filter by
   * @returns Async iterable of links
   */
  streamAsync(newerThanUnixTs?: number): AsyncIterable<CustomDataLink>;
}
