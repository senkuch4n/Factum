import type { DropdownPassThroughOptions } from "primereact/dropdown";
import { cn } from "@/lib/utils";

/**
 * Dropdown (tratamiento del perito; desde la parte 3, cámara y micrófono).
 *
 * El foco real está en un `<input>` oculto (`.p-hidden-accessible`) dentro
 * del root: por eso el anillo se pinta en el root con
 * `has-[input:focus-visible]`. Prime aplica `ptm('input')` también a ese
 * input de teclado, pero queda recortado por su contenedor y no se ve.
 * La opción elegida usa el acento (selección activa); el resto, superficies.
 */
/**
 * `dropdownIcon` existe en runtime (`ptm('dropdownIcon')` en dropdown.esm.js
 * 10.9.9) pero no en los tipos públicos: de ahí el tipo extendido.
 */
type DropdownPT = DropdownPassThroughOptions & { dropdownIcon?: { className: string } };

export const dropdown: DropdownPT = {
  root: ({ props, state }) => ({
    className: cn(
      "relative inline-flex w-full items-stretch cursor-pointer select-none rounded-fx-md border",
      "transition-[border-color] duration-fx-fast ease-fx",
      "has-[input:focus-visible]:outline has-[input:focus-visible]:outline-2 has-[input:focus-visible]:outline-offset-2 has-[input:focus-visible]:outline-fx-focus",
      props.disabled
        ? "bg-fx-surface-1 border-fx-border text-fx-text-disabled cursor-not-allowed"
        : cn(
            "bg-fx-surface-2 text-fx-text",
            props.invalid
              ? "border-fx-danger"
              : state.focused || state.overlayVisible
                ? "border-fx-accent"
                : "border-fx-border-strong hover:border-fx-text-3",
          ),
    ),
  }),
  input: ({ props }) => ({
    className: cn(
      "flex-1 min-w-0 truncate bg-transparent border-0 px-3 py-2.5 text-fx-body-sm leading-normal outline-none",
      props.value == null && "text-fx-text-3",
    ),
  }),
  trigger: { className: "flex w-10 shrink-0 items-center justify-center text-fx-text-3" },
  dropdownIcon: { className: "w-4 h-4" },
  panel: { className: "mt-1 overflow-hidden rounded-fx-lg border border-fx-border bg-fx-surface-2 text-fx-text shadow-fx-2" },
  wrapper: { className: "max-h-60 overflow-auto" },
  list: { className: "m-0 list-none py-1" },
  item: ({ context }) => ({
    className: cn(
      "flex items-center gap-2 min-h-10 px-3 py-2 text-fx-body-sm cursor-pointer",
      "transition-colors duration-fx-fast ease-fx",
      context.selected
        ? "bg-fx-accent-soft text-fx-accent-text font-semibold"
        : context.focused
          ? "bg-fx-surface-3 text-fx-text"
          : "text-fx-text hover:bg-fx-surface-3",
      context.disabled && "opacity-50 cursor-not-allowed",
    ),
  }),
  itemLabel: { className: "truncate" },
  checkIcon: { className: "w-4 h-4 shrink-0" },
  blankIcon: { className: "w-4 h-4 shrink-0" },
  emptyMessage: { className: "px-3 py-2 text-fx-text-3" },
  clearIcon: { className: "w-4 h-4 text-fx-text-3 mr-1 self-center" },
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
