import type { CalendarPassThroughMethodOptions, CalendarPassThroughOptions } from "primereact/calendar";
import { cn } from "@/lib/utils";
import { inputRootClasses, type InputRootParams } from "./inputtext";
import { CLOSE_BUTTON, FOCUS_RING } from "./shared";

/**
 * Calendar (rango de fechas del historial).
 *
 * `input`, `dropdownButton`, `todayButton` y `clearButton` son un InputText y
 * tres Button de Prime adentro del Calendar: su sección se pasa como `pt` del
 * hijo, así que van anidadas en `{ root }` (como en el preset oficial) y se
 * fusionan con el pt global de `inputtext`/`button` (tailwind-merge resuelve
 * los conflictos a favor de estas). Los tipos públicos de 10.9.9 declaran
 * esas secciones como atributos HTML planos, de ahí los casts.
 */
type Nested = CalendarPassThroughOptions["dropdownButton"];

type DayContext = CalendarPassThroughMethodOptions["context"];

function cellClasses(context: DayContext | undefined, extra?: string) {
  return cn(
    "inline-flex items-center justify-center w-9 h-9 rounded-fx-md cursor-pointer",
    "transition-colors duration-fx-fast ease-fx",
    FOCUS_RING,
    context?.selected
      ? "bg-fx-accent-soft text-fx-accent-text font-bold ring-1 ring-inset ring-fx-accent"
      : context?.disabled
        ? "text-fx-text-disabled cursor-not-allowed"
        : "text-fx-text hover:bg-fx-surface-3",
    context?.today && !context?.selected && "font-bold underline underline-offset-4",
    context?.otherMonth && !context?.selected && "text-fx-text-3",
    extra,
  );
}

const SMALL_TEXT_BUTTON = cn(
  "px-3 py-1.5 text-xs font-bold rounded-fx-md border-transparent bg-transparent",
  "text-fx-accent-text enabled:hover:bg-fx-surface-3 enabled:active:bg-fx-surface-3",
  FOCUS_RING,
);

export const calendar: CalendarPassThroughOptions = {
  root: { className: "relative inline-flex w-full sm:w-auto" },
  input: {
    root: (params: InputRootParams) => ({ className: cn(inputRootClasses(params), "pr-10") }),
  } as unknown as Nested,
  dropdownButton: {
    root: {
      className: cn(
        "absolute right-0.5 top-1/2 -translate-y-1/2 w-9 h-9 p-0 border-0 bg-transparent",
        "text-fx-text-3 enabled:hover:bg-transparent enabled:active:bg-transparent hover:text-fx-text rounded-fx-md",
        "motion-safe:enabled:active:-translate-y-1/2",
        FOCUS_RING,
      ),
    },
  } as unknown as Nested,
  panel: ({ props }) => ({
    className: cn(
      "bg-fx-surface-2 text-fx-text border border-fx-border rounded-fx-lg shadow-fx-2 p-3",
      "w-[min(20rem,calc(100vw-2rem))]",
      !props.inline && "absolute",
    ),
  }),
  header: { className: "flex items-center justify-between gap-2 pb-2 mb-2 border-b border-fx-border" },
  previousButton: { className: CLOSE_BUTTON },
  nextButton: { className: CLOSE_BUTTON },
  previousIcon: { className: "w-4 h-4" },
  nextIcon: { className: "w-4 h-4" },
  title: { className: "flex items-center gap-1 text-fx-body-sm font-semibold" },
  monthTitle: { className: cn("px-1.5 py-1 rounded-fx-sm border-0 bg-transparent text-fx-text font-semibold cursor-pointer hover:bg-fx-surface-3 capitalize", FOCUS_RING) },
  yearTitle: { className: cn("px-1.5 py-1 rounded-fx-sm border-0 bg-transparent text-fx-text font-semibold cursor-pointer hover:bg-fx-surface-3", FOCUS_RING) },
  table: { className: "w-full border-collapse text-fx-body-sm" },
  tableHeaderCell: { className: "p-1 text-center text-fx-label text-fx-text-3" },
  weekDay: { className: "text-fx-label text-fx-text-3" },
  day: { className: "p-0.5 text-center" },
  dayLabel: ({ context }) => ({ className: cellClasses(context) }),
  monthPicker: { className: "grid grid-cols-3 gap-1" },
  yearPicker: { className: "grid grid-cols-3 gap-1" },
  month: ({ context }) => ({ className: cellClasses(context, "w-auto px-2 capitalize") }),
  year: ({ context }) => ({ className: cellClasses(context, "w-auto px-2") }),
  buttonbar: { className: "flex justify-between pt-2 mt-2 border-t border-fx-border" },
  todayButton: { root: { className: SMALL_TEXT_BUTTON } } as unknown as Nested,
  clearButton: { root: { className: SMALL_TEXT_BUTTON } } as unknown as Nested,
  transition: {
    timeout: { enter: 120, exit: 100 },
    classNames: {
      enter: "opacity-0",
      enterActive: "!opacity-100 transition-opacity duration-fx-fast ease-fx",
      exit: "opacity-100",
      exitActive: "!opacity-0 transition-opacity duration-fx-fast ease-fx",
    },
  },
};
