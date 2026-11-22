<script lang="ts">
  import type { ContingencyNote } from "../protocol.gen.ts";

  interface Props {
    notes: readonly ContingencyNote[];
  }

  let { notes }: Props = $props();
</script>

<ul class="contingencies" data-contingencies>
  {#each notes as note, index (index)}
    <li data-severity={note.severity} class={note.severity}>{note.severity === "red" ? "RED" : "AMBER"} · {note.text}</li>
  {:else}
    <li class="clear" data-severity="clear">NO CONTINGENCIES</li>
  {/each}
</ul>

<style>
  .contingencies {
    margin: 0;
    padding: 0;
    list-style: none;
    font-size: 0.75rem;
  }

  li {
    padding: 0.15rem 0.4rem;
    border-left: 3px solid var(--rule);
  }

  .red {
    color: var(--alert);
    border-left-color: var(--alert);
  }

  .amber {
    color: #ffd84a;
    border-left-color: #ffd84a;
  }

  .clear {
    opacity: 0.7;
  }
</style>
