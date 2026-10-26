"use client";

import { useCallback, useEffect, useState } from "react";
import { Button } from "primereact/button";
import { AlertTriangle, ChevronDown, ChevronRight, Loader2 } from "lucide-react";
import { agent, type AgentExtractionErrorsPage } from "@/lib/agent";
import { categoryLabel } from "@/lib/extraction";

const PAGE = 100;

/**
 * Errores por archivo, desplegables y paginados (§10.5). Se leen de
 * `GET /extractions/{id}/errors`. Solo se consultan al abrir el detalle.
 */
export function FileErrorsList({ id, total }: { id: string; total: number }) {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<AgentExtractionErrorsPage["items"]>([]);
  const [loaded, setLoaded] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);

  const loadMore = useCallback(async (offset: number) => {
    setLoading(true);
    setError(false);
    try {
      const page = await agent.getExtractionErrors(id, offset, PAGE);
      setItems(prev => (offset === 0 ? page.items : [...prev, ...page.items]));
      setLoaded(offset + page.items.length);
    } catch {
      setError(true);
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => { if (open && items.length === 0) void loadMore(0); }, [open, items.length, loadMore]);

  if (total <= 0) return null;

  return (
    <section aria-labelledby="errors-title" className="space-y-2">
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        aria-expanded={open}
        className="flex min-h-11 w-full items-center gap-2 rounded-fx-md px-2 py-2 text-left hover:bg-fx-surface-2 fx-focus-ring"
      >
        {open ? <ChevronDown className="h-4 w-4 shrink-0 text-fx-text-3" aria-hidden="true" />
          : <ChevronRight className="h-4 w-4 shrink-0 text-fx-text-3" aria-hidden="true" />}
        <AlertTriangle className="h-4 w-4 shrink-0 text-fx-warning" aria-hidden="true" />
        <span id="errors-title" className="text-fx-body-sm font-semibold text-fx-text">
          {total === 1 ? "1 archivo con error" : `${total.toLocaleString("es-AR")} archivos con error`}
        </span>
      </button>

      {open && (
        <div className="space-y-2">
          {error && <p className="m-0 text-fx-body-sm text-fx-danger">No se pudieron leer los errores.</p>}
          <ul className="m-0 list-none space-y-1.5 p-0">
            {items.map((e, i) => (
              <li key={`${e.device_path}-${i}`} className="rounded-fx-md border border-fx-border bg-fx-surface-2 px-3 py-2">
                <p className="m-0 break-all font-mono text-xs text-fx-text" translate="no">{e.device_path}</p>
                <p className="m-0 mt-0.5 text-xs text-fx-text-3">
                  {categoryLabel(e.category)} · {e.reason}
                </p>
              </li>
            ))}
          </ul>
          {loading && (
            <p className="m-0 flex items-center gap-2 text-fx-body-sm text-fx-text-2">
              <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> Cargando…
            </p>
          )}
          {!loading && loaded < total && (
            <Button severity="secondary" size="small" label="Ver más" onClick={() => loadMore(loaded)} className="min-h-11" />
          )}
        </div>
      )}
    </section>
  );
}
