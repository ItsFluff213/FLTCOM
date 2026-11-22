import { describe, expect, it } from "vitest";
import { CUES, createSound, type Cue } from "./sound.ts";

function fakeContext() {
  const started: number[] = [];
  const param = () => ({ value: 0, setValueAtTime() {}, exponentialRampToValueAtTime() {} });
  const node = () => ({ connect: (next: unknown) => next });
  const context = {
    state: "running",
    currentTime: 0,
    destination: {},
    createOscillator: () => ({ ...node(), type: "", frequency: param(), start: (at: number) => started.push(at), stop() {} }),
    createGain: () => ({ ...node(), gain: param() }),
  };
  return { context: context as unknown as AudioContext, started };
}

describe("sound cues", () => {
  it("plays every tone of a cue through WebAudio", () => {
    const { context, started } = fakeContext();
    const sound = createSound(() => false, () => context);
    for (const cue of Object.keys(CUES) as Cue[]) {
      started.length = 0;
      sound.play(cue);
      expect(started).toHaveLength(CUES[cue].length);
    }
  });

  it("stays silent while muted and never opens an audio context", () => {
    let opened = 0;
    const sound = createSound(() => true, () => {
      opened++;
      return fakeContext().context;
    });
    sound.play("alert");
    expect(opened).toBe(0);
  });
});
