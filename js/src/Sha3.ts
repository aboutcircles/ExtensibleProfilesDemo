import { keccak256 } from 'js-sha3';

/**
 * Utility class for SHA3 (Keccak256) hash operations
 * Mimics the C# Sha3 class functionality
 */
export class Sha3 {
  /**
   * Computes the Keccak-256 hash of the input as a string
   * @param input Input data as string
   * @returns Hex string of the hash
   */
  public static keccak256(input: string): string {
    return '0x' + keccak256(input);
  }

  /**
   * Computes the Keccak-256 hash of the input as a Uint8Array
   * @param input Input data as Uint8Array or string
   * @returns Uint8Array containing the hash
   */
  public static keccak256Bytes(input: Uint8Array | string): Uint8Array {
    if (typeof input === 'string') {
      return new Uint8Array(keccak256.arrayBuffer(input));
    }
    return new Uint8Array(keccak256.arrayBuffer(input));
  }

  /**
   * Computes the Keccak-256 hash of the input data
   * @param data Input data
   * @returns Hex string of the hash
   */
  public static hash(data: string | Uint8Array): string {
    if (typeof data === 'string') {
      return '0x' + keccak256(data);
    }
    return '0x' + keccak256(data);
  }
}