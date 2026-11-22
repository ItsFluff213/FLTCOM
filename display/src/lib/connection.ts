import {
  PROTOCOL_VERSION,
  parseServerMessage,
  type ByeReason,
  type BriefingMessage,
  type ClientHello,
  type ClientMessage,
  type CraftLayout,
  type Fleet,
  type GroupRoster,
  type Missions,
  type Ping,
  type ServerMessage,
} from "../protocol.gen.ts";

export const DEFAULT_PORT = 47800;
export const RETRY_MS = 1000;
export const PING_MS = 2000;
export const PONG_TIMEOUT_MS = 3000;
export const STALE_AFTER_SNAPSHOTS = 4;
export const APPLY_MEASURE = "fltcom:apply";

export type DenyReason = Exclude<ByeReason, "shutdown" | "protocolMismatch">;

export type Link =
  | { readonly status: "connecting" }
  | { readonly status: "live"; readonly server: string; readonly fleet: Fleet | null }
  | { readonly status: "stale"; readonly server: string; readonly fleet: Fleet }
  | { readonly status: "lost"; readonly lastFleet: Fleet | null }
  | { readonly status: "versionMismatch"; readonly received: number | null }
  | { readonly status: "denied"; readonly reason: DenyReason };

export type LinkEvent =
  | { readonly kind: "message"; readonly message: ServerMessage }
  | { readonly kind: "fleetOverdue" }
  | { readonly kind: "versionMismatch"; readonly received: number }
  | { readonly kind: "closed" };

export function knownFleet(link: Link): Fleet | null {
  switch (link.status) {
    case "live":
    case "stale":
      return link.fleet;
    case "lost":
      return link.lastFleet;
    default:
      return null;
  }
}

export function reduceLink(link: Link, event: LinkEvent): Link {
  if (link.status === "versionMismatch") {
    return link;
  }
  if (event.kind === "versionMismatch") {
    return { status: "versionMismatch", received: event.received };
  }
  if (event.kind === "closed") {
    return link.status === "denied" ? link : { status: "lost", lastFleet: knownFleet(link) };
  }
  if (event.kind === "fleetOverdue") {
    return link.status === "live" && link.fleet !== null ? { status: "stale", server: link.server, fleet: link.fleet } : link;
  }
  const message = event.message;
  switch (message.type) {
    case "hello":
      return { status: "live", server: message.server, fleet: knownFleet(link) };
    case "fleet":
      return link.status === "live" || link.status === "stale" ? { status: "live", server: link.server, fleet: withMarkers(message, link.fleet) } : link;
    case "pong":
    case "layout":
    case "briefing":
    case "missions":
    case "groups":
      return link;
    case "bye":
      if (message.reason === "shutdown") {
        return { status: "lost", lastFleet: knownFleet(link) };
      }
      if (message.reason === "protocolMismatch") {
        return { status: "versionMismatch", received: null };
      }
      return { status: "denied", reason: message.reason };
  }
}

/** Surface markers come only when they change (and every few seconds); in between the fleet keeps the last list it got. */
export function withMarkers(fleet: Fleet, previous: Fleet | null): Fleet {
  return fleet.markers === null && previous?.markers ? { ...fleet, markers: previous.markers } : fleet;
}

export function socketUrl(location: { protocol: string; host: string }): string {
  if (location.protocol === "http:" || location.protocol === "https:") {
    return `${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/ws`;
  }
  return `ws://127.0.0.1:${DEFAULT_PORT}/ws`;
}

export interface ConnectOptions {
  url: string;
  onLink: (link: Link) => void;
  onLayout?: (layout: CraftLayout) => void;
  onBriefing?: (briefing: BriefingMessage) => void;
  onMissions?: (missions: Missions) => void;
  onGroups?: (roster: GroupRoster) => void;
  createSocket?: (url: string) => WebSocket;
  retryMs?: number;
}

export type Command = ClientMessage extends infer M ? (M extends ClientMessage ? Omit<M, "protocolVersion"> : never) : never;
export type Connection = (() => void) & { send: (command: Command) => boolean };

export function connect({ url, onLink, onLayout, onBriefing, onMissions, onGroups, createSocket = (target) => new WebSocket(target), retryMs = RETRY_MS }: ConnectOptions): Connection {
  let link: Link = { status: "connecting" };
  let socket: WebSocket | null = null;
  let retry: ReturnType<typeof setTimeout> | undefined;
  let stopped = false;
  let measures = 0;

  const apply = (event: LinkEvent) => {
    if (stopped) {
      return;
    }
    const next = reduceLink(link, event);
    if (next !== link) {
      link = next;
      onLink(link);
    }
  };

  const open = () => {
    const current = createSocket(url);
    socket = current;
    let nonce = 0;
    let staleMs: number | null = null;
    let pingTimer: ReturnType<typeof setTimeout> | undefined;
    let pongTimer: ReturnType<typeof setTimeout> | undefined;
    let staleTimer: ReturnType<typeof setTimeout> | undefined;

    const closed = () => {
      clearTimeout(pingTimer);
      clearTimeout(pongTimer);
      clearTimeout(staleTimer);
      current.onopen = current.onmessage = current.onclose = null;
      socket = null;
      apply({ kind: "closed" });
      if (!stopped && link.status !== "versionMismatch") {
        retry = setTimeout(open, retryMs);
      }
    };

    const sendPing = () => {
      nonce = (nonce + 1) >>> 0;
      const ping: Ping = { protocolVersion: PROTOCOL_VERSION, type: "ping", nonce };
      current.send(JSON.stringify(ping));
      pongTimer = setTimeout(() => {
        closed();
        current.close();
      }, PONG_TIMEOUT_MS);
    };

    const expectFleet = () => {
      clearTimeout(staleTimer);
      if (staleMs !== null) {
        staleTimer = setTimeout(() => apply({ kind: "fleetOverdue" }), staleMs);
      }
    };

    current.onopen = () => {
      const hello: ClientHello = { protocolVersion: PROTOCOL_VERSION, type: "hello", client: "fltcom-display" };
      current.send(JSON.stringify(hello));
      pingTimer = setTimeout(sendPing, PING_MS);
    };
    current.onmessage = (event: MessageEvent) => {
      const start = performance.now();
      const parsed = parseServerMessage(String(event.data));
      if (!parsed.ok) {
        if (parsed.error.kind === "versionMismatch") {
          apply({ kind: "versionMismatch", received: parsed.error.received });
          current.close();
        } else {
          console.error(`protocol error at ${parsed.error.path}: expected ${parsed.error.expected}`);
        }
        return;
      }
      const message = parsed.message;
      apply({ kind: "message", message });
      switch (message.type) {
        case "hello":
          staleMs = message.snapshotInterval * 1000 * STALE_AFTER_SNAPSHOTS;
          expectFleet();
          break;
        case "fleet":
          expectFleet();
          if (++measures % 1000 === 0) {
            performance.clearMeasures(APPLY_MEASURE);
          }
          performance.measure(APPLY_MEASURE, { start });
          break;
        case "layout":
          onLayout?.(message);
          break;
        case "briefing":
          onBriefing?.(message);
          break;
        case "missions":
          onMissions?.(message);
          break;
        case "groups":
          onGroups?.(message);
          break;
        case "pong":
          if (message.nonce === nonce) {
            clearTimeout(pongTimer);
            pingTimer = setTimeout(sendPing, PING_MS);
          }
          break;
      }
    };
    current.onclose = closed;
  };

  open();
  const stop = () => {
    stopped = true;
    clearTimeout(retry);
    socket?.close();
  };
  const send = (command: Command): boolean => {
    if (socket?.readyState !== WebSocket.OPEN) {
      return false;
    }
    socket.send(JSON.stringify({ protocolVersion: PROTOCOL_VERSION, ...command }));
    return true;
  };
  return Object.assign(stop, { send });
}

/** Where to fetch art the plugin serves, such as /art/body/Kerbin.png: the page's own server, or the default one when opened from disk. */
export function artUrl(path: string, location: { protocol: string; host: string }): string {
  if (location.protocol === "http:" || location.protocol === "https:") {
    return path;
  }
  return `http://127.0.0.1:${DEFAULT_PORT}${path}`;
}
