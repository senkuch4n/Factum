"use client";

import { forwardRef } from "react";
import { Password, type PasswordProps } from "primereact/password";
import type { IconOptions } from "primereact/utils";
import { Eye, EyeOff } from "lucide-react";
import { cn } from "@/lib/utils";

/* Lugar para el botón de 44 px a la derecha. Con `!` porque Prime mergea el
   pt global de inputtext (px-3) DESPUÉS de las clases del consumidor y
   tailwind-merge descartaría un pr-12 común. */
const TOGGLE_ROOM = "!pr-12";

type ToggleHandler = React.MouseEventHandler<HTMLButtonElement>;

function ToggleButton({ shown, onToggle, disabled }: { shown: boolean; onToggle?: ToggleHandler; disabled?: boolean }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      disabled={disabled}
      aria-label={shown ? "Ocultar contraseña" : "Mostrar contraseña"}
      className="absolute right-0.5 top-1/2 inline-flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-fx-md text-fx-text-3 transition-colors duration-fx-fast ease-fx enabled:hover:text-fx-text disabled:cursor-not-allowed disabled:text-fx-text-disabled fx-focus-ring"
    >
      {shown ? <EyeOff className="h-4 w-4" aria-hidden="true" /> : <Eye className="h-4 w-4" aria-hidden="true" />}
    </button>
  );
}

/* Prime llama a estas funciones con `iconProps` = { role="switch", tabIndex,
   aria-checked, onKeyDown, onClick, ... }. Solo se toma `onClick` (el
   toggleMask interno): el resto es la semántica que se reemplaza. */
function toggleFrom(options: IconOptions<PasswordProps>): ToggleHandler | undefined {
  return (options.iconProps as React.HTMLProps<HTMLElement>).onClick as ToggleHandler | undefined;
}

/**
 * Password de PrimeReact con el control de mostrar/ocultar accesible.
 *
 * El ícono por defecto de Prime es un <svg role="switch"> con nombres en
 * inglés, `aria-checked` invertido y, al alternar, React cambia EyeIcon por
 * EyeSlashIcon (tipos distintos): el nodo enfocado se desmonta y el foco se
 * pierde. Acá `showIcon` y `hideIcon` devuelven el mismo tipo de elemento en
 * la misma posición, así React reusa el <button> y el foco se conserva.
 * El nombre accesible cambia ("Mostrar contraseña" ↔ "Ocultar contraseña"),
 * sin `aria-pressed` (no se combinan las dos técnicas).
 *
 * Es la única forma permitida de usar Password en la app (ver pt/password.ts).
 * `feedback` y `toggleMask` vienen prendido/apagado por defecto para login;
 * el consumidor puede pisarlos.
 */
export const FxPassword = forwardRef<Password, PasswordProps>(function FxPassword(props, ref) {
  return (
    <Password
      ref={ref}
      feedback={false}
      toggleMask
      {...props}
      inputClassName={cn(TOGGLE_ROOM, props.inputClassName)}
      showIcon={(o) => <ToggleButton shown={false} disabled={props.disabled} onToggle={toggleFrom(o)} />}
      hideIcon={(o) => <ToggleButton shown disabled={props.disabled} onToggle={toggleFrom(o)} />}
    />
  );
});
