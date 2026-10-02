import type { ToastPassThroughMethodOptions, ToastPassThroughOptions } from "primereact/toast";
import { cn } from "@/lib/utils";
import { CLOSE_BUTTON } from "./shared";

/**
 * Toast. Superficie 2 + borde izquierdo de 4px del color de la severidad,
 * ícono del mismo color y texto en `--fx-text`: el estado nunca va solo en
 * color (ícono + resumen). La posición fija la pone Prime con estilos inline.
 */
type Severity = "success" | "info" | "warn" | "error" | "secondary" | "contrast";

/* Prime pasa `index` en los params de cada mensaje, aunque el tipo público
   de ToastPassThroughMethodOptions no lo declare. */
type MessageOptions = ToastPassThroughMethodOptions & { index?: number };

function severityOf(options: ToastPassThroughMethodOptions): Severity | undefined {
  const { state, index } = options as MessageOptions;
  if (index == null) return undefined;
  return state?.messages?.[index]?.message?.severity as Severity | undefined;
}

const BORDER: Partial<Record<Severity, string>> = {
  success: "border-l-fx-success",
  info: "border-l-fx-info",
  warn: "border-l-fx-warning",
  error: "border-l-fx-danger",
};

const ICON: Partial<Record<Severity, string>> = {
  success: "text-fx-success",
  info: "text-fx-info",
  warn: "text-fx-warning",
  error: "text-fx-danger",
};

export const toast: ToastPassThroughOptions = {
  root: { className: "w-[min(25rem,calc(100vw-2.5rem))]" },
  message: (options) => ({
    className: cn(
      "mb-3 overflow-hidden",
      "bg-fx-surface-2 border border-fx-border border-l-4 rounded-fx-lg shadow-fx-2",
      BORDER[severityOf(options) ?? "info"] ?? "border-l-fx-border-strong",
    ),
  }),
  content: { className: "flex items-start gap-3 p-4" },
  icon: (options) => ({
    className: cn("shrink-0 w-5 h-5 mt-px", ICON[severityOf(options) ?? "info"] ?? "text-fx-text-2"),
  }),
  text: { className: "flex flex-col flex-1 min-w-0 gap-0.5" },
  summary: { className: "text-fx-body-sm font-semibold text-fx-text break-words" },
  detail: { className: "text-fx-body-sm text-fx-text-2 break-words" },
  closeButton: { className: cn(CLOSE_BUTTON, "-m-1 w-7 h-7") },
  closeButtonIcon: { className: "w-3.5 h-3.5" },
  transition: {
    timeout: { enter: 200, exit: 150 },
    classNames: {
      enter: "opacity-0 motion-safe:translate-y-2",
      enterActive: "!opacity-100 motion-safe:!translate-y-0 transition-[opacity,transform] duration-200 ease-fx",
      exit: "opacity-100",
      exitActive: "!opacity-0 transition-opacity duration-150 ease-fx",
    },
  },
};
