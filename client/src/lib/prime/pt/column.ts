import type { ColumnPassThroughMethodOptions, ColumnPassThroughOptions } from "primereact/column";
import { cn } from "@/lib/utils";

/**
 * Column (cabeceras y celdas del DataTable).
 *
 * DataTable resuelve cada sección de columna dos veces y las fusiona: una con
 * los params de la tabla y los de la columna en `params.column`, y otra con
 * los de la columna directamente. `meta()` normaliza las dos, así las dos
 * llamadas devuelven las mismas clases.
 */
type Params = ColumnPassThroughMethodOptions & { column?: ColumnPassThroughMethodOptions };

function meta(params: Params): ColumnPassThroughMethodOptions {
  return params.column ?? params;
}

/* El outline va hacia adentro (-offset) para que el overflow del wrapper no lo recorte. */
const HEADER_FOCUS =
  "outline-none focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-fx-focus";

export const column: ColumnPassThroughOptions = {
  headerCell: (params) => {
    const { props, context } = meta(params as Params);
    return {
      className: cn(
        "px-4 py-3 border-b border-fx-border text-fx-label uppercase text-fx-text-3 whitespace-nowrap",
        props?.sortable && cn("cursor-pointer select-none hover:text-fx-text", HEADER_FOCUS),
        context?.sorted && "text-fx-text",
      ),
    };
  },
  headerContent: { className: "inline-flex items-center gap-1.5" },
  sort: { className: "inline-flex" },
  sortIcon: { className: "w-3.5 h-3.5" },
  bodyCell: { className: "px-4 py-3 border-b border-fx-border text-fx-body-sm text-fx-text align-middle" },
};
