import type { ButtonPassThroughOptions } from "primereact/button";
import { cn } from "@/lib/utils";
import { FOCUS_RING } from "./shared";

/**
 * Button. Variantes: default (CTA verde de acento), `severity="secondary"`,
 * `severity="danger"`, `outlined`, `text`, `link`; tamaños `small`/`large`;
 * estados hover, pressed, focus-visible, disabled y loading.
 * Las severidades que no tienen look propio (success/info/warning/help/
 * contrast) usan el secundario: el acento no se reusa como "éxito".
 */
type Tone = "accent" | "neutral" | "danger";

function toneOf(severity: string | undefined): Tone {
  if (!severity) return "accent";
  if (severity === "danger") return "danger";
  return "neutral";
}

const SOLID: Record<Tone, string> = {
  accent:
    "bg-fx-accent border-fx-accent text-fx-on-accent enabled:hover:bg-fx-accent-hover enabled:hover:border-fx-accent-hover enabled:active:bg-fx-accent-active enabled:active:border-fx-accent-active",
  neutral:
    "bg-fx-surface-2 border-fx-border-strong text-fx-text enabled:hover:bg-fx-surface-3 enabled:active:bg-fx-surface-3",
  danger:
    "bg-fx-danger-fill border-fx-danger-fill text-fx-on-danger enabled:hover:bg-fx-danger-fill-hover enabled:hover:border-fx-danger-fill-hover",
};

const OUTLINED: Record<Tone, string> = {
  accent: "bg-transparent border-fx-accent text-fx-accent-text enabled:hover:bg-fx-accent-soft",
  neutral: "bg-transparent border-fx-border-strong text-fx-text enabled:hover:bg-fx-surface-3",
  danger: "bg-transparent border-fx-danger text-fx-danger enabled:hover:bg-fx-danger-soft",
};

const TEXT: Record<Tone, string> = {
  accent: "bg-transparent border-transparent text-fx-accent-text enabled:hover:bg-fx-surface-3",
  neutral: "bg-transparent border-transparent text-fx-text-2 enabled:hover:bg-fx-surface-3 enabled:hover:text-fx-text",
  danger: "bg-transparent border-transparent text-fx-danger enabled:hover:bg-fx-danger-soft",
};

export const button: ButtonPassThroughOptions = {
  root: ({ props, context }) => {
    const tone = toneOf(props.severity);
    const iconOnly = props.label == null && props.children == null;
    const size = props.size ?? "normal";
    return {
      className: cn(
        "relative inline-flex items-center justify-center gap-2 align-bottom select-none whitespace-nowrap",
        "border rounded-fx-md font-bold cursor-pointer",
        "transition-[background-color,border-color,color,transform] duration-fx-fast ease-fx",
        "motion-safe:enabled:active:translate-y-px",
        FOCUS_RING,
        props.link
          ? "bg-transparent border-transparent text-fx-accent-text underline-offset-4 enabled:hover:underline"
          : props.text
            ? TEXT[tone]
            : props.outlined
              ? OUTLINED[tone]
              : SOLID[tone],
        iconOnly
          ? { "w-8 h-8 p-0": size === "small", "w-10 h-10 p-0": size === "normal", "w-12 h-12 p-0": size === "large" }
          : {
              "px-3.5 py-1.5 text-xs": size === "small",
              "px-5 py-2.5 text-fx-body-sm": size === "normal",
              "px-6 py-3 text-fx-body": size === "large",
            },
        props.loading
          ? "cursor-wait"
          : context.disabled && "opacity-50 cursor-not-allowed",
      ),
      "aria-busy": props.loading || undefined,
    };
  },
  label: ({ props }) => ({
    // Sin label, Prime renderiza un &nbsp; de relleno: en botones de ícono sobra.
    className: cn("leading-none", props.label == null && "hidden"),
  }),
  icon: { className: "shrink-0" },
  loadingIcon: { className: "shrink-0 w-4 h-4 animate-spin" },
  badge: {
    className:
      "inline-flex items-center justify-center min-w-[1.25rem] h-5 px-1 rounded-fx-pill bg-fx-surface-3 text-fx-text text-xs font-semibold",
  },
};
