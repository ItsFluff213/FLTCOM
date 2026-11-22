const FIRST = ["IRON", "SILENT", "AMBER", "COBALT", "PALE", "HOLLOW", "CRIMSON", "STEADY", "BRIGHT", "QUIET", "NORTHERN", "GOLDEN", "SLOW", "FERAL", "LUCID", "TALL"];
const SECOND = ["LANTERN", "HERON", "ANVIL", "COMET", "MERIDIAN", "FALCON", "HARVEST", "COMPASS", "TIDE", "BEACON", "ORCHARD", "KESTREL", "LEDGER", "SPARROW", "WAYPOINT", "THISTLE"];

export function codename(seed: number): string {
  const n = Math.abs(Math.floor(seed)) >>> 0;
  return `${FIRST[n % FIRST.length]} ${SECOND[Math.floor(n / FIRST.length) % SECOND.length]}`;
}

export const randomCodename = (): string => codename(Math.floor(Math.random() * FIRST.length * SECOND.length));
