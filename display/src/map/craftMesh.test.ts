import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { loadCraft } from "./craftMesh.ts";

const model = { positions: [0, 0, 0, 1, 0, 0, 0, 1, 0], indices: [0, 1, 2] };
const noWait = () => Promise.resolve();

beforeEach(() => {
  vi.stubGlobal("location", { protocol: "http:", host: "127.0.0.1:47800" });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("loadCraft", () => {
  it("keeps asking while the game has not built the model yet, then shows it", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("", { status: 404 }))
      .mockResolvedValueOnce(new Response("", { status: 404 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(model), { status: 200 }));
    vi.stubGlobal("fetch", fetch);

    const geometry = await loadCraft("/art/craft/late.json", noWait);

    expect(geometry).not.toBeNull();
    expect(fetch).toHaveBeenCalledTimes(3);
  });

  it("gives up on a server error instead of asking forever", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response("", { status: 500 }));
    vi.stubGlobal("fetch", fetch);

    expect(await loadCraft("/art/craft/broken.json", noWait)).toBeNull();
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
