import { Helpers } from './Helpers';

/**
 * Helper functions for working with CustomDataLink objects
 */
export class LinkHelpers {
  /**
   * Generates a new random nonce
   * @returns A properly formatted nonce string with 0x prefix
   */
  public static newNonce(): string {
    const bytes = Helpers.randomBytes(16);
    return '0x' + Array.from(bytes)
      .map(b => b.toString(16).padStart(2, '0'))
      .join('');
  }
}
