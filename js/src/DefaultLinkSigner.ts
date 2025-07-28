import { ethers, SigningKey } from 'ethers';
import { CustomDataLink } from './interfaces/CustomDataLink';
import { ILinkSigner } from './interfaces/ILinkSigner';
import { CanonicalJson } from './CanonicalJson';
import { Sha3 } from './Sha3';

/**
 * Default implementation of ILinkSigner – produces a simple EOA signature.
 */
export class DefaultLinkSigner implements ILinkSigner {
  public sign(link: CustomDataLink, privKeyHex: string): CustomDataLink {
    const wallet = new ethers.Wallet(privKeyHex);

    const linkWithSigner: CustomDataLink = {
      ...link,
      signerAddress: wallet.address
    };

    /* hash the canonical JSON (without the yet‑to‑be‑added signature) */
    const hash = Sha3.keccak256Bytes(
        CanonicalJson.canonicaliseWithoutSignature(linkWithSigner)
    );

    /* sign raw 32‑byte digest */
    const signingKey = new SigningKey(privKeyHex);
    const sig = signingKey.sign(hash);          // { r, s, v }

    const sigBytes = ethers.concat([
      sig.r,
      sig.s,
      new Uint8Array([sig.v])
    ]);

    return {
      ...linkWithSigner,
      signature: ethers.hexlify(sigBytes)
    };
  }
}
