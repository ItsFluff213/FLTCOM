<script lang="ts">
  import { saveSettings, settings } from "./settings.svelte.ts";

  let { onTestSound }: { onTestSound: () => void } = $props();
</script>

<div class="effects" role="group" aria-label="Display effects">
  <label>
    <input type="checkbox" data-setting="clean" bind:checked={settings.clean} onchange={saveSettings} />
    CLEAN
  </label>
  <label class:off={settings.clean}>
    FX
    <input type="range" data-setting="intensity" min="0" max="1" step="0.05" disabled={settings.clean} bind:value={settings.intensity} onchange={saveSettings} />
  </label>
  <label>
    <input type="checkbox" data-setting="muted" bind:checked={settings.muted} onchange={saveSettings} />
    MUTE
  </label>
  <button type="button" onclick={onTestSound}>BEEP</button>
</div>

<style>
  .effects {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem 0.9rem;
    font-family: var(--font-ui);
    font-size: 0.8rem;
    letter-spacing: 0.1em;
  }

  label {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
  }

  .off {
    opacity: 0.4;
  }

  input[type="range"] {
    width: 5rem;
    accent-color: #c9c6c2;
  }

  input[type="checkbox"] {
    accent-color: #c9c6c2;
  }

  button {
    border: 1px solid var(--rule);
    background: none;
    color: inherit;
    font: inherit;
    letter-spacing: inherit;
    cursor: pointer;
  }
</style>
