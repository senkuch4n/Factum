import type { PaginatorPassThroughMethodOptions, PaginatorPassThroughOptions } from "primereact/paginator";
import { cn } from "@/lib/utils";
import { FOCUS_RING } from "./shared";

/**
 * Paginator. Botones de 36px con números tabulares; la página actual usa el
 * acento (selección activa) y Prime le pone `aria-current`. Los `aria-label`
 * salen del locale `es`.
 */
function navButton({ context }: PaginatorPassThroughMethodOptions) {
  return {
    className: cn(
      "inline-flex items-center justify-center min-w-9 h-9 px-2 rounded-fx-md border-0",
      "text-fx-body-sm font-semibold tabular-nums cursor-pointer",
      "transition-colors duration-fx-fast ease-fx",
      FOCUS_RING,
      context?.active
        ? "bg-fx-accent text-fx-on-accent"
        : "bg-transparent text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text",
      context?.disabled && "opacity-40 cursor-not-allowed hover:bg-transparent hover:text-fx-text-2",
    ),
  };
}

const ICON = { className: "w-4 h-4" };

export const paginator: PaginatorPassThroughOptions = {
  root: { className: "flex flex-wrap items-center justify-center gap-1" },
  firstPageButton: navButton,
  prevPageButton: navButton,
  nextPageButton: navButton,
  lastPageButton: navButton,
  pageButton: navButton,
  firstPageIcon: ICON,
  prevPageIcon: ICON,
  nextPageIcon: ICON,
  lastPageIcon: ICON,
  pages: { className: "flex items-center gap-1" },
  current: { className: "px-2 text-fx-body-sm text-fx-text-3 tabular-nums" },
};
