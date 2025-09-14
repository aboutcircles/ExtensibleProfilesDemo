<script lang="ts">
    import ProfileTree from "$lib/components/ProfileTree.svelte";
    import ProfileSummary from "$lib/components/ProfileSummary.svelte";
    import LinkDetails from "$lib/components/LinkDetails.svelte";
    import PayloadViewer from "$lib/components/PayloadViewer.svelte";

    import {KuboIpfs} from "$lib/profiles/profile-reader/kubo-ipfs";
    import {JsonRpcChainApi} from "$lib/profiles/chain-rpc/json-rpc-chain-api";
    import {getProfileCid as resolveProfileCid} from "$lib/profiles/chain-rpc/name-registry";
    import {CIDV0_RX, ETH_ADDR_RX} from "$lib/profiles/consts";
    import {ensureLowerAddress} from "$lib/profiles/profile-reader/utils";
    import {verifyLinkDefault, type VerifyReport} from "$lib/profiles/verification/default-signature-verifier";
    import {createLogger} from "$lib/log";
    import type {Profile} from "$lib/profiles/types/profile";
    import type {CustomDataLink} from "$lib/profiles/types/custom-data-link";

    const log = createLogger("ui:page");

    // inputs
    let input = $state("");
    let rpcUrl = $state("https://rpc.aboutcircles.com");
    let chainIdStr = $state("100");
    let ipfsApi = $state("http://127.0.0.1:5001");

    // derived clients (re-created only when their config changes)
    const ipfs = $derived(new KuboIpfs({apiBase: ipfsApi}));

    // ui state
    let loading = $state(false);
    let error = $state<string | null>(null);

    let profileCid = $state<string | null>(null);
    let profile = $state<Profile | null>(null);

    // right-hand side (selection)
    let selectedLink: CustomDataLink | null = $state(null);
    let payloadText: string | null = $state(null);
    let payloadPretty: string | null = $state(null);
    let payloadError: string | null = $state(null);

    // verification
    let verifying = $state(false);
    let verifyResult: VerifyReport | null = $state(null);
    let chainWarning: string | null = $state(null);

    // in-flight coordination
    let currentLoadId = 0;
    let currentPayloadId = 0;
    let currentVerifyId = 0;
    let loadAbort: AbortController | null = null;
    let payloadAbort: AbortController | null = null;
    let verifyAbort: AbortController | null = null;

    function parseChainIdStrict(s: string): bigint {
        const trimmed = s.trim();
        const matchesDigits = /^\d+$/.test(trimmed);
        if (!matchesDigits) {
            throw new Error(`Invalid chain id: "${s}"`);
        }
        try {
            return BigInt(trimmed);
        } catch {
            throw new Error(`Invalid chain id: "${s}"`);
        }
    }

    function makeChain(): JsonRpcChainApi {
        const id = parseChainIdStrict(chainIdStr);
        return new JsonRpcChainApi(rpcUrl, id);
    }

    function resetVerification(): void {
        verifying = false;
        verifyResult = null;
        chainWarning = null;
    }

    function resetSelection(): void {
        selectedLink = null;
        payloadText = null;
        payloadPretty = null;
        payloadError = null;
        resetVerification();
    }

    function resetContent(): void {
        profileCid = null;
        profile = null;
        resetSelection();
    }

    function resetAll(): void {
        input = "";
        error = null;
        resetContent();
    }

    function clearAll(): void {
        // UI-bound clear action
        resetAll();
    }

    function validateProfile(value: unknown): Profile {
        const isObj = value !== null && typeof value === "object";
        if (!isObj) throw new Error("Profile JSON is not an object");
        const p = value as any;
        const ok =
            typeof p.schemaVersion === "string" &&
            typeof p.name === "string" &&
            typeof p.description === "string" &&
            p.namespaces && typeof p.namespaces === "object";
        if (!ok) throw new Error("Profile JSON missing required fields");
        return p as Profile;
    }

    async function loadProfile(): Promise<void> {
        const loadId = ++currentLoadId;
        loadAbort?.abort();
        loadAbort = new AbortController();

        loading = true;
        error = null;
        resetContent();

        try {
            const value = input.trim();
            log.info("loadProfile start", {loadId, input: value});
            const isCidLike = CIDV0_RX.test(value);
            const isAddressLike = ETH_ADDR_RX.test(value.toLowerCase());

            const looksValidInput = isCidLike || isAddressLike;
            if (!looksValidInput) {
                throw new Error("Enter 0x… or Qm…");
            }

            let cid: string;
            if (isCidLike) {
                cid = value;
            } else {
                const avatar = ensureLowerAddress(value);
                const chain = makeChain();
                const resolved = await resolveProfileCid(chain, avatar, {signal: loadAbort.signal});
                if (resolved === null) {
                    throw new Error(`Registry has no profile CID for ${avatar}`);
                }
                cid = resolved;
            }

            if (loadId !== currentLoadId) return;
            profileCid = cid;
            log.info("loadProfile resolved", {loadId, cid});

            const raw = await ipfs.catString(cid, {signal: loadAbort.signal});
            const parsed = JSON.parse(raw);
            const validated = validateProfile(parsed);

            if (loadId !== currentLoadId) return;
            profile = validated;
        } catch (e: any) {
            if (loadId !== currentLoadId) return;
            error = String(e?.message ?? e ?? "load failed");
            log.error("loadProfile error", {loadId, error});
        } finally {
            if (loadId === currentLoadId) {
                loading = false;
                log.info("loadProfile end", {loadId, ok: !error});
            }
        }
    }

    // tree emits selected link
    async function onSelectLink(e: CustomEvent<CustomDataLink>): Promise<void> {
        const payloadId = ++currentPayloadId;
        payloadAbort?.abort();
        payloadAbort = new AbortController();

        selectedLink = e.detail;
        resetVerification();
        log.info("selectLink", {payloadId, cid: selectedLink.cid});

        payloadText = null;
        payloadPretty = null;
        payloadError = null;

        try {
            const raw = await ipfs.catString(selectedLink.cid, {signal: payloadAbort.signal});
            if (payloadId !== currentPayloadId) return;

            payloadText = raw;
            log.debug("payload loaded", {payloadId, bytes: raw.length});
            try {
                payloadPretty = JSON.stringify(JSON.parse(raw), null, 2);
            } catch {
                payloadPretty = null; // not JSON; show raw
            }
        } catch (e: any) {
            if (payloadId !== currentPayloadId) return;
            payloadText = null;
            payloadPretty = null;
            payloadError = String(e?.message ?? e ?? "failed to load payload");
            log.error("payload error", {payloadId, error: payloadError});
        }
    }

    async function verifySelected(): Promise<void> {
        if (!selectedLink) throw new Error("no link selected");

        const verifyId = ++currentVerifyId;
        verifyAbort?.abort();
        verifyAbort = new AbortController();

        verifying = true;
        verifyResult = null;
        chainWarning = null;
        log.info("verifySelected start", {verifyId});

        try {
            const chainIdBig = parseChainIdStrict(chainIdStr);
            const chainIdNum = Number(chainIdBig);

            const chainMismatch = selectedLink.chainId !== chainIdNum;
            if (chainMismatch) {
                chainWarning = `link.chainId=${selectedLink.chainId} ≠ viewer.chainId=${chainIdNum}`;
                log.warn("chain mismatch", {verifyId, linkChainId: selectedLink.chainId, viewerChainId: chainIdNum});
            }

            const chain = makeChain();
            const result = await verifyLinkDefault(selectedLink, chain, {signal: verifyAbort.signal});

            if (verifyId !== currentVerifyId) return;
            verifyResult = result;
            log.info("verifySelected result", {verifyId, ok: result.ok, path: result.path, detail: result.detail});
        } catch (e: any) {
            if (verifyId !== currentVerifyId) return;
            const detail = String(e?.message ?? e);
            verifyResult = {ok: false, path: "none", detail};
            log.error("verifySelected error", {verifyId, error: detail});
        } finally {
            if (verifyId === currentVerifyId) {
                verifying = false;
                log.info("verifySelected end", {verifyId});
            }
        }
    }
</script>

<section class="max-w-[1200px] mx-auto my-8 px-4">
    <div class="mb-4 flex items-center justify-between gap-4">
        <h1 class="text-2xl font-semibold">Profiles Viewer</h1>
        <!-- reserved spot for future: View / Edit mode toggle -->
        <div class="text-sm opacity-60">view mode</div>
    </div>

    <!-- Controls -->
    <form class="grid grid-cols-[140px_1fr] gap-x-3 gap-y-2 items-center mb-4" on:submit|preventDefault={loadProfile}>
        <label class="text-sm font-medium">Avatar or CIDv0</label>
        <input class="font-mono border border-gray-300 rounded px-2 py-1 focus:outline-none focus:ring focus:ring-blue-200"
               bind:value={input} placeholder="0x…  or  Qm…"/>

        <label class="text-sm font-medium">RPC URL</label>
        <input class="font-mono border border-gray-300 rounded px-2 py-1 focus:outline-none focus:ring focus:ring-blue-200"
               bind:value={rpcUrl}/>

        <label class="text-sm font-medium">Chain ID</label>
        <input class="font-mono border border-gray-300 rounded px-2 py-1 focus:outline-none focus:ring focus:ring-blue-200"
               bind:value={chainIdStr}/>

        <label class="text-sm font-medium">IPFS API</label>
        <input class="font-mono border border-gray-300 rounded px-2 py-1 focus:outline-none focus:ring focus:ring-blue-200"
               bind:value={ipfsApi}/>

        <div class="col-span-full flex gap-2">
            <button type="submit"
                    class="px-3 py-1.5 rounded border border-blue-600 bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-60 disabled:cursor-not-allowed"
                    disabled={loading}>{loading ? "Loading…" : "Load"}</button>
            <button type="button" class="px-3 py-1.5 rounded border border-gray-300 bg-gray-50 hover:bg-gray-100"
                    on:click={clearAll}>Clear
            </button>
        </div>
    </form>

    {#if error}
        <div class="text-red-800 bg-red-50 border border-red-200 px-3 py-2 rounded my-2">⚠ {error}</div>
    {/if}

    {#if profile}
        <div class="grid grid-cols-2 gap-6 max-[980px]:grid-cols-1">
            <div>
                <ProfileSummary {profile} {profileCid} />

                <!-- Tree kept as-is; already modular -->
                <ProfileTree
                        {profile}
                        {ipfs}
                        on:selectLink={onSelectLink}
                />
            </div>

            <div>
                <h2 class="text-xl font-semibold mb-2">Details</h2>

                <LinkDetails
                        {selectedLink}
                        {verifying}
                        {verifyResult}
                        {chainWarning}
                        on:verify={verifySelected}
                />

                <div class="mt-3">
                    <PayloadViewer
                            {payloadError}
                            {payloadPretty}
                            {payloadText}
                    />
                </div>
            </div>
        </div>
    {/if}
</section>
