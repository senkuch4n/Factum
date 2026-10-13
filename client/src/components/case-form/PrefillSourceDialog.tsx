"use client";

import { useMemo, useRef, useState } from "react";
import { Dialog } from "primereact/dialog";
import { InputText } from "primereact/inputtext";
import { Tag } from "primereact/tag";
import { Search } from "lucide-react";
import { cn } from "@/lib/utils";
import { formatDate } from "@/lib/format";
import { normalizeCatalogKey } from "@/lib/catalogs";
import { normalizeExpediente } from "@/lib/case-summary";
import type { Case } from "@/types";

const MAX_SHOWN = 50;

function matches(c: Case, tokens: string[], exp: string): boolean {
  if (tokens.length === 0) return true;
  const hay = normalizeCatalogKey(`${c.nro_referencia ?? ""} ${c.caratula ?? ""} ${c.nombre_tribunal ?? ""}`);
  if (tokens.every(t => hay.includes(t))) return true;
  return exp.length > 0 && normalizeExpediente(c.nro_referencia ?? "").includes(exp);
}

/**
 * Buscador de la causa de origen de la precarga (§6.10) sobre el historial ya
 * cargado. Vive fuera del `<form>` (D7 #52). Al cerrar, el foco vuelve al
 * botón que lo abrió (`returnFocusId`).
 */
export function PrefillSourceDialog({ visible, sources, currentId, returnFocusId, onPick, onHide }: {
  visible: boolean;
  /** `prefillSources(historyCases)` sin el caso que se está editando. */
  sources: Case[];
  /** Causa de origen activa (lleva "Actual"). */
  currentId: string | null;
  returnFocusId: string | null;
  /** Solo aplica la elección; el diálogo se cierra solo. */
  onPick: (cas: Case) => void;
  onHide: () => void;
}) {
  const [query, setQuery] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const filtered = useMemo(() => {
    const key = normalizeCatalogKey(query);
    const tokens = key ? key.split(" ") : [];
    const exp = normalizeExpediente(query);
    return sources.filter(c => matches(c, tokens, exp));
  }, [sources, query]);
  const shown = filtered.slice(0, MAX_SHOWN);

  // Prime no siempre devuelve el foco al disparador: lo hacemos después de la salida (≈ --fx-dur-base).
  function close() {
    onHide();
    setQuery("");
    if (returnFocusId) window.setTimeout(() => document.getElementById(returnFocusId)?.focus(), 220);
  }

  return (
    <Dialog
      visible={visible}
      onHide={close}
      onShow={() => inputRef.current?.focus()}
      header="Elegir causa de origen"
      draggable={false}
      resizable={false}
      dismissableMask
    >
      <p className="m-0 mb-3">
        Se copian destinatario, organismo, sala, integrantes, tipo de causa y proponente. Nunca el expediente, la carátula ni las partes.
      </p>
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fx-text-3" aria-hidden="true" />
        <InputText
          ref={inputRef}
          type="search"
          name="prefill-search"
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={e => { if (e.key === "Enter") e.preventDefault(); }}
          aria-label="Buscar por expediente, carátula o destinatario"
          placeholder="Expediente, carátula o destinatario…"
          autoComplete="off"
          spellCheck={false}
          pt={{ root: { className: "w-full pl-9" } }}
        />
      </div>
      <p role="status" className="m-0 mt-2 text-xs text-fx-text-3 tabular-nums">
        {filtered.length === 1 ? "1 causa" : `${filtered.length} causas`}
      </p>

      {shown.length === 0 ? (
        <p className="m-0 py-6 text-center text-fx-text-2">No hay causas que coincidan.</p>
      ) : (
        <ul className="m-0 mt-2 flex list-none flex-col gap-1 p-0">
          {shown.map(c => {
            const current = c.id === currentId;
            const nro = c.nro_referencia?.trim();
            return (
              <li key={c.id}>
                <button
                  type="button"
                  onClick={() => { onPick(c); close(); }}
                  aria-current={current ? "true" : undefined}
                  className={cn(
                    "flex w-full min-h-11 cursor-pointer flex-col gap-0.5 rounded-fx-md border border-transparent bg-transparent px-3 py-2 text-left",
                    "transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 fx-focus-ring",
                    current && "border-fx-border bg-fx-surface-1",
                  )}
                >
                  <span className="flex w-full items-center gap-2">
                    <span className={cn("min-w-0 flex-1 truncate font-mono text-fx-body-sm font-semibold", nro ? "text-fx-text" : "italic text-fx-text-3")} translate="no">
                      {nro || "Sin expediente"}
                    </span>
                    {current && <Tag value="Actual" severity="info" />}
                    <span className="shrink-0 text-xs tabular-nums text-fx-text-3">{formatDate(c.created_at)}</span>
                  </span>
                  <span className="block w-full truncate text-fx-body-sm text-fx-text-2">{c.caratula?.trim() || "Sin carátula"}</span>
                  {c.nombre_tribunal?.trim() && (
                    <span className="block w-full truncate text-xs text-fx-text-3">{c.nombre_tribunal.trim()}</span>
                  )}
                </button>
              </li>
            );
          })}
        </ul>
      )}
      {filtered.length > MAX_SHOWN && (
        <p className="m-0 mt-2 text-xs text-fx-text-3">Mostrando {MAX_SHOWN} de {filtered.length}. Refiná la búsqueda.</p>
      )}
    </Dialog>
  );
}
