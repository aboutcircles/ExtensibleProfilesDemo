<script lang="ts">
    let { payloadError, payloadPretty, payloadText } = $props<{
        payloadError: string | null;
        payloadPretty: string | null;
        payloadText: string | null;
    }>();
</script>

<h3 class="font-semibold">Payload</h3>

{#if payloadError}
    <div class="text-red-800 bg-red-50 border border-red-200 px-3 py-2 rounded my-2">⚠ {payloadError}</div>
{/if}

{#if payloadText !== null}
    {#if payloadPretty !== null}
        <pre class="font-mono whitespace-pre-wrap bg-gray-50 border border-gray-200 p-3 rounded">{payloadPretty}</pre>
    {:else}
        <pre class="font-mono whitespace-pre-wrap bg-gray-50 border border-gray-200 p-3 rounded">{payloadText}</pre>
    {/if}
{:else}
    <p class="opacity-60">(payload not loaded)</p>
{/if}
