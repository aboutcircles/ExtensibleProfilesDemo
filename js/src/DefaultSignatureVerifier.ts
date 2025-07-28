import { ethers } from 'ethers';
import { ISignatureVerifier } from './interfaces/ISignatureVerifier';
import { IChainApi } from './interfaces/IChainApi';

/**
 * Default implementation of ISignatureVerifier
 * Handles both EOA signatures and ERC-1271 contract signatures
 */
export class DefaultSignatureVerifier implements ISignatureVerifier {
  // Magic return values - big-endian byte layout, exactly as Solidity returns them
  private static readonly MAGIC32_BYTES = [0x16, 0x26, 0xBA, 0x7E]; // bytes4(keccak256("isValidSignature(bytes32,bytes)"))
  private static readonly MAGIC_BYTES = [0x20, 0xC1, 0x3B, 0x0B];   // bytes4(keccak256("isValidSignature(bytes,bytes)"))

  private static readonly ABI_BYTES32 = [
    {
      inputs: [
        { type: 'bytes32' },
        { type: 'bytes' }
      ],
      name: 'isValidSignature',
      outputs: [{ type: 'bytes4' }],
      stateMutability: 'view',
      type: 'function'
    }
  ];

  private static readonly ABI_BYTES = [
    {
      inputs: [
        { type: 'bytes' },
        { type: 'bytes' }
      ],
      name: 'isValidSignature',
      outputs: [{ type: 'bytes4' }],
      stateMutability: 'view',
      type: 'function'
    }
  ];

  private readonly chain: IChainApi;

  /**
   * Creates a new DefaultSignatureVerifier
   * @param chainApi The chain API for on-chain signature verification
   */
  constructor(chainApi: IChainApi) {
    if (!chainApi) throw new Error('chainApi is required');
    this.chain = chainApi;
  }

  /**
   * Bind verifyAsync to _verify implementation to maintain the same interface
   * while allowing internal implementation changes
   */
  public verifyAsync = this._verify.bind(this);

  /**
   * Alias for verifyAsync to maintain backward compatibility with any existing code
   * that might be using this method name
   */
  public verifySignatureAsync = this._verify.bind(this);

  /**
   * Core implementation for signature verification
   * @param hash The hash that was signed
   * @param signerAddress The address of the signer
   * @param signature The signature bytes
   * @returns True if signature is valid, false otherwise
   */
  private async _verify(
    hash: Uint8Array,
    signerAddress: string,
    signature: Uint8Array
  ): Promise<boolean> {
    if (!hash || hash.length === 0) {
      throw new ArgumentNullException('hash');
    }
    if (!signature || signature.length === 0) {
      throw new ArgumentNullException('signature');
    }
    if (!signerAddress || signerAddress.trim() === '') {
      throw new ArgumentException('Empty address', 'signerAddress');
    }

    // Normalize the signer address
    const normalizedAddress = ethers.getAddress(signerAddress);

    // Check if signer is a contract
    const code = await this.chain.getCodeAsync(normalizedAddress);
    const isContract = code !== '0x' && code !== '';

    if (!isContract) {
      // EOA verification
      try {
        // Ensure signature is in the right format for recovery
        if (signature.length !== 65) {
          return false;
        }

        // Convert signature to hex for ethers.Signature.from validation
        const signatureHex = ethers.hexlify(signature);
        
        // Use ethers.Signature.from to validate and parse the signature
        const sig = ethers.Signature.from(signatureHex);
        
        // Check for high-S value (EIP-2 malleability protection)
        // Note: hasHighS was removed in ethers v6, we need to check manually
        // S value should be less than half of the curve order (secp256k1)
        const halfN = BigInt("0x7fffffffffffffffffffffffffffffff5d576e7357a4501ddfe92f46681b20a0");
        if (BigInt(sig.s) > halfN) {
          return false;
        }

        // Recover the signer
        const hashHex = ethers.hexlify(hash);
        const recoveredAddress = ethers.recoverAddress(hashHex, sig);

        return recoveredAddress.toLowerCase() === normalizedAddress.toLowerCase();
      } catch (error) {
        console.error('Error during EOA signature verification:', error);
        return false;
      }
    }

    // ERC-1271 contract verification
    // Try bytes32 variant first
    if (await this.try1271Async(
      DefaultSignatureVerifier.ABI_BYTES32,
      DefaultSignatureVerifier.MAGIC32_BYTES,
      normalizedAddress,
      hash,
      signature
    )) {
      return true;
    }

    // Try bytes variant as fallback
    return await this.try1271Async(
      DefaultSignatureVerifier.ABI_BYTES,
      DefaultSignatureVerifier.MAGIC_BYTES,
      normalizedAddress,
      hash,
      signature
    );
  }

  /**
   * Tries to verify a signature using ERC-1271
   * @param abi The ABI to use
   * @param magic The magic value to check for
   * @param contractAddress The contract address
   * @param dataOrHash The data or hash to verify
   * @param signature The signature
   * @returns True if the signature is valid according to ERC-1271
   */
  private async try1271Async(
    abi: any[],
    magic: number[],
    contractAddress: string,
    dataOrHash: Uint8Array,
    signature: Uint8Array
  ): Promise<boolean> {
    try {
      const result = await this.chain.callIsValidSignatureAsync(
        contractAddress,
        abi,
        dataOrHash,
        signature
      );

      if (result.reverted) {
        return false; // Explicit "invalid signature"
      }

      // Check if return data matches the magic value
      const returnData = result.returnData;
      if (returnData.length !== 4) {
        return false;
      }

      // Compare the return data with the expected magic value
      for (let i = 0; i < 4; i++) {
        if (returnData[i] !== magic[i]) {
          return false;
        }
      }

      return true;
    } catch (error) {
      console.error('Error during ERC-1271 verification:', error);
      return false;
    }
  }
}

/**
 * Exception thrown when an argument is null
 */
class ArgumentNullException extends Error {
  constructor(paramName: string) {
    super(`Argument '${paramName}' cannot be null or empty.`);
    this.name = 'ArgumentNullException';
  }
}

/**
 * Exception thrown when an argument is invalid
 */
class ArgumentException extends Error {
  constructor(message: string, paramName: string) {
    super(`${message} (Parameter '${paramName}')`);
    this.name = 'ArgumentException';
  }
}
