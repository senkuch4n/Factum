import type { MessagePassThroughOptions, MessageProps } from "primereact/message";
import { cn } from "@/lib/utils";

type Severity = NonNullable<MessageProps["severity"]>;

const TONE: Record<Severity, string> = {
  error: "bg-fx-danger-soft border-fx-danger text-fx-danger",
  warn: "bg-fx-warning-soft border-fx-warning text-fx-warning",
  success: "bg-fx-success-soft border-fx-success text-fx-success",
  info: "bg-fx-info-soft border-fx-info text-fx-info",
  secondary: "bg-fx-info-soft border-fx-info text-fx-info",
  contrast: "bg-fx-info-soft border-fx-info text-fx-info",
};

/**
 * Message (aviso en línea). Siempre ícono + texto, fondo `*-soft` y borde del
 * tono. Prime pone role="alert" siempre; acá los errores se anuncian con
 * aria-live="assertive" y el resto "polite". En demos estáticas, pasar
 * `role="note"` por props para no anunciar al cargar la página.
 *
 * D3-A — `secondary` y `contrast` mapean a **info** de forma intencional (un
 * aviso secundario sigue siendo un aviso). Esto difiere de `Tag`
 * (pt/tag.ts), donde `secondary`/`contrast`/sin severidad caen en neutro. La
 * divergencia entre ambos pt es deliberada, no se unifica.
 */
export const message: MessagePassThroughOptions = {
  root: ({ props }) => ({
    className: cn(
      "flex items-start gap-2 rounded-fx-md border px-3.5 py-2.5 text-fx-body-sm",
      TONE[props.severity ?? "info"],
    ),
    "aria-live": props.severity === "error" ? "assertive" : "polite",
  }),
  icon: { className: "h-4 w-4 shrink-0 mt-0.5" },
  text: { className: "min-w-0 font-medium" },
};
