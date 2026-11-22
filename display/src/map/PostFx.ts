import type { Settings } from "../settings/settings.svelte.ts";

export function fxVariables(settings: Settings): Record<string, string> {
  const level = settings.clean ? 0 : settings.intensity;
  return {
    "--fx-grain": String(0.16 * level),
    "--fx-scan": String(0.22 * level),
    "--fx-vignette": String(0.75 * level),
    "--fx-glow": String(level),
    "--fx-split": `${(1.6 * level).toFixed(2)}px`,
  };
}

export function applyFx(root: HTMLElement, settings: Settings): void {
  for (const [name, value] of Object.entries(fxVariables(settings))) {
    root.style.setProperty(name, value);
  }
  root.dataset.clean = String(settings.clean);
}

export function startGrain(canvas: HTMLCanvasElement, intensity: () => number): () => void {
  const size = 128;
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext("2d");
  if (!ctx) {
    return () => {};
  }
  const image = ctx.createImageData(size, size);
  const timer = setInterval(() => {
    const level = intensity();
    if (level <= 0) {
      return;
    }
    for (let i = 0; i < image.data.length; i += 4) {
      const value = Math.random() * 255;
      image.data[i] = image.data[i + 1] = image.data[i + 2] = value;
      image.data[i + 3] = 255;
    }
    ctx.putImageData(image, 0, 0);
  }, 90);
  return () => clearInterval(timer);
}
