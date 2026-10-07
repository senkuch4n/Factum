"use client";

/**
 * Campo de texto libre con sugerencias de un catálogo del perito
 * (formulario-caso-catalogos, SDD §7.3). Wrapper de `AutoComplete` de Prime
 * 10.9.9 (pt `autocomplete`):
 *
 * - El valor es siempre **texto**: lo que se tipea queda aunque no se elija
 *   nada, y elegir una sugerencia copia su texto (el caso no referencia al
 *   catálogo).
 * - Si lo escrito no está en el catálogo, la lista arranca con
 *   "Nuevo: «…» (se guarda con el caso)".
 * - Cada sugerencia tiene lápiz y papelera, que abren "Administrar" con esa
 *   entrada en edición o en confirmación (no se edita dentro del listbox: no
 *   se anidan controles en un `role="option"`). Esos botones no son
 *   alcanzables con Tab adentro del listbox: el camino por teclado es el
 *   botón "Administrar sugerencias" junto a la etiqueta y el pie del panel.
 * - Teclado: flecha abajo abre la lista (Prime solo la abre al tipear o con
 *   el botón), flechas recorren, Enter elige, Escape cierra.
 */

import { useCallback, useRef, useState } from "react";
import { AutoComplete } from "primereact/autocomplete";
import type { AutoCompleteCompleteEvent, AutoCompletePassThroughOptions } from "primereact/autocomplete";
import { CheckCircle2, ChevronDown, ListChecks, Pencil, Plus, Trash2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { filterEntries, normalizeCatalogKey } from "@/lib/catalogs";
import type { CatalogEntry, CatalogId } from "@/types";

export type CatalogManageTarget = { id: string; mode: "edit" | "delete" };
export type CatalogStatus = "loading" | "error" | "ready";

export const CATALOG_EMPTY_MESSAGE =
  "Todavía no guardaste valores. Escribí uno y se va a sugerir en tus próximos casos";

const NEW_ID = "__new__";

interface Suggestion {
  id: string;
  value: string;
}

/** Botón de ícono dentro de una sugerencia: 36 px con mouse, 44 px en táctil. */
const ITEM_ACTION = cn(
  "inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md border-0 bg-transparent p-0 cursor-pointer",
  "[@media(pointer:coarse)]:h-11 [@media(pointer:coarse)]:w-11",
  "text-fx-text-3 transition-colors duration-fx-fast ease-fx",
);

const ITEM_ACTION_TONES = {
  edit: "hover:bg-fx-surface-2 hover:text-fx-text",
  delete: "hover:bg-fx-danger-soft hover:text-fx-danger",
} as const;

interface Props {
  id: string;
  name: string;
  value: string;
  onChange: (value: string) => void;
  entries: CatalogEntry[];
  catalog: CatalogId;
  catalogLabel: string;
  status: CatalogStatus;
  invalid?: boolean;
  ariaDescribedBy?: string;
  required?: boolean;
  maxLength?: number;
  placeholder?: string;
  /** Abre "Administrar" (con una entrada en edición o confirmación si viene `target`). */
  onManage: (target?: CatalogManageTarget) => void;
}

export function CatalogAutoComplete({
  id, name, value, onChange, entries, catalog, catalogLabel, status,
  invalid, ariaDescribedBy, required, maxLength, placeholder, onManage,
}: Props) {
  const acRef = useRef<AutoComplete<Suggestion>>(null);
  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);

  const complete = useCallback((e: AutoCompleteCompleteEvent) => {
    const list: Suggestion[] = filterEntries(entries, e.query);
    const key = normalizeCatalogKey(e.query);
    if (key && !entries.some(en => normalizeCatalogKey(en.value) === key)) {
      list.unshift({ id: NEW_ID, value: e.query.trim() });
    }
    setSuggestions([...list]);
  }, [entries]);

  const manage = useCallback((target?: CatalogManageTarget) => {
    acRef.current?.hide();
    onManage(target);
  }, [onManage]);

  function itemTemplate(item: Suggestion) {
    if (item.id === NEW_ID) {
      return (
        <span className="flex min-w-0 flex-1 items-center gap-2 py-1.5 pr-2">
          <Plus className="h-4 w-4 shrink-0 text-fx-accent-text" aria-hidden="true" />
          <span className="min-w-0 break-words">
            <span className="font-semibold text-fx-accent-text">Nuevo:</span> «{item.value}»{" "}
            <span className="text-xs text-fx-text-3">(se guarda con el caso)</span>
          </span>
        </span>
      );
    }
    // Las acciones no seleccionan la opción ni le sacan el foco al input:
    // `preventDefault` en mousedown y `stopPropagation` en click (el `<li>`
    // elige la opción en su onClick).
    const action = (mode: CatalogManageTarget["mode"]) => ({
      type: "button" as const,
      tabIndex: -1,
      onMouseDown: (e: React.MouseEvent) => { e.preventDefault(); e.stopPropagation(); },
      onClick: (e: React.MouseEvent) => { e.preventDefault(); e.stopPropagation(); manage({ id: item.id, mode }); },
    });
    return (
      <>
        <span className="min-w-0 flex-1 truncate" title={item.value}>{item.value}</span>
        <span className="flex shrink-0 items-center">
          <button {...action("edit")} aria-label={`Editar «${item.value}»`} title="Editar" className={cn(ITEM_ACTION, ITEM_ACTION_TONES.edit)}>
            <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
          <button {...action("delete")} aria-label={`Quitar «${item.value}» de tus sugerencias`} title="Quitar de tus sugerencias" className={cn(ITEM_ACTION, ITEM_ACTION_TONES.delete)}>
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
        </span>
      </>
    );
  }

  /**
   * Prime 10 resalta la opción con el teclado solo por DOM (`data-p-highlight`)
   * y no maneja `aria-activedescendant`: sin esto el lector de pantalla no
   * anuncia la opción al recorrer con las flechas. Corre después del handler
   * de Prime (mergeProps encadena los dos).
   */
  function syncActiveDescendant(input: HTMLInputElement, key: string) {
    const li = key === "ArrowDown" || key === "ArrowUp"
      ? acRef.current?.getOverlay()?.querySelector<HTMLLIElement>('li[data-p-highlight="true"]')
      : null;
    if (!li) { input.removeAttribute("aria-activedescendant"); return; }
    if (!li.id) li.id = `${id}-opt-${li.getAttribute("index") ?? "0"}`;
    input.setAttribute("aria-activedescendant", li.id);
  }

  const showValid = !!required && value.trim().length > 0 && !invalid;
  const emptyMessage =
    status === "loading" ? "Cargando tus sugerencias…"
      : status === "error" ? "No se pudieron cargar tus sugerencias"
        : CATALOG_EMPTY_MESSAGE;

  // ARIA y clases del `<input>` por `pt.input.root`: los `aria-*` como props
  // irían también al `<span>` raíz (Prime los reparte a los dos).
  const pt = {
    input: {
      root: {
        className: cn(showValid && "pr-16 [@media(pointer:coarse)]:pr-[4.5rem]"),
        "aria-required": required || undefined,
        "aria-invalid": invalid || undefined,
        "aria-describedby": ariaDescribedBy,
        onBlur: (e: React.FocusEvent<HTMLInputElement>) => e.currentTarget.removeAttribute("aria-activedescendant"),
        onKeyDown: (e: React.KeyboardEvent<HTMLInputElement>) => {
          syncActiveDescendant(e.currentTarget, e.key);
          // Prime procesa antes sus teclas (con la lista abierta). Cerrada,
          // flecha abajo la abre con el catálogo completo, como el botón.
          // (el panel se desmonta al cerrarse: sin overlay = cerrado).
          if (e.key === "ArrowDown" && !acRef.current?.getOverlay()) {
            e.preventDefault();
            acRef.current?.search(e, "", "dropdown");
          }
        },
      },
    },
  } as unknown as AutoCompletePassThroughOptions; // `input` anidado (InputText hijo): los tipos lo declaran plano

  return (
    <div className="relative">
      <AutoComplete<Suggestion>
        ref={acRef}
        inputId={id}
        name={name}
        value={value}
        suggestions={suggestions}
        completeMethod={complete}
        field="value"
        onChange={e => {
          const v = e.value as Suggestion | string | null | undefined;
          onChange(typeof v === "string" ? v : v?.value ?? "");
        }}
        dropdown
        dropdownMode="blank"
        dropdownAriaLabel="Mostrar sugerencias"
        dropdownIcon={<ChevronDown className="h-4 w-4" aria-hidden="true" />}
        forceSelection={false}
        showEmptyMessage
        emptyMessage={emptyMessage}
        delay={150}
        maxLength={maxLength}
        placeholder={placeholder}
        invalid={invalid}
        scrollHeight="min(16rem, 45vh)"
        itemTemplate={itemTemplate}
        panelFooterTemplate={
          status === "ready" ? (
            <button
              type="button"
              onMouseDown={e => e.preventDefault()}
              onClick={() => manage()}
              className={cn(
                "flex w-full min-h-11 items-center gap-2 rounded-fx-md border-0 bg-transparent px-3 text-left",
                "text-fx-body-sm font-semibold text-fx-accent-text cursor-pointer",
                "transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 fx-focus-ring",
              )}
            >
              <ListChecks className="h-4 w-4 shrink-0" aria-hidden="true" />
              Administrar sugerencias
              <span className="sr-only">: {catalogLabel}</span>
            </button>
          ) : null
        }
        pt={pt}
        data-catalog={catalog}
      />
      {showValid && (
        <CheckCircle2
          className="pointer-events-none absolute right-10 top-1/2 h-4 w-4 -translate-y-1/2 text-fx-success [@media(pointer:coarse)]:right-12"
          aria-hidden="true"
        />
      )}
    </div>
  );
}

/**
 * Botón de ícono junto a la etiqueta: el camino por teclado (y lector de
 * pantalla) para editar y quitar valores del catálogo.
 */
export function CatalogManageButton({ catalogLabel, onClick }: { catalogLabel: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={`Administrar sugerencias: ${catalogLabel}`}
      title="Administrar sugerencias"
      className={cn(
        "inline-flex h-8 w-8 items-center justify-center rounded-fx-md border-0 bg-transparent p-0 cursor-pointer",
        "text-fx-text-3 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring",
      )}
    >
      <ListChecks className="h-4 w-4" aria-hidden="true" />
    </button>
  );
}
