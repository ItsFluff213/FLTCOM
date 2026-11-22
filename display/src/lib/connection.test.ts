import { readFileSync } from "node:fs";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PROTOCOL_VERSION } from "../protocol.gen.ts";
import { connect, PING_MS, PONG_TIMEOUT_MS, RETRY_MS, artUrl, socketUrl, STALE_AFTER_SNAPSHOTS, type Link } from "./connection.ts";

const SNAPSHOT_SECONDS = 0.5;
const STALE_MS = SNAPSHOT_SECONDS * 1000 * STALE_AFTER_SNAPSHOTS;
const fleetText = readFileSync(new URL("../../../tests/fixtures/telemetry/kerbin-small.json", import.meta.url), "utf8");
const hello = (version = PROTOCOL_VERSION) =>
  JSON.stringify({ protocolVersion: version, type: "hello", server: "test", saveName: null, snapshotInterval: SNAPSHOT_SECONDS });
const pong = (nonce: number) => JSON.stringify({ protocolVersion: PROTOCOL_VERSION, type: "pong", nonce });
const pings = (socket: FakeSocket) => socket.sent.map((text) => JSON.parse(text)).filter((message) => message.type === "ping");
const bye = (reason: string) => JSON.stringify({ protocolVersion: PROTOCOL_VERSION, type: "bye", reason });

class FakeSocket {
  sent: string[] = [];
  closed = false;
  onopen: (() => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  onclose: (() => void) | null = null;

  open() {
    this.onopen?.();
  }

  receive(text: string) {
    this.onmessage?.({ data: text });
  }

  send(text: string) {
    this.sent.push(text);
  }

  close() {
    if (!this.closed) {
      this.closed = true;
      this.onclose?.();
    }
  }
}

function start() {
  const sockets: FakeSocket[] = [];
  const links: Link[] = [];
  const stop = connect({
    url: "ws://test/ws",
    onLink: (link) => links.push(link),
    createSocket: () => {
      const socket = new FakeSocket();
      sockets.push(socket);
      return socket as unknown as WebSocket;
    },
  });
  return { sockets, links, stop, last: () => links[links.length - 1] };
}

describe("connect", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("says hello with the protocol version when the socket opens", () => {
    const { sockets } = start();
    sockets[0].open();
    expect(JSON.parse(sockets[0].sent[0])).toEqual({ protocolVersion: PROTOCOL_VERSION, type: "hello", client: "fltcom-display" });
  });

  it("shows the fleet once the server says hello", () => {
    const { sockets, links, last } = start();
    expect(links).toHaveLength(0);
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(fleetText);
    const link = last();
    expect(link.status === "live" && link.fleet?.vessels.map((vessel) => vessel.registryCode)).toContain("FLT-1101-STN");
  });

  it("keeps the last surface markers while snapshots arrive without them, and takes a new list when one comes", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(fleetText);
    const markerIds = (link: Link) => (link.status === "live" ? link.fleet?.markers?.items.map((marker) => marker.id) : undefined);
    expect(markerIds(last())).toContain("flag:1190");

    const quiet = { ...JSON.parse(fleetText), seq: 2, markers: null };
    sockets[0].receive(JSON.stringify(quiet));
    expect(markerIds(last())).toContain("flag:1190");

    const fresh = { ...quiet, seq: 3, markers: { items: [] } };
    sockets[0].receive(JSON.stringify(fresh));
    expect(markerIds(last())).toEqual([]);
  });

  it("reports the link lost, reconnects, and keeps the last fleet until a new one arrives", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(fleetText);

    sockets[0].close();
    const lost = last();
    expect(lost.status).toBe("lost");
    expect(lost.status === "lost" && lost.lastFleet?.vessels).toHaveLength(7);

    vi.advanceTimersByTime(RETRY_MS - 1);
    expect(sockets).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(sockets).toHaveLength(2);

    sockets[1].close();
    vi.advanceTimersByTime(RETRY_MS);
    expect(sockets).toHaveLength(3);
    expect(last().status).toBe("lost");

    sockets[2].open();
    sockets[2].receive(hello());
    const live = last();
    expect(live.status === "live" && live.fleet?.vessels).toHaveLength(7);
  });

  it("rejects a server with another protocol version, closes, and stops retrying", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello(999));
    expect(last()).toEqual({ status: "versionMismatch", received: 999 });
    expect(sockets[0].closed).toBe(true);

    sockets[0].receive(fleetText);
    vi.advanceTimersByTime(RETRY_MS * 10);
    expect(sockets).toHaveLength(1);
    expect(last().status).toBe("versionMismatch");
  });

  it("drops the fleet when a later message has another version", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(fleetText);
    sockets[0].receive(fleetText.replace(`"protocolVersion": ${PROTOCOL_VERSION}`, '"protocolVersion": 999'));
    expect(last()).toEqual({ status: "versionMismatch", received: 999 });
  });

  it("holds a denial across retries and recovers on a good hello", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(bye("multiplayerSession"));
    sockets[0].close();
    expect(last()).toEqual({ status: "denied", reason: "multiplayerSession" });

    vi.advanceTimersByTime(RETRY_MS);
    sockets[1].close();
    expect(last()).toEqual({ status: "denied", reason: "multiplayerSession" });

    vi.advanceTimersByTime(RETRY_MS);
    sockets[2].open();
    sockets[2].receive(hello());
    expect(last().status).toBe("live");
  });

  it("ignores a malformed message and stays live", () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => {});
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(JSON.stringify({ protocolVersion: PROTOCOL_VERSION, type: "fleet" }));
    expect(last().status).toBe("live");
    expect(error).toHaveBeenCalledWith(expect.stringContaining("$.seq"));
    error.mockRestore();
  });

  it("keeps the last good fleet when a fleet repeats a vessel id", () => {
    const error = vi.spyOn(console, "error").mockImplementation(() => {});
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(fleetText);
    const good = last();

    const repeated = JSON.parse(fleetText);
    repeated.vessels[2].id = repeated.vessels[0].id;
    sockets[0].receive(JSON.stringify(repeated));

    expect(last()).toBe(good);
    expect(error).toHaveBeenCalledWith(expect.stringContaining("$.vessels[2].id"));
    error.mockRestore();
  });

  it("calls the fleet stale when snapshots stop while the socket stays open, and live when one arrives", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(fleetText);

    vi.advanceTimersByTime(STALE_MS - 1);
    expect(last().status).toBe("live");
    vi.advanceTimersByTime(1);
    const stale = last();
    expect(stale.status).toBe("stale");
    expect(stale.status === "stale" && stale.fleet.vessels).toHaveLength(7);

    sockets[0].receive(fleetText);
    expect(last().status).toBe("live");
    vi.advanceTimersByTime(STALE_MS - 1);
    expect(last().status).toBe("live");
  });

  it("does not call a link stale before it has shown a fleet", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    vi.advanceTimersByTime(STALE_MS * 2);
    expect(last()).toEqual({ status: "live", server: "test", fleet: null });
  });

  it("pings while the server answers and reconnects when a pong is overdue", () => {
    const { sockets, last } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    expect(pings(sockets[0])).toHaveLength(0);

    vi.advanceTimersByTime(PING_MS);
    expect(pings(sockets[0])).toEqual([{ protocolVersion: PROTOCOL_VERSION, type: "ping", nonce: 1 }]);
    sockets[0].receive(pong(1));

    vi.advanceTimersByTime(PING_MS);
    expect(pings(sockets[0]).map((ping) => ping.nonce)).toEqual([1, 2]);
    vi.advanceTimersByTime(PONG_TIMEOUT_MS - 1);
    expect(last().status).toBe("live");
    expect(sockets[0].closed).toBe(false);

    vi.advanceTimersByTime(1);
    expect(last().status).toBe("lost");
    expect(sockets[0].closed).toBe(true);
    vi.advanceTimersByTime(RETRY_MS);
    expect(sockets).toHaveLength(2);
  });

  it("reports no link change after stop", () => {
    const { sockets, links, stop } = start();
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(fleetText);
    const seen = links.length;

    stop();
    vi.advanceTimersByTime(STALE_MS + PING_MS + PONG_TIMEOUT_MS + RETRY_MS);
    expect(links).toHaveLength(seen);
    expect(sockets).toHaveLength(1);
  });

  it("stops reconnecting once disconnected", () => {
    const { sockets, stop } = start();
    stop();
    vi.advanceTimersByTime(RETRY_MS * 5);
    expect(sockets).toHaveLength(1);
    expect(sockets[0].closed).toBe(true);
  });
});

describe("socketUrl", () => {
  it("uses the page's own server over HTTP and the default port from a file", () => {
    expect(socketUrl({ protocol: "http:", host: "127.0.0.1:47801" })).toBe("ws://127.0.0.1:47801/ws");
    expect(socketUrl({ protocol: "file:", host: "" })).toBe("ws://127.0.0.1:47800/ws");
  });

  it("fetches art from the same server the socket uses", () => {
    expect(artUrl("/art/body/Kerbin.png", { protocol: "http:", host: "127.0.0.1:47801" })).toBe("/art/body/Kerbin.png");
    expect(artUrl("/art/body/Kerbin.png", { protocol: "file:", host: "" })).toBe("http://127.0.0.1:47800/art/body/Kerbin.png");
  });

  it("hands each layout message to onLayout without touching the link", () => {
    const layouts: number[] = [];
    const sockets: FakeSocket[] = [];
    const stop = connect({
      url: "ws://test/ws",
      onLink: () => {},
      onLayout: (layout) => layouts.push(layout.vesselId),
      createSocket: () => {
        const socket = new FakeSocket();
        sockets.push(socket);
        return socket as unknown as WebSocket;
      },
    });
    sockets[0].open();
    sockets[0].receive(hello());
    sockets[0].receive(JSON.stringify({ protocolVersion: PROTOCOL_VERSION, type: "layout", vesselId: 1105, revision: 1, parts: [], model: null }));
    expect(layouts).toEqual([1105]);
    stop();
  });
});
