/* ------------------------------------------------------------------ */
/* test/RealSafeEndToEnd.test.ts                                      */
/* ------------------------------------------------------------------ */

import { beforeAll, describe, expect, it } from 'vitest';
import { ethers } from 'ethers';

import { IpfsStore } from '../src/IpfsStore';
import { CidConverter } from '../src/CidConverter';
import { CanonicalJson } from '../src/CanonicalJson';
import { NamespaceWriter } from '../src/NamespaceWriter';
import { DefaultSignatureVerifier } from '../src/DefaultSignatureVerifier';
import { EthereumChainApi } from '../src/EthereumChainApi';
import { SafeLinkSigner } from '../src/SafeLinkSigner';
import { SafeHelpers } from '../src/utils/SafeHelpers';
import { Helpers } from '../src/utils/Helpers';
import { Profile } from '../src/interfaces/Profile';

/* ------------------------------------------------------------------ */
/* basic setup                                                        */
/* ------------------------------------------------------------------ */

const RPC   = 'https://rpc.aboutcircles.com';
const provider = new ethers.JsonRpcProvider(RPC);

const BOOT_PK =
    process.env.BOOTSTRAP_PK ??
    '0x6c810a7f2ef411dcd76ec70b6d7f90d07a29c0646bc4e978b45297eefcf5eab8';

if (!/^0x[0-9a-fA-F]{64}$/.test(BOOT_PK)) {
    throw new Error('BOOTSTRAP_PK must be 0x + 64 hex chars');
}
const deployer = new ethers.Wallet(BOOT_PK, provider);

const ipfs      = new IpfsStore('http://127.0.0.1:5001');
const chainApi  = new EthereumChainApi(provider, 100);
const verifier  = new DefaultSignatureVerifier(chainApi);

/* ------------------------------------------------------------------ */
/* actor wiring                                                        */
/* ------------------------------------------------------------------ */

type Actor = 'Alice' | 'Bob' | 'Charly';

interface ActorCtx {
    alias: Actor;
    owner: ethers.Wallet;
    safe:  string;
    profile: Profile;
}

const ACTOR_ALIASES: Actor[] = ['Alice', 'Bob', 'Charly'];

const ACTORS: Record<Actor, ActorCtx> = Object.fromEntries(
    ACTOR_ALIASES.map(a => {
        const owner = ethers.Wallet.createRandom().connect(provider);
        return [
            a,
            {
                alias: a,
                owner,
                safe:  '',                     // filled during boot
                profile: {
                    name: a,
                    description: 'real‑safe‑e2e',
                    namespaces: {},
                },
            },
        ];
    })
) as Record<Actor, ActorCtx>;

/* ------------------------------------------------------------------ */
/* helpers                                                             */
/* ------------------------------------------------------------------ */

async function fund(addr: string, xDai = '0.001') {
    const bal  = await provider.getBalance(addr);
    const need = ethers.parseEther(xDai);
    if (bal >= need) return;

    const tx = await deployer.sendTransaction({ to: addr, value: need - bal });
    await tx.wait();
}

async function deploySafes() {
    console.log('[E2E] Boot – deployer + Safes');

    for (const a of ACTOR_ALIASES) {
        const ctx = ACTORS[a];

        await fund(ctx.owner.address);

        ctx.safe = await SafeHelpers.deploySafeAsync(
            provider,
            deployer,                                            // signer = deployer
            [deployer.address, ctx.owner.address],               // 2 owners
            1                                                    // threshold
        );

        console.log(`  ${a} Safe @ ${ctx.safe}`);
    }
}

async function execUpdateProfileCid(
    safe: string,
    owner: ethers.Wallet,
    digest32Hex: string
) {
    const calldata = SafeHelpers.encodeUpdateDigest(digest32Hex);

    await SafeHelpers.execTransactionAsync(
        provider,
        safe,
        SafeHelpers.NAME_REGISTRY_ADDRESS,
        calldata,
        owner.privateKey
    );
}

/* ------------------------------------------------------------------ */
/* test                                                                */
/* ------------------------------------------------------------------ */

describe('RealSafeEndToEnd (TypeScript port)', () => {
    beforeAll(async () => {
        await deploySafes();
    }, 120_000);

    it('PingPong_MultiRound_EndToEnd', async () => {
        const ROUNDS = 3;
        console.log(`[E2E] Writing ${ROUNDS} rounds, all sender→recipient pairs`);

        /* ---------- 1) write messages ---------------------------------- */
        for (let r = 0; r < ROUNDS; r++) {
            for (const sender of ACTOR_ALIASES) {
                const sCtx = ACTORS[sender];

                for (const recipient of ACTOR_ALIASES.filter(a => a !== sender)) {
                    const rCtx       = ACTORS[recipient];
                    const signer     = new SafeLinkSigner(sCtx.safe, chainApi);
                    const writer     = await NamespaceWriter.createAsync(
                        sCtx.profile,
                        rCtx.safe,             // namespace = recipient Safe
                        ipfs,
                        signer
                    );

                    const logicalName = `msg-r${r}-${sender[0]}to${recipient[0]}`;
                    const json        = JSON.stringify({
                        txt: `round ${r} – hi from ${sender} to ${recipient}`,
                    });

                    const link = await writer.addJsonAsync(
                        logicalName,
                        json,
                        sCtx.owner.privateKey
                    );

                    console.log(
                        `[round ${r}] ${sender} → ${recipient}  ${logicalName}  CID=${link.cid}`
                    );
                }
            }
        }

        /* ---------- 2) pin + publish profile CID via Safe -------------- */
        console.log('[E2E] Publishing profile digests via Safe');

        for (const a of ACTOR_ALIASES) {
            const ctx       = ACTORS[a];
            const profJson  = CanonicalJson.stringify(ctx.profile);
            const cid       = await ipfs.addJsonAsync(profJson, true);

            console.log(`   ${a} profile CID ${cid}`);

            const digest32  = CidConverter.cidToDigest(cid);
            const digestHex = ethers.hexlify(digest32);

            await execUpdateProfileCid(ctx.safe, ctx.owner, digestHex);
        }

        /* ---------- 3) verify last‑round inboxes ----------------------- */
        console.log('[E2E] Verifying last‑round inboxes');

        for (const recipient of ACTOR_ALIASES) {
            const rCtx = ACTORS[recipient];

            for (const sender of ACTOR_ALIASES.filter(a => a !== recipient)) {
                const sCtx  = ACTORS[sender];
                const nsKey = rCtx.safe.toLowerCase();

                expect(
                    sCtx.profile.namespaces,
                    `${sender} profile lacks namespace ${nsKey}`
                ).toHaveProperty(nsKey);

                const idxCid   = sCtx.profile.namespaces![nsKey];
                const idxDoc   = await Helpers.loadIndex(idxCid, ipfs);
                const head     = await Helpers.loadChunk(idxDoc.head, ipfs);

                const expected = `msg-r${ROUNDS - 1}-${sender[0]}to${recipient[0]}`;
                const names    = head.links.map(l => l.name);

                expect(
                    names,
                    `link ${expected} not found in ${sender}→${recipient}`
                ).toContain(expected);
            }
        }

        console.log('[E2E] ✅ all inbox checks passed');
    }, 180_000);
});
