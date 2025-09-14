<script lang="ts">
    import type { Profile } from "$lib/profiles/types/profile";

    let { profile, profileCid } = $props<{ profile: Profile; profileCid: string | null }>();
</script>

<div>
    <h2 class="text-xl font-semibold mt-0">
        Profile
        {#if profileCid}
            <small class="ml-2 opacity-70 break-all text-xs">({profileCid})</small>
        {/if}
    </h2>

    <div class="mb-2 opacity-90">
        <div><b>Name:</b> {profile.name}</div>
        <div><b>Description:</b> {profile.description}</div>
        {#if profile.imageUrl}
            <div><b>Image:</b> {profile.imageUrl}</div>
        {/if}
    </div>
</div>
