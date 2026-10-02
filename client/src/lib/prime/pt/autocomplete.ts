import type { AutoCompletePassThroughOptions } from "primereact/autocomplete";
import { cn } from "@/lib/utils";
import { FOCUS_RING } from "./shared";

/**
 * AutoComplete (catálogos del paso 2: destinatario, partes, profesión y tipo
 * de dispositivo). Mismo criterio visual que `dropdown` (panel, ítems) e
 * `inputtext` (el `<input>` es un InputText de Prime: toma el pt global de
 * `inputtext` y esta sección se le suma como pt de instancia).
 *
 * `input` y `dropdownButton` son un InputText y un Button hijos: van anidados
 * en `{ root }` y se fusionan con el pt global de `inputtext`/`button`
 * (tailwind-merge resuelve a favor de estas clases). Los tipos públicos de
 * 10.9.9 los declaran planos, de ahí los casts (mismo caso que `calendar`).
 *
 * El ítem resaltado con el teclado no lleva clase en modo unstyled: Prime le
 * pone `data-p-highlight="true"` por DOM, así que se pinta con un variant de
 * atributo.
 */
type Nested = AutoCompletePassThroughOptions["dropdownButton"];

/**
 * `listWrapper` existe en runtime (`_ptm('listWrapper')` en
 * autocomplete.esm.js 10.9.9; Prime le pone `max-height` = `scrollHeight`)
 * pero no en los tipos públicos.
 */
type AutoCompletePT = AutoCompletePassThroughOptions & { listWrapper?: { className: string } };

export const autocomplete: AutoCompletePT = {
  root: { className: "relative inline-flex w-full" },
  // `pr-10` deja lugar al botón del desplegable (adentro del borde del input).
  input: { root: { className: "pr-10 [@media(pointer:coarse)]:pr-12" } } as unknown as AutoCompletePassThroughOptions["input"],
  dropdownButton: {
    root: {
      className: cn(
        // 36 px con mouse, 44 px en táctil (no tiene fondo: no se ve que sobresale 1 px).
        "absolute right-0.5 top-1/2 -translate-y-1/2 w-9 h-9 [@media(pointer:coarse)]:w-11 [@media(pointer:coarse)]:h-11 p-0 border-0 bg-transparent",
        "text-fx-text-3 enabled:hover:bg-transparent enabled:active:bg-transparent hover:text-fx-text rounded-fx-md",
        "motion-safe:enabled:active:-translate-y-1/2",
        FOCUS_RING,
      ),
    },
    icon: { className: "w-4 h-4" },
  } as unknown as Nested,
  loadingIcon: { className: "hidden" },
  panel: {
    className: cn(
      "mt-1 overflow-hidden rounded-fx-lg border border-fx-border bg-fx-surface-2 text-fx-text shadow-fx-2",
      // Nunca más ancho que la pantalla (el ancho mínimo lo pone Prime = input).
      "max-w-[calc(100vw-1rem)]",
    ),
  },
  listWrapper: { className: "overflow-auto overscroll-contain" },
  list: { className: "m-0 list-none py-1" },
  item: ({ context }) => ({
    className: cn(
      "flex items-center gap-2 min-h-10 pl-3 pr-1 py-1 text-fx-body-sm cursor-pointer",
      "transition-colors duration-fx-fast ease-fx",
      "text-fx-text hover:bg-fx-surface-3 data-[p-highlight=true]:bg-fx-surface-3",
      context?.selected && "font-semibold",
      context?.disabled && "opacity-50 cursor-not-allowed",
    ),
  }),
  emptyMessage: { className: "px-3 py-2.5 text-fx-body-sm leading-snug text-fx-text-3" },
  footer: { className: "border-t border-fx-border p-1" },
  transition: {
    timeout: { enter: 120, exit: 100 },
    classNames: {
      enter: "opacity-0",
      enterActive: "!opacity-100 transition-opacity duration-fx-fast",
      exit: "opacity-100",
      exitActive: "!opacity-0 transition-opacity duration-fx-fast",
    },
  },
};
