"use client";

import { useEffect, useMemo, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { Loader2, Search } from "lucide-react";
import { api, ApiError, type Case, type Extraction } from "@/lib/api";
import { FxBanner } from "@/components/feedback/FxBanner";

interface Props {
  open: boolean;
  extractionId: string;
  currentCaseId: string | null;
  onClose: () => void;
  onLinked: (updated: Extraction) => void;
}

/** Diálogo "Asociar a un caso" (§10.5): lista de casos del usuario con búsqueda. */
export function LinkCaseDialog({ open, extractionId, currentCaseId, onClose, onLinked }: Props) {
  const [cases, setCases] = useState<Case[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState("");
  const [linkingId, setLinkingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setQuery("");
    setError(null);
    setLoading(true);
    api.listCases()
      .then(r => setCases(r.cases))
      .catch(() => setError("No se pudo cargar la lista de casos."))
      .finally(() => setLoading(false));
  }, [open]);

  const filtered = useMemo(() => {
    const list = cases ?? [];
    const q = query.trim().toLowerCase();
    if (!q) return list;
    return list.filter(c =>
      c.nro_referencia?.toLowerCase().includes(q) || c.caratula?.toLowerCase().includes(q));
  }, [cases, query]);

  async function link(caseId: string) {
    setLinkingId(caseId);
    setError(null);
    try {
      const updated = await api.linkExtraction(extractionId, caseId);
      onLinked(updated);
      onClose();
    } catch (e) {
      setError(describeLinkError(e));
    } finally {
      setLinkingId(null);
    }
  }

  return (
    <Dialog
      visible={open}
      onHide={onClose}
      draggable={false}
      resizable={false}
      header="Asociar a un caso"
      pt={{ root: { className: "w-[min(32rem,100%)]" } }}
    >
      <div className="space-y-3">
        <FxBanner tone="info" role="note">No modifica informes ya generados.</FxBanner>

        <span className="relative block">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fx-text-3" aria-hidden="true" />
          <InputText
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Buscar por número de causa o carátula"
            aria-label="Buscar por número de causa o carátula"
            className="w-full pl-9"
          />
        </span>

        {error && <FxBanner tone="error">{error}</FxBanner>}

        {loading ? (
          <div role="status" className="flex justify-center py-8">
            <Loader2 className="h-5 w-5 animate-spin text-fx-text-3" aria-hidden="true" />
            <span className="sr-only">Cargando casos…</span>
          </div>
        ) : filtered.length === 0 ? (
          <p className="m-0 py-8 text-center text-fx-body-sm text-fx-text-2">No hay casos que coincidan.</p>
        ) : (
          <ul className="m-0 max-h-72 list-none space-y-1.5 overflow-y-auto p-0">
            {filtered.map(c => {
              const isCurrent = c.id === currentCaseId;
              return (
                <li key={c.id}>
                  <button
                    type="button"
                    onClick={() => { if (!isCurrent) void link(c.id); }}
                    disabled={isCurrent || linkingId !== null}
                    className="fx-card fx-card-interactive flex min-h-11 w-full items-center gap-3 p-3 text-left disabled:opacity-60"
                  >
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-fx-body-sm font-semibold text-fx-text">
                        Causa {c.nro_referencia || "—"}
                      </span>
                      {c.caratula && <span className="mt-0.5 block truncate text-xs text-fx-text-3">{c.caratula}</span>}
                    </span>
                    {isCurrent
                      ? <span className="shrink-0 text-xs font-semibold text-fx-accent-text">Asociado</span>
                      : linkingId === c.id
                        ? <Loader2 className="h-4 w-4 shrink-0 animate-spin text-fx-text-3" aria-hidden="true" />
                        : null}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </Dialog>
  );
}

function describeLinkError(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.code === "extraction_already_linked") return "La extracción ya está asociada a otro caso. Desasociala primero.";
    if (e.code === "extraction_not_finished") return "La extracción todavía no terminó.";
    if (e.status === 404) return "No se encontró el caso o la extracción.";
  }
  return "No se pudo asociar la extracción.";
}
