import type { ProgressBarPassThroughOptions } from "primereact/progressbar";

/**
 * ProgressBar (riel de pasos y franja compacta del wizard). Solo el modo
 * determinado: Prime pone `role="progressbar"` y `aria-valuemin/max/now`;
 * `aria-label` y `aria-valuetext` llegan por props (se esparcen en el root).
 * El ancho del valor lo pone Prime inline.
 */
export const progressbar: ProgressBarPassThroughOptions = {
  root: { className: "relative h-1.5 w-full overflow-hidden rounded-full bg-fx-surface-3" },
  value: {
    className:
      "absolute inset-y-0 left-0 rounded-full bg-fx-accent transition-[width] duration-fx-slow ease-fx motion-reduce:transition-none",
  },
  label: { className: "hidden" },
  container: { className: "hidden" },
};
