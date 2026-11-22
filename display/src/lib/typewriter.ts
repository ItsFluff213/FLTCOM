const typed = new Set<string>();
const STEP_MS = 32;

export interface TypewriterOptions {
  text: string;
  clean: boolean;
}

/**
 * Types a title out once per text. The full text is always in the DOM and only a CSS clip steps across it,
 * so the title keeps its width and ends whole even when the browser throttles the page.
 */
export function typewriter(node: HTMLElement, options: TypewriterOptions) {
  const show = ({ text, clean }: TypewriterOptions) => {
    node.dataset.text = text;
    node.textContent = text;
    node.classList.remove("typing");
    if (clean || typed.has(text)) {
      return;
    }
    typed.add(text);
    node.style.setProperty("--type-steps", String(Math.max(1, text.length)));
    node.style.setProperty("--type-ms", `${text.length * STEP_MS}ms`);
    void node.offsetWidth;
    node.classList.add("typing");
  };

  show(options);
  return { update: show };
}
