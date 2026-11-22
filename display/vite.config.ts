import { fileURLToPath } from "node:url";
import { defineConfig, type Plugin } from "vitest/config";
import { svelte } from "@sveltejs/vite-plugin-svelte";

// Browsers refuse module scripts on file://, so emit one classic script instead.
function classicScript(): Plugin {
  return {
    name: "fltcom-classic-script",
    enforce: "post",
    transformIndexHtml(html) {
      return html.replace(/<script type="module" crossorigin/g, "<script defer");
    },
  };
}

export default defineConfig({
  base: "./",
  publicDir: false,
  plugins: [svelte(), classicScript()],
  build: {
    outDir: fileURLToPath(new URL("../dist/GameData/FLTCOM/web", import.meta.url)),
    emptyOutDir: true,
    rollupOptions: {
      output: { format: "iife" },
    },
  },
  test: {
    include: ["src/**/*.test.ts"],
  },
});
