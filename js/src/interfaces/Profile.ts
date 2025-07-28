import { SigningKey } from './SigningKey';

/**
 * A user profile.
 */
export interface Profile {
  /**
   * Display name
   */
  name?: string;
  
  /**
   * Profile description
   */
  description?: string;
  
  /**
   * Avatar image CID
   */
  avatar?: string;
  
  /**
   * Map of signing key fingerprints to signing keys
   */
  signingKeys?: Record<string, SigningKey>;
  
  /**
   * Map of namespace keys to namespace index CIDs
   */
  namespaces?: Record<string, string>;
}
