import type { DialogPassThroughOptions, DialogProps } from "primereact/dialog";
import { cn } from "@/lib/utils";
import { CLOSE_BUTTON } from "./shared";

/**
 * Dialog. La posición fija y el centrado de la máscara los pone Prime con
 * estilos inline; acá van superficie, borde, radio, elevación y la
 * transición (fade + escala, solo fade con prefers-reduced-motion).
 *
 * Variante "drawer" (`position="right"`, la guía USB): panel de alto
 * completo pegado a la derecha que entra deslizando (solo fade con reduced
 * motion). Prime pone el `justify-content: flex-end` de la máscara inline.
 */
const isDrawer = (props: DialogProps | undefined) => props?.position === "right";

export const dialog: DialogPassThroughOptions = {
  root: ({ props, state }) => ({
    className: isDrawer(props)
      ? cn(
          "flex flex-col overflow-hidden m-0",
          "h-full max-h-full w-full max-w-xl md:max-w-2xl",
          "rounded-none sm:rounded-l-fx-xl border-y-0 border-r-0 border-l border-fx-border",
          "bg-fx-surface-1 text-fx-text shadow-fx-3",
        )
      : cn(
          "flex flex-col overflow-hidden m-0",
          "w-[min(32rem,100%)] max-h-[90vh]",
          "bg-fx-surface-2 text-fx-text border border-fx-border rounded-fx-xl shadow-fx-3",
          state.maximized && "!w-screen !h-screen !max-h-full rounded-none",
        ),
  }),
  header: ({ props }) => ({
    className: isDrawer(props)
      ? "flex items-center justify-between gap-4 shrink-0 px-5 py-3.5 border-b border-fx-border"
      : "flex items-center justify-between gap-4 shrink-0 px-6 pt-5 pb-3",
  }),
  headerTitle: { className: "m-0 min-w-0 text-fx-h3 text-fx-text" },
  headerIcons: { className: "flex items-center gap-1 shrink-0" },
  closeButton: { className: CLOSE_BUTTON },
  closeButtonIcon: { className: "w-4 h-4" },
  maximizableButton: { className: CLOSE_BUTTON },
  maximizableIcon: { className: "w-4 h-4" },
  content: ({ props }) => ({
    className: isDrawer(props)
      ? "flex-1 overflow-y-auto overscroll-contain p-5 text-fx-body-sm text-fx-text-2"
      : "overflow-y-auto overscroll-contain px-6 pb-6 text-fx-body-sm text-fx-text-2",
  }),
  footer: ({ props }) => ({
    className: isDrawer(props)
      ? "flex flex-wrap items-center gap-2 shrink-0 px-5 py-3 border-t border-fx-border"
      : "flex flex-wrap justify-end gap-2 shrink-0 px-6 pb-5 pt-1",
  }),
  mask: ({ props, state }) => ({
    className: cn(
      "fixed inset-0 flex items-center justify-center",
      isDrawer(props) ? "p-0" : "p-4",
      "transition-colors duration-fx-base ease-fx",
      state.containerVisible ? "bg-fx-overlay" : "bg-transparent",
    ),
  }),
  transition: ({ props }) =>
    isDrawer(props)
      ? {
          timeout: { enter: 320, exit: 200 },
          classNames: {
            enter: "opacity-0 motion-safe:translate-x-full",
            enterActive: "!opacity-100 motion-safe:!translate-x-0 transition-[opacity,transform] duration-fx-slow ease-fx",
            exit: "opacity-100",
            exitActive: "!opacity-0 motion-safe:!translate-x-full transition-[opacity,transform] duration-200 ease-fx",
          },
        }
      : {
          timeout: { enter: 200, exit: 150 },
          classNames: {
            enter: "opacity-0 motion-safe:scale-95",
            enterActive: "!opacity-100 motion-safe:!scale-100 transition-[opacity,transform] duration-200 ease-fx",
            exit: "opacity-100",
            exitActive: "!opacity-0 motion-safe:!scale-95 transition-[opacity,transform] duration-150 ease-fx",
          },
        },
};
