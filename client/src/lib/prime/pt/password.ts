import type { PasswordPassThroughOptions } from "primereact/password";

/**
 * Password. Usar siempre a través de `components/form/FxPassword.tsx`: ese
 * wrapper reemplaza el ícono de mostrar/ocultar de Prime (un <svg> con
 * role="switch", aria-checked invertido y nombres en inglés) por un <button>
 * propio con nombre accesible en español que conserva el foco al alternar.
 *
 * El look del input lo da el pt global `inputtext` (Prime renderiza un
 * InputText interno). `showIcon`/`hideIcon` solo aplican a un Password usado
 * sin FxPassword. `panel`/`meter`/`info` quedan listos por si una HU futura
 * usa `feedback` (el login no lo usa).
 */
export const password: PasswordPassThroughOptions = {
  root: { className: "relative block w-full" },
  iconField: { className: "relative block w-full" },
  input: { className: "w-full" },
  showIcon: { className: "h-4 w-4" },
  hideIcon: { className: "h-4 w-4" },
  panel: { className: "mt-1 bg-fx-surface-1 border border-fx-border rounded-fx-md shadow-fx-2 p-3 text-fx-body-sm text-fx-text" },
  meter: { className: "h-1.5 w-full overflow-hidden rounded-full bg-fx-surface-3" },
  meterLabel: { className: "h-full rounded-full bg-fx-accent transition-[width] duration-fx-base ease-fx" },
  info: { className: "mt-2 text-fx-text-2" },
};
