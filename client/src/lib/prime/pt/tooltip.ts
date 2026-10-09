import type { TooltipPassThroughOptions } from "primereact/tooltip";
import { cn } from "@/lib/utils";

/**
 * Tooltip (vía `FxTip`). Prime posiciona con `left/top` inline: el `absolute`
 * lo pone el pt en modo unstyled. El padding del root separa el globo del
 * disparador del lado en que aparece (no hay flecha).
 *
 * D2-C — el globo usa `bg-fx-surface-3`, un único valor para los dos
 * contextos. `FxTip` renderiza el `<Tooltip>` como hermano inline del
 * disparador (Prime monta con `appendTo: null` → el Portal renderiza en su
 * lugar, no en `document.body`), así que cuando el disparador vive dentro de
 * un contenedor `.fx-nav-pill` el globo también. Ahí `globals.css` remapea
 * `--fx-surface-3 → --fx-nav-pill-hover`, por lo que el tooltip se adapta solo
 * sobre la barra oscura sin ramificar por posición.
 *
 * Observación (pendiente de otra HU, no se corrige acá): dentro de
 * `.fx-nav-pill` **no** se remapea `--fx-border-strong`, que es el borde del
 * globo; conserva el valor del tema oscuro. Se lee (borde oscuro sobre
 * superficie oscura) y no viola T12, pero queda anotado como posible
 * observación de contraste a futuro.
 */
export const tooltip: TooltipPassThroughOptions = {
  root: ({ context }) => ({
    className: cn(
      "absolute z-[1100] max-w-[220px] pointer-events-none",
      context?.left || context?.right ? "px-1.5 py-0" : "py-1.5 px-0",
    ),
  }),
  text: {
    className:
      "px-2 py-1 text-xs font-medium bg-fx-surface-3 text-fx-text border border-fx-border-strong rounded-fx-sm shadow-fx-2 break-words",
  },
  arrow: { className: "hidden" },
};
