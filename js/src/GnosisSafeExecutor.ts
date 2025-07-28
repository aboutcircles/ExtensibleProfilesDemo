// src/GnosisSafeExecutor.ts
import { ethers, SigningKey } from 'ethers';
import { ISafeExecutor } from './interfaces/ISafeExecutor';

/**
 * Safe transaction execution helper for Gnosis Safe
 * Implements single‑signature execTransaction
 */
export class GnosisSafeExecutor implements ISafeExecutor {
    private readonly signer: ethers.Signer;
    private readonly safeAddress: string;
    private readonly safeContract: ethers.Contract;

    /* ------------------------------------------------------------------ */
    /* ABI                                                                 */
    /* ------------------------------------------------------------------ */

    private static readonly SAFE_ABI = [
        {
            type: 'function',
            name: 'nonce',
            stateMutability: 'view',
            inputs: [],
            outputs: [{type: 'uint256'}]
        },
        {
            type: 'function',
            name: 'getTransactionHash',
            inputs: [
                {type: 'address', name: 'to'},
                {type: 'uint256', name: 'value'},
                {type: 'bytes', name: 'data'},
                {type: 'uint8', name: 'operation'},
                {type: 'uint256', name: 'safeTxGas'},
                {type: 'uint256', name: 'baseGas'},
                {type: 'uint256', name: 'gasPrice'},
                {type: 'address', name: 'gasToken'},
                {type: 'address', name: 'refundReceiver'},
                {type: 'uint256', name: 'nonce'}
            ],
            outputs: [{type: 'bytes32'}],
            stateMutability: 'view'
        },
        {
            type: 'function',
            name: 'execTransaction',
            inputs: [
                {type: 'address', name: 'to'},
                {type: 'uint256', name: 'value'},
                {type: 'bytes', name: 'data'},
                {type: 'uint8', name: 'operation'},
                {type: 'uint256', name: 'safeTxGas'},
                {type: 'uint256', name: 'baseGas'},
                {type: 'uint256', name: 'gasPrice'},
                {type: 'address', name: 'gasToken'},
                {type: 'address', name: 'refundReceiver'},
                {type: 'bytes', name: 'signatures'}
            ],
            outputs: [{type: 'bool', name: 'success'}],
            stateMutability: 'nonpayable'
        }
    ];

    /**
     * @param signer      Signer (ideally an ethers.Wallet with PK)
     * @param safeAddress Address of the target Safe
     */
    constructor(signer: ethers.Signer, safeAddress: string) {
        if (!safeAddress?.trim()) throw new Error('safeAddress is required');
        if (!signer.provider) throw new Error('Signer must be connected to a provider');

        this.signer      = signer;
        this.safeAddress = ethers.getAddress(safeAddress);
        this.safeContract = new ethers.Contract(
            this.safeAddress,
            GnosisSafeExecutor.SAFE_ABI,
            this.signer
        );
    }

    /* ------------------------------------------------------------------ */
    /* public API                                                         */
    /* ------------------------------------------------------------------ */

    public async execTransactionAsync (
        to: string,
        data: Uint8Array | string,
        value: bigint = 0n,
        operation = 0,
        _ct?: AbortSignal
    ): Promise<string> {

        const dataHex = typeof data === 'string'
            ? data.startsWith('0x') ? data : `0x${data}`
            : `0x${Buffer.from(data).toString('hex')}`;

        const nonce  = await this.safeContract.nonce();
        const params = {
            to:             ethers.getAddress(to),
            value,
            data:           dataHex,
            operation,
            safeTxGas:      150_000n,
            baseGas:        0n,
            gasPrice:       0n,
            gasToken:       ethers.ZeroAddress,
            refundReceiver: ethers.ZeroAddress
        };

        const txHash   = await this.safeContract.getTransactionHash(
            params.to,
            params.value,
            params.data,
            params.operation,
            params.safeTxGas,
            params.baseGas,
            params.gasPrice,
            params.gasToken,
            params.refundReceiver,
            nonce
        );

        const signature = await this.signTransactionHashRaw(txHash);

        const tx = await this.safeContract.execTransaction(
            params.to,
            params.value,
            params.data,
            params.operation,
            params.safeTxGas,
            params.baseGas,
            params.gasPrice,
            params.gasToken,
            params.refundReceiver,
            signature,
            { gasLimit: 600_000n }
        );

        const receipt = await tx.wait();
        return receipt?.hash ?? tx.hash;
    }

    /* ------------------------------------------------------------------ */
    /* private helpers                                                    */
    /* ------------------------------------------------------------------ */

    /** Signs the raw Safe tx‑hash (no EIP‑191 prefix). */
    private async signTransactionHashRaw (txHash: string): Promise<string> {
        if (!('privateKey' in this.signer)) {
            throw new Error('Signer must be an ethers.Wallet with an unlocked private key');
        }

        const key  = new SigningKey((this.signer as ethers.Wallet).privateKey);
        const sig  = key.sign(ethers.getBytes(txHash));
        const blob = ethers.concat([
            sig.r,
            sig.s,
            new Uint8Array([sig.v])
        ]);

        return ethers.hexlify(blob);
    }

    /* ------------------------------------------------------------------ */
    /* factory helper – *correct* random wallet                           */
    /* ------------------------------------------------------------------ */

    /**
     * Convenience: returns a *new* Wallet with a cryptographically‑secure
     * random private‑key connected to `provider`.
     */
    public static randomWallet (provider: ethers.JsonRpcProvider): ethers.Wallet {
        const pkHex = ethers.hexlify(ethers.randomBytes(32));     // "0x" + 64 hex
        return new ethers.Wallet(pkHex, provider);
    }
}
