/**
 * Piezas compartidas del preset pass-through de Factum.
 *
 * Reglas del preset (T12): solo clases `fx-*` que leen tokens `--fx-*`.
 * Nada de colores de la paleta Tailwind, nada de `dark:` (el modo lo
 * resuelven los tokens con la clase `.dark` de <html>) y nada de hex.
 */

/** Anillo de foco estándar: 2px `--fx-focus` con offset de 2px (T4). */
export const FOCUS_RING =
  "outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fx-focus";

/** Botón de ícono "cerrar" de Dialog y Toast. */
export const CLOSE_BUTTON = [
  "inline-flex items-center justify-center shrink-0 w-8 h-8 p-0 border-0 cursor-pointer",
  "rounded-fx-md bg-transparent text-fx-text-2",
  "transition-colors duration-fx-fast ease-fx",
  "hover:bg-fx-surface-3 hover:text-fx-text",
  FOCUS_RING,
].join(" ");

/**
 * Look del Button primario de Prime para un <a>/<Link> (p. ej. la CTA de la
 * 404). Mismas clases que la variante default de pt/button.ts, sin el
 * variante `enabled:` (un link no es un control de formulario).
 */
export const FX_BUTTON_PRIMARY = [
  "inline-flex items-center justify-center gap-2 select-none whitespace-nowrap no-underline",
  "border rounded-fx-md font-bold px-5 py-2.5 text-fx-body-sm",
  "bg-fx-accent border-fx-accent text-fx-on-accent",
  "hover:bg-fx-accent-hover hover:border-fx-accent-hover active:bg-fx-accent-active active:border-fx-accent-active",
  "transition-[background-color,border-color,color,transform] duration-fx-fast ease-fx motion-safe:active:translate-y-px",
  FOCUS_RING,
].join(" ");
