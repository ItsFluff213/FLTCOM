export type Cue = "countdown" | "burnStart" | "burnEnd" | "alert";

interface Tone {
  at: number;
  hz: number;
  seconds: number;
  wave: OscillatorType;
}

export const CUES: Record<Cue, readonly Tone[]> = {
  countdown: [{ at: 0, hz: 880, seconds: 0.09, wave: "square" }],
  burnStart: [
    { at: 0, hz: 440, seconds: 0.12, wave: "sawtooth" },
    { at: 0.14, hz: 660, seconds: 0.12, wave: "sawtooth" },
    { at: 0.28, hz: 880, seconds: 0.22, wave: "sawtooth" },
  ],
  burnEnd: [
    { at: 0, hz: 880, seconds: 0.12, wave: "sine" },
    { at: 0.14, hz: 587, seconds: 0.12, wave: "sine" },
    { at: 0.28, hz: 392, seconds: 0.26, wave: "sine" },
  ],
  alert: [
    { at: 0, hz: 988, seconds: 0.16, wave: "square" },
    { at: 0.22, hz: 740, seconds: 0.16, wave: "square" },
    { at: 0.44, hz: 988, seconds: 0.16, wave: "square" },
    { at: 0.66, hz: 740, seconds: 0.16, wave: "square" },
  ],
};

const LEVEL = 0.06;

export interface Sound {
  play(cue: Cue): void;
}

export function createSound(isMuted: () => boolean, makeContext: () => AudioContext = () => new AudioContext()): Sound {
  let context: AudioContext | null = null;
  return {
    play(cue) {
      if (isMuted()) {
        return;
      }
      try {
        context ??= makeContext();
        if (context.state === "suspended") {
          void context.resume();
        }
        const start = context.currentTime;
        for (const tone of CUES[cue]) {
          const oscillator = context.createOscillator();
          const gain = context.createGain();
          oscillator.type = tone.wave;
          oscillator.frequency.value = tone.hz;
          gain.gain.setValueAtTime(LEVEL, start + tone.at);
          gain.gain.exponentialRampToValueAtTime(0.0001, start + tone.at + tone.seconds);
          oscillator.connect(gain).connect(context.destination);
          oscillator.start(start + tone.at);
          oscillator.stop(start + tone.at + tone.seconds + 0.02);
        }
      } catch {
        return;
      }
    },
  };
}
