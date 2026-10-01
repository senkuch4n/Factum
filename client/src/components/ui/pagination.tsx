"use client";

/**
 * Paginación — adaptada de scrollxui (docs/components/pagination) al design
 * system de factum:
 *  - un componente autocontenido con `onPageChange`, no primitivas + `<a href>`
 *  - tokens de factum; página activa igual que el "done" del StepIndicator
 *    (fondo `--text-primary`, texto `--bg-base`) para que el sistema se lea parejo
 *  - se quitó el pulso infinito del activo y el shimmer "shiny" (el DS no lleva
 *    animación decorativa); solo queda un `whileTap` sutil
 *  - <button> reales con focus ring y `aria-current`
 */

import { motion } from "framer-motion";
import { ChevronLeft, ChevronRight, MoreHorizontal } from "lucide-react";
import { cn } from "@/lib/utils";

interface Props {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  className?: string;
  /** máximo de números visibles antes de colapsar con "…" (default 7) */
  siblingWindow?: number;
}

/** [1, "…", 4, 5, 6, "…", 20] — primera y última siempre, actual ±1, "…" en los huecos */
function buildRange(current: number, total: number, cap: number): (number | "ellipsis")[] {
  if (total <= cap) return Array.from({ length: total }, (_, i) => i + 1);

  const range: (number | "ellipsis")[] = [1];
  const left = Math.max(2, current - 1);
  const right = Math.min(total - 1, current + 1);

  if (left > 2) range.push("ellipsis");
  for (let i = left; i <= right; i++) range.push(i);
  if (right < total - 1) range.push("ellipsis");

  range.push(total);
  return range;
}

export function Pagination({
  currentPage,
  totalPages,
  onPageChange,
  className,
  siblingWindow = 7,
}: Props) {
  if (totalPages <= 1) return null;

  const go = (p: number) => {
    const next = Math.min(Math.max(p, 1), totalPages);
    if (next !== currentPage) onPageChange(next);
  };

  const items = buildRange(currentPage, totalPages, siblingWindow);
  const atStart = currentPage <= 1;
  const atEnd = currentPage >= totalPages;

  return (
    <nav
      role="navigation"
      aria-label="Paginación"
      className={cn("flex w-full items-center justify-center gap-1.5", className)}
    >
      <motion.button
        type="button"
        onClick={() => go(currentPage - 1)}
        disabled={atStart}
        aria-label="Página anterior"
        whileTap={atStart ? undefined : { scale: 0.94 }}
        className="flex h-9 items-center gap-1 rounded-lg px-2.5 text-[13px] font-medium transition-colors disabled:pointer-events-none disabled:opacity-40"
        style={{ border: "1px solid var(--border-md)", color: "var(--text-secondary)" }}
        onMouseEnter={(e) => { if (!atStart) e.currentTarget.style.background = "var(--bg-elevated)"; }}
        onMouseLeave={(e) => { e.currentTarget.style.background = "transparent"; }}
      >
        <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        <span className="hidden sm:inline">Anterior</span>
      </motion.button>

      <ul className="flex items-center gap-1">
        {items.map((item, i) =>
          item === "ellipsis" ? (
            <li key={`e${i}`} className="flex h-9 w-9 items-center justify-center" aria-hidden="true">
              <MoreHorizontal className="h-4 w-4" style={{ color: "var(--text-muted)" }} />
            </li>
          ) : (
            <li key={item}>
              <motion.button
                type="button"
                onClick={() => go(item)}
                aria-label={`Página ${item}`}
                aria-current={item === currentPage ? "page" : undefined}
                whileTap={item === currentPage ? undefined : { scale: 0.94 }}
                className="flex h-9 w-9 items-center justify-center rounded-lg text-[13px] font-semibold transition-colors"
                style={
                  item === currentPage
                    ? { background: "var(--text-primary)", color: "var(--bg-base)", border: "1px solid var(--text-primary)" }
                    : { background: "transparent", color: "var(--text-secondary)", border: "1px solid var(--border-md)" }
                }
                onMouseEnter={(e) => { if (item !== currentPage) e.currentTarget.style.background = "var(--bg-elevated)"; }}
                onMouseLeave={(e) => { if (item !== currentPage) e.currentTarget.style.background = "transparent"; }}
              >
                {item}
              </motion.button>
            </li>
          ),
        )}
      </ul>

      <motion.button
        type="button"
        onClick={() => go(currentPage + 1)}
        disabled={atEnd}
        aria-label="Página siguiente"
        whileTap={atEnd ? undefined : { scale: 0.94 }}
        className="flex h-9 items-center gap-1 rounded-lg px-2.5 text-[13px] font-medium transition-colors disabled:pointer-events-none disabled:opacity-40"
        style={{ border: "1px solid var(--border-md)", color: "var(--text-secondary)" }}
        onMouseEnter={(e) => { if (!atEnd) e.currentTarget.style.background = "var(--bg-elevated)"; }}
        onMouseLeave={(e) => { e.currentTarget.style.background = "transparent"; }}
      >
        <span className="hidden sm:inline">Siguiente</span>
        <ChevronRight className="h-4 w-4" aria-hidden="true" />
      </motion.button>
    </nav>
  );
}

export default Pagination;
