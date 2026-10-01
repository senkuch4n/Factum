"use client";

/**
 * Tabla ordenable — adaptada de scrollxui (docs/components/table).
 *
 * El original es un `DataTable` sobre `@tanstack/react-table` +
 * `@radix-ui/react-dropdown-menu` + `@scrollxui/button`, con animaciones de
 * stagger por fila. Acá se reconstruye a mano (sin dependencias nuevas): solo
 * ordenamiento por columna al hacer click en el header, con tokens de factum.
 * La paginación/búsqueda ya las resuelve `CaseHistory` por fuera.
 */

import { useMemo, useState } from "react";
import { ChevronDown, ChevronUp, ChevronsUpDown } from "lucide-react";
import { cn } from "@/lib/utils";

export interface Column<T> {
  key: string;
  header: React.ReactNode;
  /** celda; si se omite se usa `row[key]` */
  cell?: (row: T) => React.ReactNode;
  /** valor para ordenar; si se omite y es sortable, se usa `row[key]` */
  sortValue?: (row: T) => string | number;
  sortable?: boolean;
  className?: string;
  headerClassName?: string;
}

export type SortState = { key: string; dir: "asc" | "desc" } | null;

function nextSort(prev: SortState, key: string): SortState {
  if (!prev || prev.key !== key) return { key, dir: "asc" };
  if (prev.dir === "asc") return { key, dir: "desc" };
  return null; // tercer click: sin orden
}

interface Props<T> {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  /** modo no controlado: orden inicial (DataTable ordena `rows` internamente) */
  initialSort?: SortState;
  /** modo controlado: el padre ordena las filas y solo refleja el estado */
  sort?: SortState;
  onSortChange?: (sort: SortState) => void;
  className?: string;
  emptyMessage?: string;
}

export function DataTable<T>({
  columns,
  rows,
  rowKey,
  onRowClick,
  initialSort = null,
  sort: controlledSort,
  onSortChange,
  className,
  emptyMessage = "Sin resultados",
}: Props<T>) {
  const controlled = onSortChange !== undefined;
  const [innerSort, setInnerSort] = useState<SortState>(initialSort);
  const sort = controlled ? controlledSort ?? null : innerSort;

  const sorted = useMemo(() => {
    if (controlled || !sort) return rows; // en controlado, el padre ya ordenó
    const col = columns.find((c) => c.key === sort.key);
    if (!col) return rows;
    const get = col.sortValue ?? ((r: T) => (r as Record<string, unknown>)[col.key] as string | number);
    const factor = sort.dir === "asc" ? 1 : -1;
    return [...rows].sort((a, b) => {
      const va = get(a);
      const vb = get(b);
      if (va < vb) return -1 * factor;
      if (va > vb) return 1 * factor;
      return 0;
    });
  }, [rows, sort, columns, controlled]);

  function toggleSort(key: string) {
    if (controlled) onSortChange!(nextSort(sort, key));
    else setInnerSort((prev) => nextSort(prev, key));
  }

  return (
    <div className={cn("w-full overflow-x-auto rounded-lg", className)} style={{ border: "1px solid var(--border)" }}>
      <table className="w-full text-left text-[13px]">
        <thead>
          <tr style={{ borderBottom: "1px solid var(--border)", background: "var(--bg-elevated)" }}>
            {columns.map((col) => {
              const active = sort?.key === col.key;
              return (
                <th
                  key={col.key}
                  scope="col"
                  aria-sort={active ? (sort!.dir === "asc" ? "ascending" : "descending") : "none"}
                  className={cn("px-3 py-2.5 font-medium whitespace-nowrap", col.headerClassName)}
                  style={{ color: "var(--text-muted)" }}
                >
                  {col.sortable ? (
                    <button
                      type="button"
                      onClick={() => toggleSort(col.key)}
                      className="inline-flex items-center gap-1 transition-colors hover:text-[var(--text-primary)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--blue-lg)] rounded"
                      style={{ color: active ? "var(--text-primary)" : undefined }}
                    >
                      {col.header}
                      {active ? (
                        sort!.dir === "asc" ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />
                      ) : (
                        <ChevronsUpDown className="h-3.5 w-3.5 opacity-50" />
                      )}
                    </button>
                  ) : (
                    col.header
                  )}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {sorted.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="px-3 py-10 text-center" style={{ color: "var(--text-muted)" }}>
                {emptyMessage}
              </td>
            </tr>
          ) : (
            sorted.map((row) => (
              <tr
                key={rowKey(row)}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                className={cn(
                  "transition-colors",
                  onRowClick && "cursor-pointer",
                )}
                style={{ borderBottom: "1px solid var(--border)" }}
                onMouseEnter={(e) => { e.currentTarget.style.background = "var(--bg-elevated)"; }}
                onMouseLeave={(e) => { e.currentTarget.style.background = "transparent"; }}
              >
                {columns.map((col) => (
                  <td key={col.key} className={cn("px-3 py-2.5 align-middle", col.className)} style={{ color: "var(--text-primary)" }}>
                    {col.cell ? col.cell(row) : String((row as Record<string, unknown>)[col.key] ?? "")}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

export default DataTable;
