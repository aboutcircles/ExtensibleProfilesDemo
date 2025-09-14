<script lang="ts">
    import { createEventDispatcher } from "svelte";
    import type { Profile } from "$lib/profiles/types/profile";
    import type { NameIndexDoc } from "$lib/profiles/types/name-index-doc";
    import type { NamespaceChunk } from "$lib/profiles/types/namespace-chunk";
    import type { CustomDataLink } from "$lib/profiles/types/custom-data-link";
    import type { IpfsStore } from "$lib/profiles/interfaces/ipfs-store";

    let { profile, ipfs } = $props<{ profile: Profile; ipfs: IpfsStore }>();

    type NsState = {
        status: "idle" | "loading" | "ready" | "error";
        index?: NameIndexDoc;
        chunks?: { cid: string; links: CustomDataLink[] }[];
        error?: string | null;
        expanded: boolean;
        chunkExpanded: Record<string, boolean>;
    };

    const dispatch = createEventDispatcher<{ selectLink: CustomDataLink }>();

    let nsState = $state<Record<string, NsState>>({});

    let showProfileFields = $state(true);
    let showNamespaces = $state(true);
    let showSigningKeys = $state(false);

    function ensureNs(nsKey: string): NsState {
        if (!nsState[nsKey]) {
            nsState[nsKey] = { status: "idle", expanded: false, chunkExpanded: {} };
        }
        return nsState[nsKey];
    }

    async function toggleNamespace(nsKey: string): Promise<void> {
        const s = ensureNs(nsKey);
        s.expanded = !s.expanded;

        if (s.expanded && s.status === "idle") {
            s.status = "loading";
            try {
                const idxCid = profile.namespaces[nsKey];
                const idxRaw = await ipfs.catString(idxCid);
                const idx = JSON.parse(idxRaw) as NameIndexDoc;

                const chunks: { cid: string; links: CustomDataLink[] }[] = [];
                let cur = idx.head;
                while (cur) {
                    const chunkRaw = await ipfs.catString(cur);
                    const chunk = JSON.parse(chunkRaw) as NamespaceChunk;
                    const links = Array.isArray(chunk.links) ? chunk.links : [];
                    chunks.push({ cid: cur, links });
                    cur = (chunk.prev ?? null) || null;
                }

                nsState[nsKey] = {
                    ...s,
                    status: "ready",
                    index: idx,
                    chunks,
                    error: null
                };
            } catch (e: any) {
                nsState[nsKey] = { ...s, status: "error", error: String(e?.message ?? e) };
            }
        }
    }

    function toggleChunk(nsKey: string, cid: string): void {
        const s = ensureNs(nsKey);
        const cur = !!s.chunkExpanded[cid];
        s.chunkExpanded = { ...s.chunkExpanded, [cid]: !cur };
    }

    function selectLink(link: CustomDataLink): void {
        dispatch("selectLink", link);
    }
</script>

<div class="font-mono text-sm">
    <!-- Profile node -->
    <div class="mb-1">
        <div class="flex items-center gap-1.5 leading-6">
            <button type="button" class="w-5 h-5 inline-flex items-center justify-center p-0 border border-gray-300 bg-gray-50 rounded cursor-pointer" on:click={() => showProfileFields = !showProfileFields}>{showProfileFields ? "▼" : "▶"}</button>
            <span class="text-xs px-1.5 py-0.5 rounded-full border bg-blue-50 border-blue-200">profile</span>
            <span class="font-semibold">{profile.name || "(no name)"}</span>
            <span class="opacity-65">schema {profile.schemaVersion}</span>
        </div>
        {#if showProfileFields}
            <ul class="list-none my-1 ml-6 p-0">
                <li><b>description</b>: <span class="font-mono">{profile.description ?? ""}</span></li>
                <li><b>imageUrl</b>: <span class="font-mono">{String(profile.imageUrl ?? "null")}</span></li>
                <li><b>previewImageUrl</b>: <span class="font-mono">{String(profile.previewImageUrl ?? "null")}</span></li>
            </ul>
        {/if}
    </div>

    <!-- Namespaces -->
    <div class="mt-2">
        <div class="flex items-center gap-1.5 leading-6">
            <button type="button" class="w-5 h-5 inline-flex items-center justify-center p-0 border border-gray-300 bg-gray-50 rounded cursor-pointer" on:click={() => showNamespaces = !showNamespaces}>{showNamespaces ? "▼" : "▶"}</button>
            <span class="text-xs px-1.5 py-0.5 rounded-full border bg-green-50 border-green-200">namespaces</span>
            <span class="opacity-65">{Object.keys(profile.namespaces).length} key{Object.keys(profile.namespaces).length === 1 ? "" : "s"}</span>
        </div>

        {#if showNamespaces}
            <ul class="list-none my-1 ml-6 p-0">
                {#each Object.entries(profile.namespaces).sort(([a],[b]) => a<b?-1:a>b?1:0) as [nsKey, idxCid]}
                    <li class="my-0.5">
                        <div class="flex items-center gap-1.5 leading-6">
                            <button type="button" class="w-5 h-5 inline-flex items-center justify-center p-0 border border-gray-300 bg-gray-50 rounded cursor-pointer" on:click={() => toggleNamespace(nsKey)}>
                                {(nsState[nsKey]?.expanded ?? false) ? "▼" : "▶"}
                            </button>
                            <span class="text-xs px-1.5 py-0.5 rounded-full border bg-green-50 border-green-200">namespace</span>
                            <span class="break-words font-mono">{nsKey}</span>
                            <small class="opacity-70 break-all text-xs ml-1">{idxCid}</small>
                            {#if nsState[nsKey]?.status === "loading"}<span class="opacity-65">loading…</span>{/if}
                            {#if nsState[nsKey]?.status === "error"}<span class="text-red-700">error: {nsState[nsKey]?.error}</span>{/if}
                        </div>

                        {#if nsState[nsKey]?.expanded && nsState[nsKey]?.status === "ready"}
                            <!-- Index node -->
                            <div class="ml-6">
                                <div class="flex items-center gap-1.5 leading-6">
                                    <span class="text-xs px-1.5 py-0.5 rounded-full border bg-orange-50 border-orange-200">index</span>
                                    <span>head <code class="font-mono">{nsState[nsKey]!.index!.head}</code></span>
                                </div>
                            </div>

                            <!-- Chunks -->
                            {#if nsState[nsKey]!.chunks && nsState[nsKey]!.chunks!.length > 0}
                                <ul class="list-none my-1 ml-6 p-0">
                                    {#each nsState[nsKey]!.chunks! as c, i}
                                        <li class="my-0.5">
                                            <div class="flex items-center gap-1.5 leading-6">
                                                <button type="button" class="w-5 h-5 inline-flex items-center justify-center p-0 border border-gray-300 bg-gray-50 rounded cursor-pointer" on:click={() => toggleChunk(nsKey, c.cid)}>
                                                    {(nsState[nsKey]!.chunkExpanded[c.cid] ?? true) ? "▼" : "▶"}
                                                </button>
                                                <span class="text-xs px-1.5 py-0.5 rounded-full border bg-indigo-50 border-indigo-200">chunk{ i === 0 ? " (head)" : "" }</span>
                                                <code class="font-mono">{c.cid}</code>
                                                <span class="opacity-65">• {c.links.length} link{c.links.length === 1 ? "" : "s"}</span>
                                            </div>

                                            {#if (nsState[nsKey]!.chunkExpanded[c.cid] ?? true)}
                                                <ul class="list-none my-0.5 ml-9 p-0">
                                                    {#each c.links as l}
                                                        <li class="grid grid-cols-[max-content_1fr] gap-2 items-baseline my-0.5">
                                                            <button type="button" class="inline-flex items-center gap-1.5 px-1.5 py-0.5 border border-gray-300 bg-blue-50 rounded hover:bg-blue-100" title={l.cid} on:click={() => selectLink(l)}>
                                                                <span class="text-xs px-1.5 py-0.5 rounded-full border bg-pink-50 border-pink-200">link</span>
                                                                <span class="font-mono">{l.name}</span>
                                                            </button>
                                                            <small class="opacity-70 break-all text-xs font-mono ml-1">{l.cid}</small>
                                                        </li>
                                                    {/each}
                                                </ul>
                                            {/if}
                                        </li>
                                    {/each}
                                </ul>
                            {:else}
                                <div class="opacity-65 ml-6">(no chunks)</div>
                            {/if}
                        {/if}
                    </li>
                {/each}
            </ul>
        {/if}
    </div>

    <!-- Signing keys -->
    <div class="mt-2">
        <div class="flex items-center gap-1.5 leading-6">
            <button type="button" class="w-5 h-5 inline-flex items-center justify-center p-0 border border-gray-300 bg-gray-50 rounded cursor-pointer" on:click={() => showSigningKeys = !showSigningKeys}>{showSigningKeys ? "▼" : "▶"}</button>
            <span class="text-xs px-1.5 py-0.5 rounded-full border bg-gray-100 border-gray-200">signingKeys</span>
            <span class="opacity-65">{Object.keys(profile.signingKeys ?? {}).length} key{Object.keys(profile.signingKeys ?? {}).length === 1 ? "" : "s"}</span>
        </div>
        {#if showSigningKeys}
            <ul class="list-none my-1 ml-6 p-0">
                {#each Object.entries(profile.signingKeys ?? {}) as [fp, meta]}
                    <li class="my-0.5">
                        <div class="flex items-center gap-1.5"><span class="font-mono break-all">{fp}</span></div>
                        <ul class="list-none my-0.5 ml-6 p-0">
                            <li><b>publicKey</b>: <span class="font-mono break-all">{meta.publicKey}</span></li>
                            <li><b>validFrom</b>: {meta.validFrom}</li>
                            <li><b>validTo</b>: {String(meta.validTo ?? "null")}</li>
                            <li><b>revokedAt</b>: {String(meta.revokedAt ?? "null")}</li>
                        </ul>
                    </li>
                {/each}
            </ul>
        {/if}
    </div>
</div>
