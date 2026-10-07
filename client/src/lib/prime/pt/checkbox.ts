import type { CheckboxPassThroughOptions } from "primereact/checkbox";
import { cn } from "@/lib/utils";

/**
 * Checkbox (micrófono de la PC en la captura, selección del explorador).
 * Prime renderiza un `<input type="checkbox">` real (id = `inputId`) y
 * después el `box`: el input va invisible encima de todo (recibe click,
 * teclado y foco) y el box se pinta con `peer-*`.
 * Marcado = selección activa → acento. El ícono se pasa por prop
 * (`icon={<Check …/>}`) para no depender del de Prime.
 */
export const checkbox: CheckboxPassThroughOptions = {
  root: ({ props }) => ({
    className: cn(
      "relative inline-flex shrink-0 w-5 h-5 align-middle",
      props.disabled ? "opacity-50" : "cursor-pointer",
    ),
  }),
  input: {
    className:
      "peer absolute inset-0 z-10 m-0 p-0 w-full h-full opacity-0 appearance-none cursor-pointer disabled:cursor-not-allowed",
  },
  box: ({ context }) => ({
    className: cn(
      "flex items-center justify-center w-5 h-5 rounded-fx-sm border-2 transition-colors duration-fx-fast ease-fx",
      context.checked
        ? "bg-fx-accent border-fx-accent text-fx-on-accent"
        : "bg-fx-surface-1 border-fx-border-strong text-transparent",
      // `outline-none` (TW3: 2px solid transparent) da el estilo del contorno:
      // tailwind-merge 3 descarta `peer-focus-visible:outline` frente a `…:outline-2`.
      "outline-none peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-fx-focus",
      !context.disabled && !context.checked && "peer-hover:border-fx-text-3",
    ),
  }),
  icon: { className: "w-3.5 h-3.5" },
};
