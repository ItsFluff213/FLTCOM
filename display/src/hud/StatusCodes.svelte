<script lang="ts">
  import type { Fleet } from "../protocol.gen.ts";

  interface Props {
    fleet: Fleet;
    linkLabel: string;
  }

  let { fleet, linkLabel }: Props = $props();

  const count = (predicate: (vessel: Fleet["vessels"][number]) => boolean) => fleet.vessels.filter(predicate).length;
  const codes = $derived([
    { key: "LINK", value: linkLabel },
    { key: "VESSELS", value: String(fleet.vessels.length) },
    { key: "COMMS LINK", value: String(count((vessel) => vessel.comms === "link")) },
    { key: "COMMS LOS", value: String(count((vessel) => vessel.comms === "los")), alert: count((vessel) => vessel.comms === "los") > 0 },
    { key: "LOADED", value: String(count((vessel) => vessel.loaded)) },
    { key: "LOW Δv <100", value: String(count((vessel) => vessel.deltaV !== null && vessel.deltaV.total < 100)) },
    { key: "WARP", value: `x${fleet.warpRate}` },
    { key: "SEQ", value: String(fleet.seq) },
  ]);
</script>

<dl class="codes" aria-label="Status codes">
  {#each codes as code (code.key)}
    <dt>{code.key}</dt>
    <dd class:alert={code.alert} data-code={code.key}>{code.value}</dd>
  {/each}
</dl>

<style>
  .codes {
    display: grid;
    grid-template-columns: 7rem 1fr;
    gap: 0.1rem 0.5rem;
    margin: 0;
    padding: 0.5rem 0.75rem;
    font-size: 0.72rem;
  }

  dt {
    opacity: 0.65;
  }

  dd {
    margin: 0;
  }

  .alert {
    color: var(--alert);
  }
</style>
