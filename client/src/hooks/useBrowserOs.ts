"use client";

import { useSyncExternalStore } from "react";
import { browserOs, type BrowserOs } from "@/lib/agent-messages";

const subscribe = () => () => {};

/**
 * SO de la PC según el navegador (tatana-linux F17). En el servidor y durante la
 * hidratación vale `null`, así el HTML del servidor y el primer render coinciden;
 * después React re-renderiza con el SO real.
 */
export function useBrowserOs(): BrowserOs | null {
  return useSyncExternalStore(subscribe, browserOs, () => null);
}
