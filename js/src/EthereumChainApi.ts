import {ethers} from 'ethers';
import {IChainApi} from './interfaces/IChainApi';
import {SignatureCallResult} from './interfaces/SignatureCallResult';

/**
 * Thin ethers‑v6 implementation of IChainApi.
 */
export class EthereumChainApi implements IChainApi {
    /** Ethers provider that backs all read‑only calls. */
    private readonly provider: ethers.JsonRpcProvider;

    /** Chain ID this API instance talks to (numeric, not hex‑encoded). */
    private readonly chainId: number;

    /** Expose the ID as bigint for SafeLinkSigner (legacy). */
    public readonly id: bigint;

    /**
     * Creates a new EthereumChainApi.
     *
     * @param provider  An already‑configured ethers JsonRpcProvider
     * @param chainId   Optional override (provider.getNetwork() is a second fallback)
     */
    constructor(provider: ethers.JsonRpcProvider, chainId?: number) {
        this.provider = provider;

        if (chainId !== undefined) {
            this.chainId = chainId;
        } else {
            // Synchronously grab the network ID – ok because provider has the URL.
            // eslint‑disable-next-line  @typescript-eslint/no-non-null-assertion
            const net = (provider as any)._network ?? {chainId: 0};
            this.chainId = net.chainId;
        }

        this.id = BigInt(this.chainId);
    }

    /* ---------------------------------------------------------------------- */
    /* IChainApi implementation                                              */

    /* ---------------------------------------------------------------------- */

    public getChainId(): number {
        return this.chainId;
    }

    public async getCodeAsync(address: string): Promise<string> {
        return await this.provider.getCode(address);
    }

    public async callIsValidSignatureAsync(
        address: string,
        abi: any[],
        dataOrHash: Uint8Array,
        signature: Uint8Array,
    ): Promise<SignatureCallResult> {
        const contract = new ethers.Contract(address, abi, this.provider);

        // `ethers` v6 normalises bytes to hex strings (0x‑prefixed).
        const dataHex = ethers.hexlify(dataOrHash);
        const sigHex = ethers.hexlify(signature);

        try {
            const fn = contract.getFunction('isValidSignature');
            const returnData: string = await fn(dataHex, sigHex);

            // When the call succeeds but returns empty data, ethers gives "0x".
            const bytes = returnData === '0x'
                ? new Uint8Array(0)
                : ethers.getBytes(returnData);

            return {
                reverted: false,
                returnData: bytes,
            };
        } catch (err: any) {
            const msg = (err.message ?? '').toLowerCase();
            if (msg.includes('execution reverted') || msg.includes('revert')) {
                return {
                    reverted: true,
                    returnData: new Uint8Array(0),
                };
            }
            throw err;
        }
    }

    public async getSafeNonceAsync(safeAddress: string): Promise<bigint> {
        const abi = [
            {
                inputs: [],
                name: 'nonce',
                outputs: [{name: '', type: 'uint256'}],
                stateMutability: 'view',
                type: 'function',
            },
        ];

        const contract = new ethers.Contract(safeAddress, abi, this.provider);
        const nonceHex: bigint = await contract.nonce();
        return nonceHex;
    }
}
