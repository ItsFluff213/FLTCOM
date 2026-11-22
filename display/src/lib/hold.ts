export const HOLD_MS = 600;
/** A timer, not animation frames, drives the hold, so the confirm never waits on the frame rate. */
const FRAME_MS = 16;

/** Press-and-hold timing: a hold confirms once it lasts `ms`; letting go early confirms nothing. */
export class Hold {
  #start: number | null = null;
  #fired = false;
  readonly ms: number;

  constructor(ms = HOLD_MS) {
    this.ms = ms;
  }

  get holding(): boolean {
    return this.#start !== null;
  }

  press(now: number): void {
    if (this.#start === null) {
      this.#start = now;
      this.#fired = false;
    }
  }

  release(): void {
    this.#start = null;
  }

  tick(now: number): { progress: number; confirm: boolean } {
    if (this.#start === null) {
      return { progress: 0, confirm: false };
    }
    const progress = Math.min(1, Math.max(0, (now - this.#start) / this.ms));
    const confirm = progress >= 1 && !this.#fired;
    this.#fired ||= confirm;
    return { progress, confirm };
  }
}

export interface HoldOptions {
  onconfirm: () => void;
  ms?: number;
}

/** Makes a button act only when held: by pointer, or by Enter or Space. Writes the fill to `--hold` and marks `data-holding`. */
export function holdToConfirm(node: HTMLButtonElement, options: HoldOptions) {
  let current = options;
  let hold = new Hold(current.ms);
  let timer: ReturnType<typeof setInterval> | undefined;

  const paint = (progress: number) => {
    node.style.setProperty("--hold", String(progress));
    node.toggleAttribute("data-holding", hold.holding);
  };
  const step = () => {
    const { progress, confirm } = hold.tick(performance.now());
    paint(progress);
    if (confirm) {
      current.onconfirm();
    }
  };
  const press = () => {
    if (node.disabled || hold.holding) {
      return;
    }
    hold.press(performance.now());
    step();
    timer = setInterval(step, FRAME_MS);
  };
  const release = () => {
    hold.release();
    clearInterval(timer);
    paint(0);
  };
  const isKey = (event: KeyboardEvent) => event.key === "Enter" || event.key === " ";

  const handlers: [string, (event: Event) => void][] = [
    ["pointerdown", (event) => (event as PointerEvent).button === 0 && (node.setPointerCapture((event as PointerEvent).pointerId), press())],
    ["pointerup", release],
    ["pointercancel", release],
    ["lostpointercapture", release],
    ["blur", release],
    ["contextmenu", (event) => event.preventDefault()],
    [
      "keydown",
      (event) => {
        if (isKey(event as KeyboardEvent)) {
          event.preventDefault();
          if (!(event as KeyboardEvent).repeat) {
            press();
          }
        }
      },
    ],
    ["keyup", (event) => isKey(event as KeyboardEvent) && (event.preventDefault(), release())],
  ];
  for (const [type, handler] of handlers) {
    node.addEventListener(type, handler);
  }
  paint(0);

  return {
    update(next: HoldOptions) {
      if (next.ms !== current.ms) {
        release();
        hold = new Hold(next.ms);
      }
      current = next;
    },
    destroy() {
      release();
      for (const [type, handler] of handlers) {
        node.removeEventListener(type, handler);
      }
    },
  };
}
