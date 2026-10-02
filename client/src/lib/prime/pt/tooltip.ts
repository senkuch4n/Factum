import type { TooltipPassThroughOptions } from "primereact/tooltip";
import { cn } from "@/lib/utils";

/**
 * Tooltip (vía `FxTip`). Prime posiciona con `left/top` inline: el `absolute`
 * lo pone el pt en modo unstyled. El padding del root separa el globo del
 * disparador del lado en que aparece (no hay flecha).
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
