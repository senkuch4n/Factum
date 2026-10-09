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
 *
 * D1-A — lenguaje "pill" del sistema para badges de estado: `bg-fx-*-soft`
 * (fondo suave) + `border-fx-*` + `text-fx-*` (texto saturado), el mismo
 * patrón que `AgentChip`. Es la regla para todos los badges de estado.
 *
 * D3-A — `secondary`, `contrast` o sin severidad caen en NEUTRAL
 * (`bg-fx-surface-3 border-fx-border-strong text-fx-text-2`), **a diferencia
 * de `Message`** (pt/message.ts), que mapea `secondary`/`contrast` al tono
 * info de forma intencional. La diferencia es deliberada: el Tag neutro es un
 * chip sin carga semántica; el Message secundario sigue siendo un aviso.
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
