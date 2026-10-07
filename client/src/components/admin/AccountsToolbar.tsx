"use client";

import { useEffect, useId, useRef, useState } from "react";
import { InputText } from "primereact/inputtext";
import { SelectButton } from "primereact/selectbutton";
import { Search, X } from "lucide-react";
import type { AccountFilters, RoleFilter, StatusFilter } from "@/lib/admin-accounts";

const SEARCH_DEBOUNCE_MS = 200;

const STATUS_OPTIONS: { label: string; value: StatusFilter }[] = [
  { label: "Todas", value: "all" },
  { label: "Activas", value: "activo" },
  { label: "Suspendidas", value: "suspendido" },
];

const ROLE_OPTIONS: { label: string; value: RoleFilter }[] = [
  { label: "Todos", value: "all" },
  { label: "Clientes", value: "cliente" },
  { label: "Superadmins", value: "superadmin" },
];

/* 44 px de alto en mobile (objeto, no función: se fusiona con el pt global). */
const SEGMENT_PT = { button: { className: "max-md:min-h-11" } };

interface Props {
  filters: AccountFilters;
  onChange: (next: AccountFilters) => void;
}

/**
 * Buscador (con debounce) y filtros de estado y rol del panel de cuentas.
 * La búsqueda es en el navegador (D8). El texto del buscador es local; el
 * padre recibe el valor ya "asentado". Si el padre limpia los filtros, el
 * campo se vacía.
 */
export function AccountsToolbar({ filters, onChange }: Props) {
  const uid = useId();
  const searchId = `${uid}-q`;
  const [text, setText] = useState(filters.q);
  const emitted = useRef(filters.q);
  // Último `filters`/`onChange` sin reiniciar el debounce en cada render del padre.
  const latest = useRef({ filters, onChange });
  useEffect(() => { latest.current = { filters, onChange }; });

  // Limpieza desde afuera ("Limpiar filtros"): sincroniza el campo.
  useEffect(() => {
    if (filters.q !== emitted.current) {
      emitted.current = filters.q;
      setText(filters.q);
    }
  }, [filters.q]);

  useEffect(() => {
    if (text === emitted.current) return;
    const t = setTimeout(() => {
      emitted.current = text;
      latest.current.onChange({ ...latest.current.filters, q: text });
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(t);
  }, [text]);

  function clearSearch() {
    setText("");
    emitted.current = "";
    onChange({ ...filters, q: "" });
    document.getElementById(searchId)?.focus();
  }

  return (
    <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
      <div className="relative min-w-0 flex-1 lg:max-w-md">
        <label htmlFor={searchId} className="sr-only">Buscar por nombre o DNI</label>
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fx-text-3"
          aria-hidden="true"
        />
        <InputText
          id={searchId}
          type="search"
          name="buscar-cuenta"
          autoComplete="off"
          spellCheck={false}
          aria-label="Buscar por nombre o DNI"
          placeholder="Buscar por nombre o DNI…"
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Escape" && text) { e.preventDefault(); clearSearch(); } }}
          className="pl-9 pr-11 [&::-webkit-search-cancel-button]:hidden"
        />
        {text && (
          <button
            type="button"
            onClick={clearSearch}
            aria-label="Limpiar búsqueda"
            className="absolute right-0.5 top-1/2 inline-flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-fx-md text-fx-text-3 transition-colors duration-fx-fast ease-fx hover:text-fx-text fx-focus-ring"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        )}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <SelectButton
          value={filters.status}
          options={STATUS_OPTIONS}
          optionLabel="label"
          optionValue="value"
          allowEmpty={false}
          onChange={(e) => onChange({ ...filters, status: (e.value as StatusFilter) ?? "all" })}
          pt={{ ...SEGMENT_PT, root: { "aria-label": "Filtrar por estado" } as React.HTMLAttributes<HTMLDivElement> }}
        />
        <SelectButton
          value={filters.role}
          options={ROLE_OPTIONS}
          optionLabel="label"
          optionValue="value"
          allowEmpty={false}
          onChange={(e) => onChange({ ...filters, role: (e.value as RoleFilter) ?? "all" })}
          pt={{ ...SEGMENT_PT, root: { "aria-label": "Filtrar por rol" } as React.HTMLAttributes<HTMLDivElement> }}
        />
      </div>
    </div>
  );
}
