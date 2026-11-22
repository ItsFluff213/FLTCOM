import { crc32, deflateSync } from "node:zlib";

const WIDTH = 512;
const HEIGHT = 256;

function chunk(type, data) {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(data.length, 0);
  head.write(type, 4, "latin1");
  const tail = Buffer.alloc(4);
  tail.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])), 0);
  return Buffer.concat([head, data, tail]);
}

/** Encodes RGB rows as a PNG. */
export function encodePng(width, height, rgb) {
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) {
    rgb.copy(raw, y * (width * 3 + 1) + 1, y * width * 3, (y + 1) * width * 3);
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header.set([8, 2, 0, 0, 0], 8);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(raw)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function seedOf(text) {
  let hash = 2166136261;
  for (const c of text) {
    hash = Math.imul(hash ^ c.charCodeAt(0), 16777619);
  }
  return hash >>> 0;
}

/** Smooth made-up relief on the sphere: a sum of a few seeded waves over the unit vector. */
function relief(seed) {
  const waves = [];
  let state = seed;
  const next = () => ((state = Math.imul(state ^ (state >>> 15), 2246822507) >>> 0) / 4294967296);
  for (let octave = 0; octave < 24; octave++) {
    const frequency = 1.5 * 1.6 ** (octave % 6);
    const theta = next() * 2 * Math.PI;
    const z = next() * 2 - 1;
    const r = Math.sqrt(1 - z * z);
    waves.push({ x: r * Math.cos(theta) * frequency, y: r * Math.sin(theta) * frequency, z: z * frequency, phase: next() * 6.3, weight: 1 / frequency });
  }
  return (x, y, z) => waves.reduce((sum, w) => sum + Math.sin(w.x * x + w.y * y + w.z * z + w.phase) * w.weight, 0);
}

const hex = (color) => [1, 3, 5].map((i) => parseInt(color.slice(i, i + 2), 16));
const mix = (a, b, t) => a.map((value, i) => value + (b[i] - value) * t);

/**
 * A made-up equirectangular surface for a mock body, standing in for the texture the plugin captures from the game.
 * Bodies with oceans get water below a sea level; every body gets polar caps or highlands from its own colour.
 */
export function bodyTexture(name, color) {
  const base = hex(color);
  const height = relief(seedOf(name));
  const watery = name === "Kerbin" || name === "Laythe" || name === "Eve";
  const water = name === "Eve" ? [70, 30, 90] : [24, 52, 96];
  const rgb = Buffer.alloc(WIDTH * HEIGHT * 3);
  for (let row = 0; row < HEIGHT; row++) {
    const lat = Math.PI / 2 - ((row + 0.5) / HEIGHT) * Math.PI;
    for (let column = 0; column < WIDTH; column++) {
      const lon = ((column + 0.5) / WIDTH) * 2 * Math.PI - Math.PI;
      const h = height(Math.cos(lat) * Math.cos(lon), Math.cos(lat) * Math.sin(lon), Math.sin(lat));
      let pixel;
      if (watery && h < 0.1) {
        pixel = mix(water, [10, 24, 48], Math.min(1, (0.1 - h) * 1.2));
      } else {
        pixel = mix(mix(base, [0, 0, 0], 0.35), mix(base, [255, 255, 255], 0.25), Math.max(0, Math.min(1, 0.5 + h * 0.6)));
      }
      if (Math.abs(lat) > 1.25 + h * 0.08) {
        pixel = mix(pixel, [235, 240, 245], 0.85);
      }
      rgb.set(pixel.map(Math.round), (row * WIDTH + column) * 3);
    }
  }
  return encodePng(WIDTH, HEIGHT, rgb);
}
