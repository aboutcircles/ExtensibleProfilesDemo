import { keccak256 } from 'js-sha3';
import { ethers, SigningKey } from 'ethers';
import { ILinkSigner } from './interfaces/ILinkSigner';
import { CustomDataLink } from './interfaces/CustomDataLink';
import { IChainApi } from './interfaces/IChainApi';
import { CanonicalJson } from './CanonicalJson';

/**
 * Produces an ECDSA signature with `signerAddress == SafeAddress`
 * while still using the *owner* EOA key for the cryptographic proof.
 * The resulting link passes on‑chain ERC‑1271 checks for the Safe.
 */
export class SafeLinkSigner implements ILinkSigner {
  private readonly safe: string;
  private readonly chain: IChainApi;
  private static readonly safeMsgTypeHash: Uint8Array = 
    Buffer.from(keccak256.create().update('SafeMessage(bytes)').digest());

  /**
   * Creates a new instance of SafeLinkSigner
   * @param safeAddress The address of the Safe wallet
   * @param chain The chain API
   */
  constructor(safeAddress: string, chain: IChainApi) {
    if (!safeAddress || safeAddress.trim() === '') {
      throw new Error('safeAddress is required');
    }

    this.safe = safeAddress;
    this.chain = chain;
  }

  /**
   * Signs a link with the provided owner private key
   * @param link The link to sign
   * @param ownerPrivKeyHex The owner's private key in hex
   * @returns The signed link
   */
  public sign(link: CustomDataLink, ownerPrivKeyHex: string): CustomDataLink {
    // Create a wallet from the owner private key
    const ownerWallet = new ethers.Wallet(ownerPrivKeyHex);
    
    // Update link with safe address as signer
    const updatedLink = {
      ...link,
      signerAddress: this.safe
    };

    // Calculate hashes
    const payloadHash = Buffer.from(
      keccak256.create().update(
        CanonicalJson.canonicaliseWithoutSignature(updatedLink)
      ).digest()
    );

    const safeTxHash = Buffer.from(
      keccak256.create().update(
        Buffer.concat([SafeLinkSigner.safeMsgTypeHash, payloadHash])
      ).digest()
    );

    const chainId = BigInt(this.chain.getChainId());
    const domainSeparator = this.buildDomainSeparator(chainId, this.safe);

    const safeHash = Buffer.from(
      keccak256.create().update(
        Buffer.concat([
          Buffer.from([0x19, 0x01]),
          domainSeparator,
          safeTxHash
        ])
      ).digest()
    );

    // Sign the hash
    const messageHashBytes = ethers.getBytes(safeHash);
    
    // Use SigningKey directly for better compatibility
    const signingKey = new SigningKey(ownerPrivKeyHex);
    const signature = signingKey.sign(messageHashBytes);
    
    // Create the 65-byte signature (r + s + v)
    const r = signature.r.substring(2); // remove 0x
    const s = signature.s.substring(2); // remove 0x
    const v = signature.v.toString(16).padStart(2, '0');
    
    const signatureHex = `0x${r}${s}${v}`;

    // Return the signed link
    return {
      ...updatedLink,
      signature: signatureHex
    };
  }

  /**
   * Builds the domain separator for EIP-712
   * @param chainId The chain ID
   * @param safeAddress The safe address
   * @returns The domain separator as a byte array
   */
  private buildDomainSeparator(chainId: bigint, safeAddress: string): Uint8Array {
    const typeHash = Buffer.from(
      keccak256.create().update('EIP712Domain(uint256 chainId,address verifyingContract)').digest()
    );

    // Encode parameters according to Solidity ABI encoding
    const abiCoder = ethers.AbiCoder.defaultAbiCoder();
    const encoded = abiCoder.encode(
      ['bytes32', 'uint256', 'address'],
      [typeHash, chainId, safeAddress]
    );

    return Buffer.from(keccak256.create().update(Buffer.from(encoded.substring(2), 'hex')).digest());
  }
}
