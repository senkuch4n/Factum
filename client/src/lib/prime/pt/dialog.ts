import type { DialogPassThroughOptions } from "primereact/dialog";
import { cn } from "@/lib/utils";
import { CLOSE_BUTTON } from "./shared";

/**
 * Dialog. La posición fija y el centrado de la máscara los pone Prime con
 * estilos inline; acá van superficie, borde, radio, elevación y la
 * transición (fade + escala, solo fade con prefers-reduced-motion).
 */
export const dialog: DialogPassThroughOptions = {
  root: ({ state }) => ({
    className: cn(
      "flex flex-col overflow-hidden m-0",
      "w-[min(32rem,100%)] max-h-[90vh]",
      "bg-fx-surface-2 text-fx-text border border-fx-border rounded-fx-xl shadow-fx-3",
      state.maximized && "!w-screen !h-screen !max-h-full rounded-none",
    ),
  }),
  header: { className: "flex items-center justify-between gap-4 shrink-0 px-6 pt-5 pb-3" },
  headerTitle: { className: "m-0 min-w-0 text-fx-h3 text-fx-text" },
  headerIcons: { className: "flex items-center gap-1 shrink-0" },
  closeButton: { className: CLOSE_BUTTON },
  closeButtonIcon: { className: "w-4 h-4" },
  maximizableButton: { className: CLOSE_BUTTON },
  maximizableIcon: { className: "w-4 h-4" },
  content: { className: "overflow-y-auto px-6 pb-6 text-fx-body-sm text-fx-text-2" },
  footer: { className: "flex flex-wrap justify-end gap-2 shrink-0 px-6 pb-5 pt-1" },
  mask: ({ state }) => ({
    className: cn(
      "fixed inset-0 flex items-center justify-center p-4",
      "transition-colors duration-fx-base ease-fx",
      state.containerVisible ? "bg-fx-overlay" : "bg-transparent",
    ),
  }),
  transition: {
    timeout: { enter: 200, exit: 150 },
    classNames: {
      enter: "opacity-0 motion-safe:scale-95",
      enterActive: "!opacity-100 motion-safe:!scale-100 transition-[opacity,transform] duration-200 ease-fx",
      exit: "opacity-100",
      exitActive: "!opacity-0 motion-safe:!scale-95 transition-[opacity,transform] duration-150 ease-fx",
    },
  },
};
