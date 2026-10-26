"use client";

import { useCallback, useEffect, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { Checkbox } from "primereact/checkbox";
import { AlertTriangle, ArrowLeft, ChevronRight, FolderClosed, Loader2 } from "lucide-react";
import { agent, type DeviceFileEntry } from "@/lib/agent";
import { cn } from "@/lib/utils";

interface Props {
  open: boolean;
  serial: string;
  initialSelected: string[];
  onClose: () => void;
  onConfirm: (paths: string[]) => void;
}

const ROOT = "/sdcard";

/**
 * Árbol de carpetas de `/sdcard` para "Elegir carpetas" (§10.4.2). Navega con
 * `agent.listDeviceFiles` en modo selección de carpetas (casillas), sin traer
 * archivos. Devuelve las rutas elegidas al confirmar.
 */
export function DeviceFolderPicker({ open, serial, initialSelected, onClose, onConfirm }: Props) {
  const [path, setPath] = useState(ROOT);
  const [entries, setEntries] = useState<DeviceFileEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[]>(initialSelected);

  const load = useCallback((p: string) => {
    setLoading(true);
    setError(null);
    agent.listDeviceFiles(serial, p)
      .then(list => setEntries(list.filter(e => e.is_directory)))
      .catch(() => setError("No se pudo leer la carpeta. ¿Sigue conectado el celular?"))
      .finally(() => setLoading(false));
  }, [serial]);

  // Al abrir: reinicia a la raíz y adopta la selección vigente.
  useEffect(() => {
    if (!open) return;
    setPath(ROOT);
    setSelected(initialSelected);
  }, [open, initialSelected]);

  useEffect(() => { if (open) load(path); }, [open, path, load]);

  function toggle(p: string) {
    setSelected(prev => prev.includes(p) ? prev.filter(x => x !== p) : [...prev, p]);
  }

  const canGoUp = path !== ROOT;
  const parent = path.slice(0, path.lastIndexOf("/")) || ROOT;

  return (
    <Dialog
      visible={open}
      onHide={onClose}
      draggable={false}
      resizable={false}
      header="Elegir carpetas del celular"
      pt={{ root: { className: "w-[min(34rem,100%)]" } }}
      footer={
        <>
          <Button severity="secondary" label="Cancelar" onClick={onClose} className="min-h-11" />
          <Button
            label={selected.length > 0 ? `Usar ${selected.length} carpeta${selected.length === 1 ? "" : "s"}` : "Listo"}
            onClick={() => onConfirm(selected)}
            className="min-h-11"
          />
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex items-center gap-2">
          <Button
            type="button"
            text
            size="small"
            icon={<ArrowLeft className="h-4 w-4" aria-hidden="true" />}
            label="Subir"
            disabled={!canGoUp}
            onClick={() => setPath(parent)}
            className="min-h-11"
          />
          <p className="m-0 min-w-0 truncate font-mono text-xs text-fx-text-3" translate="no">{path}</p>
        </div>

        {error && (
          <p className="m-0 flex items-center gap-2 text-fx-body-sm text-fx-danger">
            <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
            {error}
          </p>
        )}

        {loading ? (
          <div role="status" className="flex justify-center py-8">
            <Loader2 className="h-5 w-5 animate-spin text-fx-text-3" aria-hidden="true" />
            <span className="sr-only">Cargando…</span>
          </div>
        ) : entries.length === 0 ? (
          <p className="m-0 py-8 text-center text-fx-body-sm text-fx-text-2">No hay subcarpetas acá.</p>
        ) : (
          <ul className="m-0 max-h-72 list-none space-y-1 overflow-y-auto p-0">
            {entries.map(e => {
              const checked = selected.includes(e.path);
              const cid = `folder-${e.path}`;
              return (
                <li key={e.path} className="flex items-center gap-2 rounded-fx-md px-1 py-0.5 hover:bg-fx-surface-2">
                  <Checkbox inputId={cid} checked={checked} onChange={() => toggle(e.path)} />
                  <label htmlFor={cid} className="flex min-w-0 flex-1 cursor-pointer items-center gap-2 py-2">
                    <FolderClosed className="h-4 w-4 shrink-0 text-fx-text-2" aria-hidden="true" />
                    <span className="min-w-0 truncate text-fx-body-sm text-fx-text">{e.name}</span>
                  </label>
                  <button
                    type="button"
                    onClick={() => setPath(e.path)}
                    aria-label={`Abrir ${e.name}`}
                    className={cn(
                      "flex h-11 w-11 shrink-0 items-center justify-center rounded-fx-md text-fx-text-3",
                      "hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring",
                    )}
                  >
                    <ChevronRight className="h-4 w-4" aria-hidden="true" />
                  </button>
                </li>
              );
            })}
          </ul>
        )}

        {selected.length > 0 && (
          <p className="m-0 text-xs text-fx-text-2">
            {selected.length} carpeta{selected.length === 1 ? "" : "s"} elegida{selected.length === 1 ? "" : "s"}.
          </p>
        )}
      </div>
    </Dialog>
  );
}
