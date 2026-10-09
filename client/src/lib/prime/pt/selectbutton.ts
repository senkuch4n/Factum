import type { SelectButtonPassThroughOptions } from "primereact/selectbutton";
import { cn } from "@/lib/utils";
import { FOCUS_RING } from "./shared";

/**
 * SelectButton (selector de vista, pestañas de la guía y del soporte).
 * Grupo segmentado sobre superficie 2; la opción activa usa el acento
 * (selección activa). Prime pone `role="group"` y `aria-pressed`.
 */
export const selectbutton: SelectButtonPassThroughOptions = {
  root: {
    className: "inline-flex items-stretch gap-0.5 rounded-fx-md border border-fx-border-strong bg-fx-surface-2 p-0.5",
  },
  button: ({ context }) => ({
    className: cn(
      "inline-flex items-center justify-center gap-1.5 min-h-8 px-2.5 rounded-fx-sm",
      "text-fx-body-sm font-semibold cursor-pointer select-none",
      "transition-colors duration-fx-fast ease-fx",
      FOCUS_RING,
      context.selected
        ? "bg-fx-accent text-fx-on-accent"
        : "text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text",
      context.disabled && "opacity-50 cursor-not-allowed",
    ),
  }),
  label: { className: "leading-none" },
};
