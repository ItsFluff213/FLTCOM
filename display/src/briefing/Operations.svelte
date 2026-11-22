<script lang="ts">
  import type { CommandKind, Tracked } from "../lib/acks.ts";
  import { holdToConfirm } from "../lib/hold.ts";
  import { duration } from "../lib/kerbinTime.ts";
  import type { Missions } from "../protocol.gen.ts";
  import DescentReadout from "./DescentReadout.svelte";
  import Timeline from "./Timeline.svelte";

  interface Props {
    missions: Missions | null;
    vesselName: (id: number) => string;
    onscrub: (operationId: string) => void;
    onresume: (operationId: string) => void;
    onanswer: (promptId: number, accept: boolean) => void;
    ackOf?: (kind: CommandKind, operationId: string) => Tracked | null;
  }

  let { missions, vesselName, onscrub, onresume, onanswer, ackOf = () => null }: Props = $props();

  const operations = $derived(missions?.operations.filter((operation) => operation.state !== "scrubbed") ?? []);
  const open = (state: string) => !["done", "missed", "aborted", "scrubbed"].includes(state);
</script>

{#if missions && missions.prompts.length > 0}
  <div class="prompts" role="alertdialog" aria-label="Operator prompt">
    {#each missions.prompts as prompt (prompt.id)}
      <div class="prompt" data-prompt={prompt.kind} data-prompt-id={prompt.id}>
        <p>
          {prompt.text.toUpperCase()}
          {#if prompt.untilUt !== null}<span class="until">· WARP {duration(Math.max(0, prompt.untilUt - missions.ut))}</span>{/if}
        </p>
        <button type="button" class="ctl" data-answer="yes" onclick={() => onanswer(prompt.id, true)}>{prompt.kind === "warp" ? "WARP" : prompt.kind === "fly" ? "LET FLTCOM FLY IT" : "YES"}</button>
        <button type="button" class="ctl" data-answer="no" onclick={() => onanswer(prompt.id, false)}>{prompt.kind === "fly" ? "I FLY IT" : "NO"}</button>
      </div>
    {/each}
  </div>
{/if}

<section class="operations" aria-label="Operations" data-operations>
  <h3>OPERATIONS</h3>
  {#each operations as operation (operation.id)}
    {@const resumeAck = ackOf("resume", operation.id)}
    {@const scrubAck = ackOf("scrub", operation.id)}
    <article data-operation={operation.id} data-state={operation.state}>
      <h4>OPERATION {operation.codename} <span class="state {operation.state}">{operation.state.toUpperCase()}</span></h4>
      <Timeline burns={operation.burns} ut={missions?.ut ?? 0} {operation} others={operations} {vesselName} />
      <DescentReadout {operation} ut={missions?.ut ?? 0} />
      {#if operation.burns[operation.burnIndex]}
        <p class="who">{vesselName(operation.burns[operation.burnIndex]!.vesselId).toUpperCase()}{operation.state === "executing" ? ` · ${Math.round(operation.deltaVDone)} m/s FLOWN` : ""}</p>
      {/if}
      <div class="buttons">
        {#if operation.state === "hold"}
          <button type="button" class="ctl" data-resume data-ack={resumeAck?.status} onclick={() => onresume(operation.id)}>RESUME</button>
        {/if}
        {#if open(operation.state)}
          <button
            type="button"
            class="scrub ctl hold"
            title="Hold to {operation.state === "executing" ? "abort" : "scrub"}: mouse, Enter or Space"
            data-scrub-operation
            data-ack={scrubAck?.status}
            use:holdToConfirm={{ onconfirm: () => onscrub(operation.id) }}>{operation.state === "executing" ? "HOLD TO ABORT" : "HOLD TO SCRUB"}</button
          >
        {/if}
      </div>
      {#each [resumeAck, scrubAck] as ack, index (index)}
        {#if ack?.status === "refused"}<p class="refusal" role="status" data-refusal={ack.kind}>{ack.reason}</p>{/if}
      {/each}
    </article>
  {:else}
    <p class="empty">NO OPERATIONS</p>
  {/each}
</section>

<style>
  .operations {
    padding: 0.5rem 0.75rem;
    border-top: 1px solid var(--rule);
  }

  h3 {
    margin: 0 0 0.3rem;
    font-family: var(--font-ui);
    padding-left: 0.45rem;
    border-left: 2px solid var(--fg);
    font-size: 0.8rem;
    letter-spacing: 0.32em;
  }

  h4 {
    margin: 0.3rem 0;
    font-size: 0.8rem;
    color: #ffd84a;
  }

  .state {
    margin-left: 0.4rem;
    font-size: 0.7rem;
    letter-spacing: 0.1em;
    color: #39ff88;
  }

  .state.hold,
  .state.executing {
    color: #ffd84a;
  }

  .state.missed,
  .state.aborted {
    color: var(--alert);
  }

  .who,
  .empty {
    margin: 0.2rem 0;
    font-size: 0.7rem;
    opacity: 0.8;
  }

  .buttons {
    display: flex;
    gap: 0.5rem;
  }

  button {
    min-height: 1.8rem;
    padding: 0 0.8rem;
    border: 1px solid #ffd84a;
    background: #151211;
    color: #ffd84a;
    font: 700 0.75rem var(--font-ui);
    letter-spacing: 0.12em;
    cursor: pointer;
  }

  .scrub {
    color: var(--alert);
    border-color: var(--alert);
  }

  .prompts {
    position: fixed;
    left: 50%;
    bottom: 1rem;
    z-index: 60;
    display: grid;
    gap: 0.5rem;
    width: min(34rem, calc(100% - 32px));
    transform: translateX(-50%);
  }

  .prompt {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.5rem;
    padding: 0.6rem 0.8rem;
    border: 1px solid #ffd84a;
    background: var(--bg);
    color: #ffd84a;
  }

  .prompt p {
    flex: 1 1 14rem;
    margin: 0;
    font-size: 0.85rem;
  }

  .until {
    opacity: 0.7;
  }

  .prompt button {
    color: #39ff88;
    border-color: #39ff88;
  }

  .prompt button[data-answer="no"] {
    color: var(--alert);
    border-color: var(--alert);
  }
</style>
