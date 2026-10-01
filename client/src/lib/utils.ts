import { clsx, type ClassValue } from "clsx"
import { extendTailwindMerge } from "tailwind-merge"

/*
 * tailwind-merge no conoce las escalas `fx-*` de tailwind.config.ts: sin esta
 * extensión clasifica `text-fx-body-sm` como color de texto (y lo descarta al
 * lado de `text-fx-text`) y `shadow-fx-*` como color de sombra. Solo se suman
 * grupos para clases con prefijo `fx-`: el merge de las clases legacy no cambia.
 */
const twMerge = extendTailwindMerge({
  extend: {
    classGroups: {
      "font-size": [{ text: ["fx-display", "fx-h1", "fx-h2", "fx-h3", "fx-body", "fx-body-sm", "fx-label"] }],
      shadow: [{ shadow: ["fx-1", "fx-2", "fx-3"] }],
      rounded: [{ rounded: ["fx-sm", "fx-md", "fx-lg", "fx-xl"] }],
    },
  },
})

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
