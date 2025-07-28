// src/utils/SafeHelpers.ts
import {
    Contract,
    ContractTransactionResponse,
    JsonRpcProvider,
    Signer,
    getAddress,
    id,
    Interface,
    ethers
} from 'ethers';
import {randomBytes} from 'crypto';
import {GnosisSafeExecutor} from '../GnosisSafeExecutor';

/* ---------- ABI pieces shared by all factory versions -------------- */
const FACTORY_ABI = [
    // functions
    'function createProxy(address _singleton, bytes initializer) returns (address)',
    'function createProxyWithNonce(address _singleton, bytes initializer, uint256 saltNonce) returns (address)',
    // events
    'event ProxyCreation(address indexed proxy, address singleton)',
    'event ProxyCreation(address indexed proxy, address singleton, bytes32 salt)'
];

const SAFE_ABI = [
    'function setup(address[] _owners, uint256 _threshold, address to, bytes data, address fallbackHandler, address paymentToken, uint256 payment, address paymentReceiver)',
    'function nonce() view returns (uint256)'
];

const NAME_REGISTRY_ABI = [
    'function updateProfileCid(string avatar, bytes32 metadataDigest)'
];

export class SafeHelpers {
    /* ---------- static addresses (Gnosis Chain, v1.3.0) ---------- */
    public static readonly PROXY_FACTORY =
        '0xa6B71E26C5e0845f74c812102Ca7114b6a896AB2';

    public static readonly SAFE_SINGLETON =
        '0x3e5c63644e683549055b9be8653de26e0b4cd36e';

    public static readonly NAME_REGISTRY_ADDRESS =
        '0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474';

    /* ------------------------------------------------------------------
     *  Safe deployment helper
     * ------------------------------------------------------------------ */
    /** Robust Safe deployment that works on all factory versions */
    public static async deploySafeAsync(
        provider: JsonRpcProvider,
        signer: Signer,
        owners: string[],
        threshold: number
    ): Promise<string> {

        const factory = new Contract(
            SafeHelpers.PROXY_FACTORY,
            FACTORY_ABI,
            signer
        );

        /* build the Safe ‘setup’ calldata */
        const safeIface = new Interface(SAFE_ABI);
        const initializer = safeIface.encodeFunctionData('setup', [
            owners,
            threshold,
            ethers.ZeroAddress,
            '0x',
            ethers.ZeroAddress,
            ethers.ZeroAddress,
            0n,
            ethers.ZeroAddress
        ]);

        /* ------------------------------------------------------------- */
        /* call factory (try modern → fallback to legacy)                */
        /* ------------------------------------------------------------- */
        let tx: ContractTransactionResponse;
        try {
            tx = await factory.createProxyWithNonce(
                SafeHelpers.SAFE_SINGLETON,
                initializer,
                // 32 byte salt
                BigInt('0x' + randomBytes(32).toString('hex')),
                {gasLimit: 3_000_000n}
            );
        } catch {
            tx = await factory.createProxy(
                SafeHelpers.SAFE_SINGLETON,
                initializer,
                {gasLimit: 3_000_000n}
            );
        }

        const receipt = await tx.wait();
        if (!receipt || receipt.status === 0) {
            throw new Error(`createProxy reverted (tx ${tx.hash})`);
        }

        /* ------------------------------------------------------------- */
        /* pull proxy address from logs                                  */
        /* ------------------------------------------------------------- */
        const sigTopic = id('ProxyCreation(address,address)');
        const raw = receipt.logs.find(l => l.topics[0] === sigTopic);

        if (raw) {
            /* --------------------------------------------------------- */
            /* Newer factories encode both addresses in the DATA part.   */
            /* Layout (ABI‑encoded):                                     */
            /*   0‑31  proxy  (left‑padded to 32 B)                      */
            /*   32‑63 singleton                                         */
            /*   64‑95 salt (v1.4.1+) ‑ optional                         */
            /* --------------------------------------------------------- */
            const bytes = ethers.getBytes(raw.data);
            if (bytes.length < 32) {
                throw new Error('ProxyCreation log too short');
            }
            return getAddress(ethers.hexlify(bytes.slice(12, 32)));
        }

        /* ------------------------------------------------------------- */
        /* fallback – let Interface try every log                        */
        /* ------------------------------------------------------------- */
        for (const log of receipt.logs) {
            try {
                const parsed = factory.interface.parseLog(log);
                if (parsed?.name === 'ProxyCreation') {
                    return getAddress(parsed.args[0] as string);
                }
            } catch {
                /* ignore logs that don't match */
            }
        }

        throw new Error('ProxyCreation event not found, although tx succeeded');
    }

    /**
     * Fires a single‑owner transaction through a Safe.
     *
     * @param provider   JSON‑RPC provider
     * @param safe       Safe address
     * @param to         Destination address
     * @param data       Calldata (hex string or bytes)
     * @param ownerPk    Owner private‑key – **either**
     *                   * 0x‑prefixed hex **string** (64 hex chars) **or**
     *                   * raw **Uint8Array** (32 bytes)
     * @param value      Ether to send (default 0)
     * @param operation  0 = CALL, 1 = DELEGATECALL (default 0)
     * @returns          Transaction hash
     */
    public static async execTransactionAsync(
        provider: JsonRpcProvider,
        safe: string,
        to: string,
        data: Uint8Array | string,
        ownerPk: Uint8Array | string,
        value: bigint = 0n,
        operation = 0
    ): Promise<string> {

        /* ------------------------------------------------------------- */
        /* normalise/validate private‑key                                */
        /* ------------------------------------------------------------- */
        let pkHex: string;

        if (typeof ownerPk === 'string') {
            pkHex = ownerPk.startsWith('0x') ? ownerPk : ('0x' + ownerPk);
            if (pkHex.length !== 66) {
                throw new Error('ownerPk string must be 32 bytes (64 hex chars)');
            }
        } else {                                        // Uint8Array
            if (ownerPk.length !== 32) {
                throw new Error('ownerPk Uint8Array must be 32 bytes');
            }
            pkHex = ethers.hexlify(ownerPk);
        }

        /* ------------------------------------------------------------- */
        /* execute                                                       */
        /* ------------------------------------------------------------- */
        const signer = new ethers.Wallet(pkHex, provider);
        const exec = new GnosisSafeExecutor(signer, safe);

        return await exec.execTransactionAsync(
            to,
            data,
            value,
            operation
        );
    }

    /* ------------------------------------------------------------------
     *  Misc utilities
     * ------------------------------------------------------------------ */

    /**
     * Encodes the calldata for **NameRegistry.updateProfileCid**.
     *
     * The helper is tolerant about parameter order – you can pass either
     *
     * ```ts
     * encodeUpdateDigest(digest, avatar);
     * // or (legacy)
     * encodeUpdateDigest(avatar, digest);
     * ```
     *
     * If the avatar is omitted or empty we still encode an empty string,
     * because the on‑chain function accepts that just fine and the current
     * test‑suite expects the helper *not* to throw in that situation.
     */
    // eslint-disable-next-line @typescript-eslint/explicit-module-boundary-types
    public static encodeUpdateDigest(
        digestOrAvatar: Uint8Array | string,
        avatarOrDigest?: Uint8Array | string
    ): string {

        /* ------------------------------------------------------------- */
        /* quick type sniffing                                           */
        /* ------------------------------------------------------------- */
        let digest32: Uint8Array | string;
        let avatar: string | Uint8Array | undefined;

        const firstLooksLikeDigest =
            (typeof digestOrAvatar === 'string' && digestOrAvatar.startsWith('0x') && digestOrAvatar.length === 66) ||
            (digestOrAvatar instanceof Uint8Array && digestOrAvatar.length === 32);

        if (firstLooksLikeDigest) {
            digest32 = digestOrAvatar;
            avatar = avatarOrDigest;
        } else {
            avatar = digestOrAvatar;
            digest32 = avatarOrDigest as Uint8Array | string;   // caller must supply!
        }

        /* ------------------------------------------------------------- */
        /* normalise avatar (may be undefined)                           */
        /* ------------------------------------------------------------- */
        const avatarStr =
            typeof avatar === 'string'
                ? avatar
                : avatar instanceof Uint8Array
                    ? new TextDecoder().decode(avatar)
                    : '';

        /* ------------------------------------------------------------- */
        /* normalise digest                                              */
        /* ------------------------------------------------------------- */
        if (digest32 === undefined) {
            throw new Error('digest32 is required');
        }

        let digestHex: string;
        if (typeof digest32 === 'string') {
            if (!digest32.startsWith('0x')) {
                throw new Error('digest32 string must be 0x‑prefixed');
            }
            if (digest32.length !== 66) {
                throw new Error('digest32 must be exactly 32 bytes (64 hex chars)');
            }
            digestHex = digest32.toLowerCase();
        } else {
            if (digest32.length !== 32) {
                throw new Error('digest32 must be exactly 32 bytes');
            }
            digestHex = ethers.hexlify(digest32);
        }

        /* ------------------------------------------------------------- */
        /* encode & return                                               */
        /* ------------------------------------------------------------- */
        const iface = new Interface(NAME_REGISTRY_ABI);
        return iface.encodeFunctionData('updateProfileCid', [avatarStr, digestHex]);
    }
}
