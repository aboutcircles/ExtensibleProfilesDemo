
import { CustomDataLink } from './CustomDataLink';

/**
 * Interface for signing links.
 */
export interface ILinkSigner {
  /**
   * Signs a link with the given private key.
   * 
   * @param link The link to sign
   * @param privKeyHex The private key (hex-encoded)
   * @returns The signed link
   */
  sign(link: CustomDataLink, privKeyHex: string): CustomDataLink;
}