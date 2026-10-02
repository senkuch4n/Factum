"use client";

import { Dialog } from "primereact/dialog";
import { CircleHelp } from "lucide-react";
import { useShortcuts } from "./EditorToolbar";

/** Formatos del dialecto (D3): atajo de teclado y lo que se puede escribir directo. */
const ROWS: { label: string; keys?: string; typed?: string }[] = [
  { label: "Negrita", keys: "Mod+B", typed: "**texto**" },
  { label: "Cursiva", keys: "Mod+I", typed: "*texto*" },
  { label: "Subrayado", keys: "Mod+U" },
  { label: "Lista con viñetas", keys: "Mod+Shift+8", typed: "- al principio de la línea" },
  { label: "Lista numerada", keys: "Mod+Shift+7", typed: "1. al principio de la línea" },
  { label: "Anidar / desanidar ítem (hasta 3 niveles)", keys: "Tab / Shift+Tab" },
  { label: "Subtítulo", keys: "Mod+Alt+3", typed: "### al principio de la línea" },
  { label: "Cita", keys: "Mod+Shift+B", typed: "> al principio de la línea" },
  { label: "Código en línea", keys: "Mod+E", typed: "`texto`" },
  { label: "Bloque de código", keys: "Mod+Alt+C", typed: "``` y un espacio" },
  { label: "Enlace", keys: "Mod+K" },
  { label: "Salto de línea sin párrafo nuevo", keys: "Shift+Enter" },
  { label: "Pegar sin formato", keys: "Mod+Shift+V" },
  { label: "Deshacer / rehacer", keys: "Mod+Z / Mod+Shift+Z" },
];

/** Diálogo "Formatos disponibles" con la lista de atajos (SDD §7.5). */
export function FormatHelp({ open, onClose }: { open: boolean; onClose: () => void }) {
  const sc = useShortcuts();
  const keys = (k: string) => k.split(" / ").map(part => sc(part).label).join(" / ");
  return (
    <Dialog
      visible={open}
      onHide={onClose}
      modal
      dismissableMask
      closeOnEscape
      draggable={false}
      resizable={false}
      header={
        <span className="flex items-center gap-2 text-fx-body font-bold">
          <CircleHelp className="h-4 w-4 text-fx-text-3" aria-hidden="true" />
          Formatos disponibles
        </span>
      }
      pt={{ root: { className: "w-[min(34rem,100%)]" } }}
    >
      <p className="m-0 mb-3">
        Usá la barra, los atajos de teclado o escribí la marca directamente. Lo que no está en
        esta lista (tablas, tachado, imágenes, colores) no se guarda en el informe.
      </p>
      <table className="w-full border-collapse text-left text-fx-body-sm">
        <caption className="sr-only">Formatos, atajos de teclado y marcas que se pueden escribir</caption>
        <thead>
          <tr className="border-b border-fx-border text-fx-label uppercase text-fx-text-3">
            <th scope="col" className="py-2 pr-3 font-semibold">Formato</th>
            <th scope="col" className="py-2 pr-3 font-semibold">Atajo</th>
            <th scope="col" className="py-2 font-semibold">Escribiendo</th>
          </tr>
        </thead>
        <tbody>
          {ROWS.map(r => (
            <tr key={r.label} className="border-b border-fx-border last:border-0 align-top">
              <th scope="row" className="py-2 pr-3 font-medium text-fx-text">{r.label}</th>
              <td className="py-2 pr-3">
                {r.keys && <kbd className="whitespace-nowrap rounded-fx-sm bg-fx-surface-3 px-1.5 py-0.5 font-mono text-xs text-fx-text">{keys(r.keys)}</kbd>}
              </td>
              <td className="py-2">{r.typed && <code className="font-mono text-xs">{r.typed}</code>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Dialog>
  );
}
