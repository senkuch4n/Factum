import type { TagPassThroughOptions, TagProps } from "primereact/tag";
import { cn } from "@/lib/utils";

type Severity = NonNullable<TagProps["severity"]>;

const TONE: Partial<Record<Severity, string>> = {
  success: "bg-fx-success-soft border-fx-success text-fx-success",
  info: "bg-fx-info-soft border-fx-info text-fx-info",
  warning: "bg-fx-warning-soft border-fx-warning text-fx-warning",
  danger: "bg-fx-danger-soft border-fx-danger text-fx-danger",
};

const NEUTRAL = "bg-fx-surface-3 border-fx-border-strong text-fx-text-2";

/**
 * Tag (estado de caso, estado de token). Fondo `*-soft` + borde y texto del
 * tono; siempre se usa con ícono + texto, nunca solo color.
 * `secondary`, `contrast` o sin severidad → neutro.
 */
export const tag: TagPassThroughOptions = {
  root: ({ props }) => ({
    className: cn(
      "inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-semibold whitespace-nowrap",
      (props.severity && TONE[props.severity]) ?? NEUTRAL,
    ),
  }),
  icon: { className: "w-3 h-3 shrink-0" },
  value: { className: "leading-none" },
};
