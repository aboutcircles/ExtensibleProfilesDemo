<script lang="ts">
    import { createEventDispatcher } from "svelte";
    import type { CustomDataLink } from "$lib/profiles/types/custom-data-link";
    import type { VerifyReport } from "$lib/profiles/verification/default-signature-verifier";

    let { selectedLink, verifying, verifyResult, chainWarning } = $props<{
        selectedLink: CustomDataLink | null;
        verifying: boolean;
        verifyResult: VerifyReport | null;
        chainWarning: string | null;
    }>();

    const dispatch = createEventDispatcher<{ verify: void }>();

    function onVerify(): void {
        dispatch("verify");
    }

    function formatUnixSecs(secs: number): string {
        const ms = Number(secs) * 1000;
        const isFiniteNumber = Number.isFinite(ms);
        if (!isFiniteNumber) return "(invalid timestamp)";
        return new Date(ms).toISOString();
    }
</script>

{#if selectedLink}
    <h3 class="font-semibold mt-2">Selected link</h3>
    <table class="w-full text-sm">
        <tbody>
        <tr>
            <th class="text-left pr-2 align-top whitespace-nowrap">name</th>
            <td class="font-mono break-all">{selectedLink.name}</td>
        </tr>
        <tr>
            <th class="text-left pr-2 align-top whitespace-nowrap">signer</th>
            <td class="font-mono break-all">{selectedLink.signerAddress}</td>
        </tr>
        <tr>
            <th class="text-left pr-2 align-top whitespace-nowrap">ts</th>
            <td>{formatUnixSecs(selectedLink.signedAt)}</td>
        </tr>
        <tr>
            <th class="text-left pr-2 align-top whitespace-nowrap">cid</th>
            <td class="font-mono break-all">{selectedLink.cid}</td>
        </tr>
        </tbody>
    </table>

    <div class="mt-2 flex items-center gap-2 flex-wrap">
        <button
                class="px-2 py-1 text-sm rounded border border-gray-300 bg-gray-50 hover:bg-gray-100 disabled:opacity-60 disabled:cursor-not-allowed"
                disabled={verifying}
                on:click={onVerify}
        >
            {verifying ? "Verifying…" : "Verify signature"}
        </button>

        {#if chainWarning}<span class="text-amber-700">⚠ {chainWarning}</span>{/if}

        {#if verifyResult}
            {#if verifyResult.ok}
                <span class="text-green-700">✓ valid ({verifyResult.path})</span>
            {:else}
                <span class="text-red-700">✗ invalid</span>
                {#if verifyResult.detail}<span class="opacity-60"> — {verifyResult.detail}</span>{/if}
            {/if}
        {/if}
    </div>
{:else}
    <p class="opacity-60">(select a link in the tree)</p>
{/if}
