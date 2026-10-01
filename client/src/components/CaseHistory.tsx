"use client";

import { useState, useMemo, useEffect, useRef } from "react";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { Calendar } from "primereact/calendar";
import { SelectButton } from "primereact/selectbutton";
import { DataTable } from "primereact/datatable";
import { Column } from "primereact/column";
import { Paginator } from "primereact/paginator";
import {
  Plus, Loader2, RefreshCw, Inbox, Search, X, LayoutGrid, List, Table,
  CalendarRange, ChevronUp, ChevronDown, ChevronsUpDown, type LucideIcon,
} from "lucide-react";
import type { Case } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate } from "@/lib/format";
import { FxTip } from "@/components/overlay/FxTip";
import { CaseCard } from "./CaseCard";
import { CaseGridCard } from "./CaseGridCard";
import { StatusBadge } from "./StatusBadge";

/** Carátula del caso; los casos previos al informe pericial muestran el titular. */
const caratulaOf = (c: Case) => c.caratula || c.nombre_denunciante;

const SORT_ACCESSORS: Record<string, (c: Case) => string | number> = {
  causa:       (c) => c.nro_referencia.toLowerCase(),
  caratula:    (c) => caratulaOf(c).toLowerCase(),
  fecha:       (c) => new Date(c.created_at).getTime(),
  estado:      (c) => c.status,
};

type SortState = { key: string; dir: "asc" | "desc" };
type View = "list" | "table" | "grid";

const VIEWS: { value: View; label: string; Icon: LucideIcon }[] = [
  { value: "list",  label: "Lista",      Icon: List },
  { value: "table", label: "Tabla",      Icon: Table },
  { value: "grid",  label: "Cuadrícula", Icon: LayoutGrid },
];

const PAGE_SIZE = 8;

const FADE_IN = "motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]";

// Puente string "YYYY-MM-DD" (lo que filtra/guarda CaseHistory) <-> Date (lo
// que maneja el calendario). new Date("YYYY-MM-DD") parsea en UTC y puede
// correr el día según timezone — por eso se arma con el constructor local.
function toYMD(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
function fromYMD(s: string): Date {
  const [y, m, d] = s.split("-").map(Number);
  return new Date(y, m - 1, d);
}

interface Props {
  cases: Case[];
  loading: boolean;
  onNewCase: () => void;
  onRefresh: () => void;
  onResume: (cas: Case) => void;
}

export function CaseHistory({ cases, loading, onNewCase, onRefresh, onResume }: Props) {
  const [query,       setQuery]       = useState("");
  const [dateFrom,    setDateFrom]    = useState("");
  const [dateTo,      setDateTo]      = useState("");
  const [view,        setView]        = useState<View>("list");
  const [tableSort,   setTableSort]   = useState<SortState>({ key: "fecha", dir: "desc" });
  const searchRef = useRef<HTMLInputElement>(null);

  const hasDateFilter = !!dateFrom || !!dateTo;
  const hasQuery      = query.trim().length > 0;
  const hasAnyFilter  = hasQuery || hasDateFilter;

  const filtered = useMemo(() => {
    let result = cases;

    if (hasQuery) {
      const q = query.toLowerCase().trim();
      const has = (v: string | null | undefined) => !!v && v.toLowerCase().includes(q);
      result = result.filter(c =>
        has(c.nro_referencia) ||
        has(c.caratula) ||
        has(c.parte_denunciante) ||
        has(c.parte_denunciada) ||
        has(c.nombre_denunciante) ||
        has(c.dni_denunciante) ||
        c.device.manufacturer.toLowerCase().includes(q) ||
        c.device.model.toLowerCase().includes(q) ||
        (c.device.imei && c.device.imei.includes(q))
      );
    }

    if (dateFrom) {
      const from = new Date(dateFrom + "T00:00:00");
      result = result.filter(c => new Date(c.created_at) >= from);
    }

    if (dateTo) {
      const to = new Date(dateTo + "T23:59:59");
      result = result.filter(c => new Date(c.created_at) <= to);
    }

    return result;
  }, [cases, query, dateFrom, dateTo, hasQuery]);

  // ── Paginación ─────────────────────────────────────────────────────
  const [page, setPage] = useState(1);
  const resultsRef = useRef<HTMLDivElement>(null);

  // La vista tabla ordena el conjunto completo antes de paginar (así el orden es
  // global, no por página). Lista y grilla mantienen el orden que llega del server.
  const displayRows = useMemo(() => {
    if (view !== "table" || !tableSort) return filtered;
    const acc = SORT_ACCESSORS[tableSort.key];
    if (!acc) return filtered;
    const f = tableSort.dir === "asc" ? 1 : -1;
    return [...filtered].sort((a, b) => {
      const va = acc(a), vb = acc(b);
      return va < vb ? -f : va > vb ? f : 0;
    });
  }, [filtered, view, tableSort]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const paged = useMemo(
    () => displayRows.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE),
    [displayRows, page],
  );

  // Al cambiar filtros/búsqueda el conjunto se achica: volvemos a página 1.
  useEffect(() => { setPage(1); }, [query, dateFrom, dateTo]);
  // Si la página quedó fuera de rango (p. ej. borraste casos), la acotamos.
  useEffect(() => { if (page > totalPages) setPage(totalPages); }, [page, totalPages]);

  // Al paginar, el scroll y el foco van al inicio de los resultados (el
  // paginador queda abajo y, si no, el lector seguiría al final de la lista).
  function changePage(next: number) {
    setPage(next);
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    resultsRef.current?.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "start" });
    resultsRef.current?.focus({ preventScroll: true });
  }

  function clearFilters() {
    setQuery("");
    setDateFrom("");
    setDateTo("");
    // El botón que disparó esto desaparece: el foco vuelve al buscador.
    searchRef.current?.focus();
  }

  function clearSearch() {
    setQuery("");
    searchRef.current?.focus();
  }

  const range: (Date | null)[] | null = hasDateFilter
    ? [dateFrom ? fromYMD(dateFrom) : null, dateTo ? fromYMD(dateTo) : null]
    : null;

  const countText = loading
    ? "Cargando…"
    : hasAnyFilter
      ? `${filtered.length} de ${cases.length} resultado${filtered.length !== 1 ? "s" : ""}`
      : cases.length === 0
        ? "Aún no hay inspecciones"
        : `${cases.length} inspección${cases.length !== 1 ? "es" : ""} registrada${cases.length !== 1 ? "s" : ""}`;

  return (
    <section aria-labelledby="case-history-title" className="space-y-4">

      {/* ── Encabezado ── */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 id="case-history-title" className="text-fx-h2 text-fx-text">Mis inspecciones</h2>
          <p aria-live="polite" className="mt-1 text-fx-body-sm text-fx-text-2">{countText}</p>
        </div>
        <FxTip label="Actualizar lista">
          <Button
            text
            severity="secondary"
            icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
            aria-label="Actualizar lista de inspecciones"
            onClick={onRefresh}
          />
        </FxTip>
      </div>

      {/* ── Filtros ── */}
      {!loading && cases.length > 0 && (
        <div className="flex flex-col gap-2.5 sm:flex-row sm:flex-wrap sm:items-center">
          <div className="group relative w-full sm:flex-1 sm:min-w-[16rem]">
            <label htmlFor="case-search" className="sr-only">Buscar inspecciones</label>
            <Search
              className="pointer-events-none absolute left-3.5 top-1/2 z-[1] h-4 w-4 -translate-y-1/2 text-fx-text-3 transition-colors duration-fx-fast ease-fx group-focus-within:text-fx-accent-text"
              aria-hidden="true"
            />
            <InputText
              ref={searchRef}
              id="case-search"
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Buscar por causa, carátula, partes, titular, DNI, equipo o IMEI…"
              autoComplete="off"
              className="!pl-10 !pr-10 [&::-webkit-search-cancel-button]:appearance-none"
            />
            {query && (
              <button
                type="button"
                onClick={clearSearch}
                aria-label="Limpiar búsqueda"
                className="absolute right-1 top-1/2 inline-flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-fx-md text-fx-text-3 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            )}
          </div>

          <div className="w-full sm:w-auto">
            <label htmlFor="case-date-range" className="sr-only">Filtrar por fecha de creación</label>
            <Calendar
              inputId="case-date-range"
              selectionMode="range"
              value={range}
              onChange={(e) => {
                const [f, t] = e.value ?? [];
                setDateFrom(f ? toYMD(f) : "");
                setDateTo(t ? toYMD(t) : "");
              }}
              readOnlyInput
              showIcon
              icon={<CalendarRange className="h-4 w-4" aria-hidden="true" />}
              showButtonBar
              hideOnRangeSelection
              dateFormat="dd/mm/yy"
              placeholder="Desde – hasta"
            />
          </div>

          {hasAnyFilter && (
            <Button
              text
              size="small"
              icon={<X className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Limpiar filtros"
              onClick={clearFilters}
              className={FADE_IN}
            />
          )}

          <SelectButton
            value={view}
            onChange={(e) => e.value && setView(e.value as View)}
            allowEmpty={false}
            options={VIEWS}
            optionLabel="label"
            optionValue="value"
            itemTemplate={(o: (typeof VIEWS)[number]) => (
              <>
                <o.Icon className="h-4 w-4" aria-hidden="true" />
                <span className="sr-only sm:not-sr-only">{o.label}</span>
              </>
            )}
            pt={{ root: { "aria-label": "Vista del historial", className: "self-start sm:ml-auto" } }}
          />
        </div>
      )}

      {/* ── Resultados ── */}
      <div
        ref={resultsRef}
        tabIndex={-1}
        aria-label="Resultados de inspecciones"
        className="scroll-mt-6 outline-none"
      >
        {loading ? (
          <div role="status" className="flex flex-col items-center gap-3 py-14">
            <Loader2 className="h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
            <p className="text-fx-body-sm text-fx-text-2">Cargando historial…</p>
          </div>

        ) : cases.length === 0 ? (
          <div className="fx-card border-dashed border-fx-border-strong py-14 px-6 text-center space-y-4">
            <span className="mx-auto flex h-20 w-20 items-center justify-center rounded-fx-xl bg-fx-surface-2">
              <Inbox className="h-10 w-10 text-fx-text-3" strokeWidth={1.5} aria-hidden="true" />
            </span>
            <div>
              <p className="text-fx-h3 text-fx-text">Sin inspecciones aún</p>
              <p className="mt-1 text-fx-body-sm text-fx-text-2">Hacé clic en &quot;Nueva inspección&quot; para empezar</p>
            </div>
            <Button
              icon={<Plus className="h-4 w-4" aria-hidden="true" />}
              label="Primera inspección"
              onClick={onNewCase}
              className="min-h-11"
            />
          </div>

        ) : filtered.length === 0 ? (
          <div className={cn("py-12 text-center space-y-3", FADE_IN)}>
            <span className="mx-auto flex h-14 w-14 items-center justify-center rounded-fx-lg bg-fx-surface-2">
              <Search className="h-6 w-6 text-fx-text-3" aria-hidden="true" />
            </span>
            <div>
              <p className="text-fx-body-sm font-semibold text-fx-text">Sin resultados</p>
              <p className="mt-1 text-xs text-fx-text-2">Ninguna inspección coincide con los filtros actuales</p>
            </div>
            <Button
              severity="secondary"
              size="small"
              icon={<X className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Limpiar filtros"
              onClick={clearFilters}
            />
          </div>

        ) : view === "list" ? (
          <ul key="list" className={cn("space-y-2.5", FADE_IN)}>
            {paged.map((cas) => (
              <CaseCard key={cas.id} cas={cas} onResume={onResume} />
            ))}
          </ul>

        ) : view === "table" ? (
          <div key="table" className={cn("fx-card overflow-hidden", FADE_IN)}>
            <DataTable
              value={paged}
              dataKey="id"
              lazy
              rowHover
              sortField={tableSort.key}
              sortOrder={tableSort.dir === "asc" ? 1 : -1}
              onSort={(e) => {
                setTableSort({ key: e.sortField, dir: e.sortOrder === 1 ? "asc" : "desc" });
                setPage(1);
              }}
              removableSort={false}
              sortIcon={(opts: { sorted?: boolean; sortOrder?: number | null }) =>
                opts.sorted
                  ? opts.sortOrder === 1
                    ? <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" />
                    : <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
                  : <ChevronsUpDown className="h-3.5 w-3.5" aria-hidden="true" />
              }
              tableStyle={{ minWidth: "40rem" }}
              pt={{ table: { "aria-label": "Inspecciones" } }}
            >
              <Column
                columnKey="causa"
                field="causa"
                sortField="causa"
                sortable
                header="N° de causa"
                body={(c: Case) => <span className="font-medium">{c.nro_referencia}</span>}
              />
              <Column
                columnKey="caratula"
                field="caratula"
                sortField="caratula"
                sortable
                header="Carátula"
                body={(c: Case) => (
                  <div className="min-w-0 max-w-[18rem]">
                    <div className="truncate">{caratulaOf(c)}</div>
                    {c.caratula && <div className="truncate text-xs text-fx-text-3">{c.nombre_denunciante}</div>}
                  </div>
                )}
              />
              <Column
                columnKey="equipo"
                header="Equipo"
                body={(c: Case) => <span className="text-fx-text-2">{c.device.manufacturer} {c.device.model}</span>}
              />
              <Column
                columnKey="fecha"
                field="fecha"
                sortField="fecha"
                sortable
                header="Fecha"
                body={(c: Case) => <span className="text-fx-text-2 tabular-nums">{formatDate(c.created_at)}</span>}
              />
              <Column
                columnKey="estado"
                field="estado"
                sortField="estado"
                sortable
                header="Estado"
                body={(c: Case) => <StatusBadge status={c.status} />}
              />
              <Column
                columnKey="acciones"
                header={<span className="sr-only">Acciones</span>}
                bodyClassName="text-right"
                body={(c: Case) =>
                  c.status === "draft" ? (
                    <Button severity="secondary" size="small" label="Retomar" onClick={() => onResume(c)} />
                  ) : null
                }
              />
            </DataTable>
          </div>

        ) : (
          <ul key="grid" className={cn("grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4", FADE_IN)}>
            {paged.map((cas) => (
              <CaseGridCard key={cas.id} cas={cas} onResume={onResume} />
            ))}
          </ul>
        )}
      </div>

      {!loading && filtered.length > PAGE_SIZE && (
        <nav aria-label="Paginación de inspecciones" className="pt-2 space-y-2">
          <Paginator
            first={(page - 1) * PAGE_SIZE}
            rows={PAGE_SIZE}
            totalRecords={filtered.length}
            onPageChange={(e) => changePage(e.page + 1)}
            template="PrevPageLink PageLinks NextPageLink"
            pageLinkSize={5}
          />
          <p className="text-center text-xs text-fx-text-3 tabular-nums">
            {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, filtered.length)} de {filtered.length}
          </p>
        </nav>
      )}
    </section>
  );
}
