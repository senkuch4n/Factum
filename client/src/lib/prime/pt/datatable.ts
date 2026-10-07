import type { DataTablePassThroughOptions } from "primereact/datatable";

/**
 * DataTable. El wrapper scrollea en horizontal (la tabla vive dentro de una
 * tarjeta), cabecera sobre superficie 2 y hover de fila en superficie 3.
 * El estilo de cabeceras y celdas está en `column.ts`.
 */
export const datatable: DataTablePassThroughOptions = {
  root: { className: "relative" },
  wrapper: { className: "overflow-x-auto" },
  table: { className: "w-full border-collapse text-left" },
  thead: { className: "bg-fx-surface-2" },
  tbody: { className: "" },
  bodyRow: { className: "transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3" },
  emptyMessage: { className: "px-4 py-6 text-center text-fx-text-3" },
  loadingOverlay: { className: "absolute inset-0 flex items-center justify-center bg-fx-overlay" },
  loadingIcon: { className: "w-6 h-6 animate-spin text-fx-text-2" },
};
