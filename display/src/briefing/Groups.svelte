<script lang="ts">
  import type { Group, GroupEdit } from "../protocol.gen.ts";

  type Edit = Omit<GroupEdit, "protocolVersion" | "type">;

  interface Props {
    groups: readonly Group[];
    vesselName: (id: number) => string;
    selectedVesselId: number | null;
    pickedGroupId: string | null;
    onpick: (groupId: string | null) => void;
    onedit: (edit: Edit) => void;
  }

  let { groups, vesselName, selectedVesselId, pickedGroupId, onpick, onedit }: Props = $props();

  let newName = $state("");
  let renaming = $state<{ id: string; text: string } | null>(null);

  function create() {
    const name = newName.trim();
    if (name) {
      onedit({ action: "create", groupId: null, name, vesselIds: selectedVesselId === null ? [] : [selectedVesselId] });
      newName = "";
    }
  }

  function rename(group: Group) {
    const name = renaming?.id === group.id ? renaming.text.trim() : "";
    if (name && name !== group.name) {
      onedit({ action: "rename", groupId: group.id, name, vesselIds: [] });
    }
    renaming = null;
  }
</script>

<section class="groups" aria-label="Groups" data-groups>
  <h3>GROUPS</h3>
  {#each groups as group (group.id)}
    <details class:picked={group.id === pickedGroupId} data-group={group.id} open={group.id === pickedGroupId}>
      <summary
        onclick={(event) => {
          event.preventDefault();
          onpick(group.id === pickedGroupId ? null : group.id);
        }}
      >
        <span class="name">{group.name.toUpperCase()}</span>
        <span class="count">{group.vesselIds.length} {group.vesselIds.length === 1 ? "SHIP" : "SHIPS"}</span>
      </summary>
      <ul>
        {#each group.vesselIds as id (id)}
          <li data-member={id}>
            <span>{vesselName(id).toUpperCase()}</span>
            <button type="button" class="small" aria-label="Remove {vesselName(id)}" data-remove-member={id} onclick={() => onedit({ action: "remove", groupId: group.id, name: null, vesselIds: [id] })}>REMOVE</button>
          </li>
        {:else}
          <li class="empty">NO MEMBERS</li>
        {/each}
      </ul>
      <div class="row">
        <button
          type="button"
          data-add-member
          disabled={selectedVesselId === null || group.vesselIds.includes(selectedVesselId)}
          onclick={() => selectedVesselId !== null && onedit({ action: "add", groupId: group.id, name: null, vesselIds: [selectedVesselId] })}
        >
          ADD {selectedVesselId === null ? "SELECTED" : vesselName(selectedVesselId).toUpperCase()}
        </button>
      </div>
      <div class="row">
        <input
          aria-label="New name for {group.name}"
          maxlength="40"
          placeholder="NEW NAME"
          value={renaming?.id === group.id ? renaming.text : ""}
          oninput={(event) => (renaming = { id: group.id, text: event.currentTarget.value })}
          onkeydown={(event) => event.key === "Enter" && rename(group)}
          data-rename-input
        />
        <button type="button" data-rename onclick={() => rename(group)}>RENAME</button>
        <button type="button" class="danger" data-delete-group onclick={() => onedit({ action: "delete", groupId: group.id, name: null, vesselIds: [] })}>DELETE</button>
      </div>
    </details>
  {:else}
    <p class="empty">NO GROUPS · NAME ONE BELOW, IT STARTS WITH THE SELECTED VESSEL</p>
  {/each}
  <div class="row">
    <input aria-label="New group name" maxlength="40" placeholder="NEW GROUP" bind:value={newName} onkeydown={(event) => event.key === "Enter" && create()} data-new-group />
    <button type="button" data-create-group disabled={!newName.trim()} onclick={create}>CREATE</button>
  </div>
</section>

<style>
  .groups {
    display: grid;
    gap: 0.3rem;
    padding: 0.5rem 0.75rem;
    border-top: 1px solid var(--rule);
  }

  h3 {
    margin: 0;
    font-family: var(--font-ui);
    padding-left: 0.45rem;
    border-left: 2px solid var(--fg);
    font-size: 0.8rem;
    letter-spacing: 0.32em;
  }

  details {
    border: 1px solid var(--rule);
  }

  details.picked {
    border-color: #ffd84a;
  }

  summary {
    display: flex;
    justify-content: space-between;
    gap: 0.5rem;
    padding: 0.2rem 0.4rem;
    cursor: pointer;
    font-size: 0.8rem;
  }

  .picked summary {
    color: #ffd84a;
  }

  .count {
    font-size: 0.7rem;
    opacity: 0.75;
  }

  ul {
    margin: 0;
    padding: 0 0.4rem;
    list-style: none;
  }

  li {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 0.4rem;
    font-size: 0.75rem;
  }

  .row {
    display: flex;
    gap: 0.3rem;
    padding: 0.2rem 0.4rem;
  }

  .groups > .row {
    padding: 0;
  }

  input {
    flex: 1;
    min-width: 0;
  }

  input,
  button {
    min-height: 1.6rem;
    border: 1px solid var(--rule);
    background: #151211;
    color: inherit;
    font: 0.75rem var(--font-ui);
  }

  button {
    cursor: pointer;
    letter-spacing: 0.1em;
  }

  button.small {
    min-height: 1.3rem;
    font-size: 0.65rem;
  }

  button.danger {
    color: var(--alert);
    border-color: var(--alert);
  }

  button:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }

  .empty {
    margin: 0.1rem 0;
    font-size: 0.7rem;
    opacity: 0.7;
  }
</style>
