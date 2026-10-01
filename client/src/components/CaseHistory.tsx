"use client";

import { useState, useMemo, useEffect, useRef } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  Plus, Loader2, RefreshCw, FolderOpen,
  Search, X, LayoutGrid, List, Table,
  Shrimp,
} from "lucide-react";
import type { Case } from "@/lib/api";
import { cn } from "@/lib/utils";
import { useTheme } from "@/lib/theme";
import { formatDate } from "@/lib/format";
import { CaseCard } from "./CaseCard";
import { CaseGridCard } from "./CaseGridCard";
import { StatusBadge } from "./StatusBadge";
import { DottedGlowBackground } from "@/components/ui/dotted-glow-background";
import { Pagination } from "@/components/ui/pagination";
import { DateRangeCalendar } from "@/components/ui/date-range-calendar";
import { CyclingPlaceholderInput } from "@/components/ui/cycling-placeholder-input";
import { Tip } from "@/components/ui/tooltip";
import { DataTable, type Column, type SortState } from "@/components/ui/data-table";

/** Carátula del caso; los casos previos al informe pericial muestran el titular. */
const caratulaOf = (c: Case) => c.caratula || c.nombre_denunciante;

const SORT_ACCESSORS: Record<string, (c: Case) => string | number> = {
  causa:       (c) => c.nro_referencia.toLowerCase(),
  caratula:    (c) => caratulaOf(c).toLowerCase(),
  fecha:       (c) => new Date(c.created_at).getTime(),
  estado:      (c) => c.status,
};

const PAGE_SIZE = 8;

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
  const { isDark } = useTheme();
  const [query,       setQuery]       = useState("");
  const [dateFrom,    setDateFrom]    = useState("");
  const [dateTo,      setDateTo]      = useState("");
  const [view,        setView]        = useState<"list" | "table" | "grid">("list");
  const [tableSort,   setTableSort]   = useState<SortState>({ key: "fecha", dir: "desc" });

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
  const listTopRef = useRef<HTMLDivElement>(null);

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

  function changePage(next: number) {
    setPage(next);
    listTopRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  function clearFilters() {
    setQuery("");
    setDateFrom("");
    setDateTo("");
  }

  const caseColumns: Column<Case>[] = [
    {
      key: "causa", header: "N° de causa", sortable: true,
      cell: (c) => <span className="font-medium">{c.nro_referencia}</span>,
    },
    {
      key: "caratula", header: "Carátula", sortable: true,
      cell: (c) => (
        <div className="min-w-0">
          <div className="truncate">{caratulaOf(c)}</div>
          {c.caratula && (
            <div className="truncate text-[11px]" style={{ color: "var(--text-muted)" }}>{c.nombre_denunciante}</div>
          )}
        </div>
      ),
    },
    {
      key: "equipo", header: "Equipo",
      cell: (c) => <span style={{ color: "var(--text-secondary)" }}>{c.device.manufacturer} {c.device.model}</span>,
    },
    {
      key: "fecha", header: "Fecha", sortable: true,
      cell: (c) => <span style={{ color: "var(--text-secondary)" }}>{formatDate(c.created_at)}</span>,
    },
    {
      key: "estado", header: "Estado", sortable: true,
      cell: (c) => <StatusBadge status={c.status} />,
    },
    {
      key: "acciones", header: "", className: "text-right",
      cell: (c) =>
        c.status === "draft" ? (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onResume(c); }}
            className="btn-secondary btn-sm rounded-md whitespace-nowrap"
          >
            Retomar
          </button>
        ) : null,
    },
  ];

  return (
    <div className="space-y-4">

      {/* ── Title row ── */}
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="step-title">Mis inspecciones</h2>
          <p className="text-sm mt-0.5" aria-live="polite" style={{ color: "var(--text-secondary)" }}>
            {loading
              ? "Cargando…"
              : hasAnyFilter
                ? `${filtered.length} de ${cases.length} resultado${filtered.length !== 1 ? "s" : ""}`
                : cases.length === 0
                  ? "Aún no hay inspecciones"
                  : `${cases.length} inspección${cases.length !== 1 ? "es" : ""} registrada${cases.length !== 1 ? "s" : ""}`}
          </p>
        </div>
        <Tip label="Actualizar lista">
          <motion.button
            className="btn-ghost btn-sm w-8 h-8 p-0 rounded-lg flex-shrink-0"
            onClick={onRefresh}
            whileTap={{ rotate: 180, scale: 0.95 }}
            aria-label="Actualizar lista de inspecciones"
          >
            <RefreshCw className="w-3.5 h-3.5" />
          </motion.button>
        </Tip>
      </div>

      {/* ── Search + controls ── */}
      {!loading && cases.length > 0 && (
        <div className="space-y-2.5">
          {/* Search bar — placeholders rotativos (adaptado de Aceternity) */}
          <CyclingPlaceholderInput
            id="case-search"
            ariaLabel="Buscar inspecciones"
            value={query}
            onChange={setQuery}
            staticPlaceholder="Buscar por carátula, titular o IMEI…"
            placeholders={[
              "Buscar por número de causa…",
              "Buscar por carátula o partes…",
              "Buscar por titular o DNI…",
              "Buscar por equipo o IMEI…",
            ]}
          />

          {/* Filter controls row */}
          <div className="flex items-center gap-2">
            <DateRangeCalendar
              value={{ from: dateFrom ? fromYMD(dateFrom) : undefined, to: dateTo ? fromYMD(dateTo) : undefined }}
              onChange={(r) => { setDateFrom(r.from ? toYMD(r.from) : ""); setDateTo(r.to ? toYMD(r.to) : ""); }}
            />

            <AnimatePresence>
              {hasAnyFilter && (
                <motion.button
                  initial={{ opacity: 0, scale: 0.8, x: -8 }}
                  animate={{ opacity: 1, scale: 1, x: 0 }}
                  exit={{ opacity: 0, scale: 0.8, x: -8 }}
                  className="btn-sm flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium"
                  style={{ background: "rgba(239,68,68,0.08)", border: "1px solid rgba(239,68,68,0.2)", color: "#f87171" }}
                  onClick={clearFilters}
                  whileTap={{ scale: 0.96 }}
                >
                  <X className="w-3 h-3" /> Limpiar
                </motion.button>
              )}
            </AnimatePresence>

            <div className="flex-1" />

            <div
              className="flex items-center rounded-lg overflow-hidden"
              style={{ border: "1px solid var(--border)", background: "var(--bg-elevated)" }}
            >
              {([
                { v: "list",  label: "Vista lista",     Icon: List },
                { v: "table", label: "Vista tabla",     Icon: Table },
                { v: "grid",  label: "Vista cuadrícula", Icon: LayoutGrid },
              ] as const).map(({ v, label, Icon }) => (
                <Tip key={v} label={label}>
                  <motion.button
                    onClick={() => setView(v)}
                    className="w-8 h-8 flex items-center justify-center transition"
                    style={{ background: view === v ? "var(--blue)" : "transparent", color: view === v ? "#fff" : "var(--text-muted)" }}
                    whileTap={{ scale: 0.9 }}
                    aria-label={label}
                    aria-pressed={view === v}
                  >
                    <Icon className="w-3.5 h-3.5" />
                  </motion.button>
                </Tip>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* ── Content ── */}
      <div ref={listTopRef} className="scroll-mt-6" aria-hidden="true" />
      <AnimatePresence mode="wait">
        {loading ? (
          <motion.div key="loading" className="flex flex-col items-center gap-3 py-14" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
            <div className="relative w-10 h-10">
              <div className="absolute inset-0 rounded-full border" style={{ borderColor: "var(--border)" }} />
              <motion.div
                className="absolute inset-0 rounded-full border-t border-teal-400"
                animate={{ rotate: 360 }}
                transition={{ duration: 1, repeat: Infinity, ease: "linear" }}
              />
            </div>
            <p className="text-sm" style={{ color: "var(--text-secondary)" }}>Cargando historial…</p>
          </motion.div>

        ) : cases.length === 0 ? (
          <motion.div
            key="empty-all"
            className="relative overflow-hidden text-center py-14"
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
          >
            <DottedGlowBackground isDark={isDark} className="[mask-image:radial-gradient(ellipse_60%_70%_at_50%_45%,black_0%,transparent_75%)] [-webkit-mask-image:radial-gradient(ellipse_60%_70%_at_50%_45%,black_0%,transparent_75%)]" />

            <div className="relative z-10 space-y-4">
            <motion.div
              className="w-20 h-20 mx-auto rounded-3xl flex items-center justify-center"
              style={{ background: "rgba(45,212,191,0.06)", border: "1px solid var(--border)" }}
              animate={{ y: [0, -6, 0] }}
              transition={{ duration: 3, repeat: Infinity, ease: "easeInOut" }}
            >
              <Shrimp className="w-10 h-10 text-teal-400" strokeWidth={1.2} />
            </motion.div>
            <div>
              <p className="font-semibold" style={{ color: "var(--text-primary)" }}>Sin inspecciones aún</p>
              <p className="text-sm mt-1" style={{ color: "var(--text-muted)" }}>
                Hacé clic en "Nueva inspección" para empezar
              </p>
            </div>
            <motion.button
              className="btn-primary mx-auto"
              onClick={onNewCase}
              whileHover={{ scale: 1.03 }}
              whileTap={{ scale: 0.97 }}
            >
              <Plus className="w-4 h-4" /> Primera inspección
            </motion.button>
            </div>
          </motion.div>

        ) : filtered.length === 0 ? (
          <motion.div
            key="empty-filtered"
            className="text-center py-12 space-y-3"
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
          >
            <div className="w-14 h-14 mx-auto rounded-lg flex items-center justify-center"
              style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
            >
              <Search className="w-6 h-6" style={{ color: "var(--text-muted)" }} />
            </div>
            <div>
              <p className="font-semibold text-sm" style={{ color: "var(--text-primary)" }}>Sin resultados</p>
              <p className="text-xs mt-1" style={{ color: "var(--text-muted)" }}>
                Ninguna inspección coincide con los filtros actuales
              </p>
            </div>
            <button className="btn-secondary btn-sm mx-auto" onClick={clearFilters}>
              <X className="w-3 h-3" /> Limpiar filtros
            </button>
          </motion.div>

        ) : view === "list" ? (
          <motion.div key="list" className="space-y-2.5" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
            {paged.map((cas, i) => (
              <CaseCard key={cas.id} cas={cas} index={i} onResume={onResume} />
            ))}
          </motion.div>

        ) : view === "table" ? (
          <motion.div key="table" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
            <DataTable<Case>
              columns={caseColumns}
              rows={paged}
              rowKey={(c) => c.id}
              sort={tableSort}
              onSortChange={(s) => { setTableSort(s); setPage(1); }}
            />
          </motion.div>

        ) : (
          <motion.div
            key="grid"
            className="grid grid-cols-2 gap-3"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
          >
            {paged.map((cas, i) => (
              <CaseGridCard key={cas.id} cas={cas} index={i} onResume={onResume} />
            ))}
          </motion.div>
        )}
      </AnimatePresence>

      {!loading && filtered.length > PAGE_SIZE && (
        <div className="pt-2 space-y-2">
          <Pagination currentPage={page} totalPages={totalPages} onPageChange={changePage} />
          <p className="text-center text-[11px]" style={{ color: "var(--text-muted)" }}>
            {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, filtered.length)} de {filtered.length}
          </p>
        </div>
      )}
    </div>
  );
}
