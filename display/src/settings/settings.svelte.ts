export interface Settings {
  clean: boolean;
  intensity: number;
  muted: boolean;
}

export const DEFAULT_SETTINGS: Settings = { clean: false, intensity: 0.6, muted: false };
export const SETTINGS_KEY = "fltcom.settings";

export function parseSettings(text: string | null): Settings {
  if (text === null) {
    return { ...DEFAULT_SETTINGS };
  }
  try {
    const raw: unknown = JSON.parse(text);
    if (typeof raw !== "object" || raw === null) {
      return { ...DEFAULT_SETTINGS };
    }
    const value = raw as Record<string, unknown>;
    return {
      clean: typeof value.clean === "boolean" ? value.clean : DEFAULT_SETTINGS.clean,
      intensity: typeof value.intensity === "number" && value.intensity >= 0 && value.intensity <= 1 ? value.intensity : DEFAULT_SETTINGS.intensity,
      muted: typeof value.muted === "boolean" ? value.muted : DEFAULT_SETTINGS.muted,
    };
  } catch {
    return { ...DEFAULT_SETTINGS };
  }
}

function load(): Settings {
  try {
    return parseSettings(localStorage.getItem(SETTINGS_KEY));
  } catch {
    return { ...DEFAULT_SETTINGS };
  }
}

export const settings: Settings = $state(load());

export function saveSettings(): void {
  try {
    localStorage.setItem(SETTINGS_KEY, JSON.stringify({ clean: settings.clean, intensity: settings.intensity, muted: settings.muted }));
  } catch {
    return;
  }
}
