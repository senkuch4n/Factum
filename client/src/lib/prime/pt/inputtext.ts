import type { InputTextPassThroughOptions } from "primereact/inputtext";
import { cn } from "@/lib/utils";

/**
 * InputText. Borde `--fx-border-strong` (límite de control, ≥ 3:1), foco
 * estándar + borde de acento, `invalid` (prop o aria-invalid) en peligro y
 * deshabilitado sobre superficie 1.
 */
export const inputtext: InputTextPassThroughOptions = {
  root: ({ props, context }) => ({
    className: cn(
      "m-0 w-full appearance-none font-sans",
      "rounded-fx-md border px-3 py-2.5 text-fx-body-sm",
      "transition-[border-color,background-color] duration-fx-fast ease-fx",
      "placeholder:text-fx-text-3",
      "outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fx-focus",
      context?.disabled
        ? "bg-fx-surface-1 text-fx-text-disabled border-fx-border cursor-not-allowed placeholder:text-fx-text-disabled"
        : cn(
            "bg-fx-surface-2 text-fx-text",
            props.invalid
              ? "border-fx-danger"
              : "border-fx-border-strong hover:border-fx-text-3 focus-visible:border-fx-accent aria-[invalid=true]:border-fx-danger",
          ),
      {
        "px-2.5 py-1.5 text-xs": props.size === "small",
        "px-4 py-3 text-fx-body": props.size === "large",
      },
    ),
  }),
};
