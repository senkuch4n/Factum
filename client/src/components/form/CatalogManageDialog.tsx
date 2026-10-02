"use client";

/**
 * "Tus sugerencias: <catálogo>" (formulario-caso-catalogos, SDD §7.4).
 * Lista completa de un catálogo del perito con búsqueda, edición en línea y
 * borrado con confirmación en línea. Editar o quitar un valor **no** cambia
 * ningún caso ni el formulario abierto: el caso guarda texto (D11).
 *
 * Teclado: Tab recorre buscar → filas (lápiz, papelera). En edición, Enter
 * guarda y Escape cancela la edición sin cerrar el diálogo (mientras hay una
 * fila en edición o confirmación, Escape no cierra el diálogo).
 */

import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { Pencil, Search, Trash2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { ApiError } from "@/lib/api";
import { MAX_LEN_LINE } from "@/lib/pericial";
import { cleanCatalogValue, filterEntries } from "@/lib/catalogs";
import { useFxToast } from "@/components/shell/FxToastProvider";
import { CATALOG_EMPTY_MESSAGE, type CatalogManageTarget } from "./CatalogAutoComplete";
import type { CatalogEntry } from "@/types";

interface Props {
  /** `false` → cerrado. El resto de los datos se conserva durante la animación de salida. */
  visible: boolean;
  catalogLabel: string;
  entries: CatalogEntry[];
  /** Fila con la que arranca (edición o confirmación), si se abrió desde una sugerencia. */
  initialTarget?: CatalogManageTarget;
  onHide: () => void;
  /** Tira `ApiError` (400/404/409) con el `error` del servidor. */
  onUpdate: (id: string, value: string) => Promise<unknown>;
  onRemove: (id: string) => Promise<void>;
}

type Active = { id: string; mode: CatalogManageTarget["mode"] } | null;

/** Botón de ícono de una fila: 36 px con mouse, 44 px en táctil. */
const ROW_ACTION = cn(
  "inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md border-0 bg-transparent p-0 cursor-pointer",
  "[@media(pointer:coarse)]:h-11 [@media(pointer:coarse)]:w-11",
  "text-fx-text-3 transition-colors duration-fx-fast ease-fx fx-focus-ring",
);

function errorText(e: unknown, fallback: string): string {
  if (e instanceof ApiError && e.serverMessage) return e.serverMessage;
  return fallback;
}

export function CatalogManageDialog({
  visible, catalogLabel, entries, initialTarget, onHide, onUpdate, onRemove,
}: Props) {
  const toast = useFxToast();
  const uid = useId();
  const searchId = `${uid}-search`;
  const rowId = (id: string, part: string) => `${uid}-${part}-${id}`;

  const [query, setQuery] = useState("");
  const [active, setActive] = useState<Active>(null);
  const [draft, setDraft] = useState("");
  const [rowError, setRowError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const pendingFocus = useRef<string | null>(null);

  const filtered = useMemo(() => filterEntries(entries, query), [entries, query]);

  // Al abrir: arranca limpio, o con la fila pedida en edición / confirmación.
  useEffect(() => {
    if (!visible) return;
    setQuery("");
    setRowError(null);
    setBusy(false);
    if (initialTarget && entries.some(e => e.id === initialTarget.id)) {
      setActive(initialTarget);
      setDraft(entries.find(e => e.id === initialTarget.id)?.value ?? "");
    } else {
      setActive(null);
      setDraft("");
    }
    // Solo al abrir; `entries` cambia con cada edición y no tiene que reiniciar.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible, initialTarget]);

  // Foco diferido al próximo commit (la fila cambia de modo antes de existir el control).
  useEffect(() => {
    if (!pendingFocus.current) return;
    const el = document.getElementById(pendingFocus.current);
    if (el) { el.focus(); pendingFocus.current = null; }
  });

  function focusLater(id: string) { pendingFocus.current = id; }

  /** Foco inicial al terminar de abrir: la fila pedida o el buscador. */
  function onShow() {
    const t = initialTarget && entries.some(e => e.id === initialTarget.id) ? initialTarget : null;
    const target = t ? rowId(t.id, t.mode === "edit" ? "input" : "cancel") : searchId;
    document.getElementById(target)?.focus();
  }

  function startEdit(entry: CatalogEntry) {
    setActive({ id: entry.id, mode: "edit" });
    setDraft(entry.value);
    setRowError(null);
    focusLater(rowId(entry.id, "input"));
  }

  function startDelete(entry: CatalogEntry) {
    setActive({ id: entry.id, mode: "delete" });
    setRowError(null);
    focusLater(rowId(entry.id, "cancel"));
  }

  /** Cierra la edición/confirmación y devuelve el foco al botón que la abrió. */
  function cancel(focusPart: "edit" | "delete") {
    if (active) focusLater(rowId(active.id, focusPart));
    setActive(null);
    setRowError(null);
  }

  async function save(entry: CatalogEntry) {
    const value = cleanCatalogValue(draft);
    if (!value) { setRowError("Ingresá un valor"); return; }
    if (value.length > MAX_LEN_LINE) { setRowError(`El valor supera los ${MAX_LEN_LINE} caracteres`); return; }
    if (value === entry.value) { cancel("edit"); return; }
    setBusy(true);
    try {
      await onUpdate(entry.id, value);
      toast.success("Sugerencia actualizada");
      focusLater(rowId(entry.id, "edit"));
      setActive(null);
      setRowError(null);
    } catch (e) {
      setRowError(errorText(e, "No se pudo guardar el cambio. Probá de nuevo"));
      focusLater(rowId(entry.id, "input"));
    } finally {
      setBusy(false);
    }
  }

  async function remove(entry: CatalogEntry) {
    setBusy(true);
    try {
      await onRemove(entry.id);
      toast.success("Sugerencia quitada");
      // La fila desaparece: el foco va a la siguiente (o a la anterior, o al buscador).
      const idx = filtered.findIndex(e => e.id === entry.id);
      const next = filtered[idx + 1] ?? filtered[idx - 1];
      focusLater(next ? rowId(next.id, "edit") : searchId);
      setActive(null);
      setRowError(null);
    } catch (e) {
      setRowError(errorText(e, "No se pudo quitar. Probá de nuevo"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      visible={visible}
      onHide={onHide}
      onShow={onShow}
      header={`Tus sugerencias: ${catalogLabel}`}
      modal
      draggable={false}
      resizable={false}
      dismissableMask
      closeOnEscape={active === null}
      blockScroll
    >
      <div className="flex flex-col gap-3">
        <p className="m-0 text-xs leading-snug text-fx-text-3">
          Editar o quitar una sugerencia no cambia los casos que ya la usan.
        </p>

        {entries.length > 0 && (
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fx-text-3" aria-hidden="true" />
            <InputText
              id={searchId}
              name="buscar-sugerencias"
              type="search"
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="Buscar…"
              aria-label={`Buscar en tus sugerencias: ${catalogLabel}`}
              autoComplete="off"
              pt={{ root: { className: "pl-9" } }}
            />
          </div>
        )}

        {entries.length === 0 ? (
          <p className="m-0 rounded-fx-md border border-dashed border-fx-border px-3 py-4 text-center text-fx-body-sm text-fx-text-3">
            {CATALOG_EMPTY_MESSAGE}
          </p>
        ) : filtered.length === 0 ? (
          <p role="status" className="m-0 px-1 py-3 text-fx-body-sm text-fx-text-3">
            No hay sugerencias que coincidan con «{query.trim()}».
          </p>
        ) : (
          <ul
            aria-label={`Sugerencias: ${catalogLabel}`}
            className="m-0 max-h-[min(24rem,50vh)] list-none divide-y divide-fx-border overflow-y-auto overscroll-contain rounded-fx-md border border-fx-border p-0"
          >
            {filtered.map(entry => {
              const mode = active?.id === entry.id ? active.mode : null;
              const errId = rowId(entry.id, "error");
              return (
                <li key={entry.id} className={cn("px-2 py-1.5", mode && "bg-fx-surface-1")}>
                  {mode === "edit" ? (
                    <div className="flex flex-col gap-2 py-1">
                      <InputText
                        id={rowId(entry.id, "input")}
                        name="sugerencia"
                        value={draft}
                        maxLength={MAX_LEN_LINE}
                        autoComplete="off"
                        aria-label={`Editar «${entry.value}»`}
                        aria-invalid={!!rowError || undefined}
                        aria-describedby={rowError ? errId : undefined}
                        disabled={busy}
                        onChange={e => { setDraft(e.target.value); if (rowError) setRowError(null); }}
                        onKeyDown={e => {
                          if (e.key === "Enter") { e.preventDefault(); void save(entry); }
                          else if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); cancel("edit"); }
                        }}
                      />
                      {rowError && (
                        <p id={errId} role="alert" className="m-0 text-xs font-medium text-fx-danger">{rowError}</p>
                      )}
                      {!rowError && cleanCatalogValue(draft) && cleanCatalogValue(draft) !== entry.value && (
                        <p className="m-0 text-xs text-fx-text-3">Antes: «{entry.value}»</p>
                      )}
                      <div className="flex justify-end gap-2">
                        <Button
                          type="button"
                          size="small"
                          severity="secondary"
                          text
                          label="Cancelar"
                          disabled={busy}
                          onClick={() => cancel("edit")}
                          className="min-h-11 sm:min-h-0"
                        />
                        <Button
                          type="button"
                          size="small"
                          label="Guardar"
                          loading={busy}
                          onClick={() => void save(entry)}
                          className="min-h-11 sm:min-h-0"
                        />
                      </div>
                    </div>
                  ) : mode === "delete" ? (
                    <div
                      role="group"
                      aria-labelledby={rowId(entry.id, "confirm")}
                      className="flex flex-col gap-2 py-1"
                      onKeyDown={e => {
                        if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); cancel("delete"); }
                      }}
                    >
                      <p id={rowId(entry.id, "confirm")} className="m-0 text-fx-body-sm text-fx-text">
                        <span className="font-semibold break-words">«{entry.value}»</span>
                        <span className="block text-fx-text-2">¿Quitar de tus sugerencias? Los casos que lo usan no cambian</span>
                      </p>
                      {rowError && (
                        <p role="alert" className="m-0 text-xs font-medium text-fx-danger">{rowError}</p>
                      )}
                      <div className="flex justify-end gap-2">
                        <Button
                          id={rowId(entry.id, "cancel")}
                          type="button"
                          size="small"
                          severity="secondary"
                          text
                          label="Cancelar"
                          disabled={busy}
                          onClick={() => cancel("delete")}
                          className="min-h-11 sm:min-h-0"
                        />
                        <Button
                          type="button"
                          size="small"
                          severity="danger"
                          label="Quitar"
                          loading={busy}
                          onClick={() => void remove(entry)}
                          className="min-h-11 sm:min-h-0"
                        />
                      </div>
                    </div>
                  ) : (
                    <div className="flex min-h-10 items-center gap-1">
                      <span className="min-w-0 flex-1 break-words px-1 text-fx-body-sm text-fx-text">{entry.value}</span>
                      <button
                        id={rowId(entry.id, "edit")}
                        type="button"
                        onClick={() => startEdit(entry)}
                        aria-label={`Editar «${entry.value}»`}
                        title="Editar"
                        className={cn(ROW_ACTION, "hover:bg-fx-surface-3 hover:text-fx-text")}
                      >
                        <Pencil className="h-4 w-4" aria-hidden="true" />
                      </button>
                      <button
                        id={rowId(entry.id, "delete")}
                        type="button"
                        onClick={() => startDelete(entry)}
                        aria-label={`Quitar «${entry.value}» de tus sugerencias`}
                        title="Quitar de tus sugerencias"
                        className={cn(ROW_ACTION, "hover:bg-fx-danger-soft hover:text-fx-danger")}
                      >
                        <Trash2 className="h-4 w-4" aria-hidden="true" />
                      </button>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </Dialog>
  );
}
