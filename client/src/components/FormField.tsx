"use client";

import { createContext, useContext } from "react";
import { AlertCircle } from "lucide-react";

/**
 * Estilo de etiqueta de `FormField`:
 * - `"eyebrow"` (default, el de toda la app): ícono + MAYÚSCULAS con tracking.
 * - `"field"` (formulario de la causa, formulario-causa-rediseno reintento 1):
 *   sentence case, peso medio, sin ícono, en una línea; "Opcional" tenue a la
 *   derecha y las aclaraciones como ayuda debajo del control.
 * Va por contexto para que los campos anidados (perfil, integrantes) lo tomen
 * sin pasar props por cada nivel.
 */
export type FormFieldLabelStyle = "eyebrow" | "field";
export const FormFieldStyleContext = createContext<FormFieldLabelStyle>("eyebrow");

/** `aria-describedby` para un control dentro de `FormField` (ayuda + error). */
export function describedBy(id: string, opts: { hint?: string; error?: string }): string | undefined {
  const ids = [opts.hint ? `${id}-hint` : null, opts.error ? `${id}-error` : null].filter(Boolean);
  return ids.length ? ids.join(" ") : undefined;
}

export function FormField({
  id, labelId, label, sublabel, icon, error, required, optional, hint, labelAside, labelBadge, children,
}: {
  id?: string;
  /** id del `<label>`, para un control que se nombra con `aria-labelledby` (p. ej. el editor del informe). */
  labelId?: string;
  label: string; sublabel?: string; icon: React.ElementType;
  error?: string; required?: boolean;
  /** Campo opcional: "· opcional" (eyebrow) o "Opcional" tenue a la derecha (field). */
  optional?: boolean;
  /** Ayuda persistente debajo del control (id `${id}-hint`, para aria-describedby). */
  hint?: string;
  /**
   * Control chico alineado a la derecha de la etiqueta (p. ej. "Administrar
   * sugerencias"). Va fuera del `<label>` para no anidar controles interactivos.
   */
  labelAside?: React.ReactNode;
  /** Marca no interactiva dentro del `<label>`, después del sublabel (p. ej. "tomado de la causa anterior"). */
  labelBadge?: React.ReactNode;
  children: React.ReactNode;
}) {
  // `ElementType` a secas también incluye las etiquetas JSX que registra la librería 3D (augment global de JSX): se acota a algo que acepta className.
  const Icon = icon as React.ElementType<{ className?: string }>;
  const labelStyle = useContext(FormFieldStyleContext);
  const hintEl = hint && (
    <p id={id ? `${id}-hint` : undefined} className={labelStyle === "field" ? "m-0 mt-2 text-xs leading-snug text-fx-text-3" : "m-0 mt-1.5 text-xs leading-snug text-fx-text-3"}>
      {hint}
    </p>
  );
  const errorEl = error && (
    <p
      id={id ? `${id}-error` : undefined}
      role="alert"
      className="m-0 mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-danger motion-safe:animate-[fx-fade-in_var(--fx-dur-fast)_var(--fx-ease-out)_both]"
    >
      <AlertCircle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {error}
    </p>
  );

  if (labelStyle === "field") {
    // Fila de etiqueta de alto fijo (20 px): los controles de una misma fila de
    // la grilla quedan alineados aunque una etiqueta lleve marca u "Opcional".
    return (
      <div>
        <div className="mb-2 flex h-5 items-center gap-2">
          <label id={labelId} htmlFor={id} className="min-w-0 truncate text-fx-body-sm font-medium text-fx-text" title={label}>
            {label}
            {sublabel && <span className="ml-1.5 font-normal text-fx-text-3">{sublabel}</span>}
          </label>
          {labelBadge && <span className="inline-flex shrink-0">{labelBadge}</span>}
          {(optional || labelAside) && (
            <span className="ml-auto flex shrink-0 items-center gap-1.5">
              {optional && <span className="text-xs text-fx-text-3">Opcional</span>}
              {labelAside && <span className="-my-2 flex">{labelAside}</span>}
            </span>
          )}
        </div>
        {children}
        {hintEl}
        {errorEl}
      </div>
    );
  }

  const labelEl = (className: string) => (
    <label id={labelId} htmlFor={id} className={`${className} flex items-start gap-1.5`}>
      <Icon className="mt-px h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
      <span className="min-w-0">
        <span className="text-fx-label uppercase text-fx-text-2">{label}</span>
        {required && <span aria-hidden="true" className="ml-1 text-fx-danger">*</span>}
        {sublabel && <span className="ml-1.5 text-xs normal-case text-fx-text-3">{sublabel}</span>}
        {optional && !sublabel && <span className="ml-1.5 text-xs normal-case text-fx-text-3">· opcional</span>}
        {labelBadge}
      </span>
    </label>
  );
  return (
    <div>
      {/* Ícono fijo a la izquierda y el texto fluye al lado: con flex-wrap una
          etiqueta larga en media columna dejaba el ícono solo en su línea. */}
      {labelAside ? (
        // Con control al costado: la fila es un flex y el `<label>` ocupa el resto.
        // `-my-2` absorbe el alto del control (32 px) para que la fila mida lo
        // mismo que una etiqueta sola y los inputs de la grilla queden alineados.
        <div className="mb-1.5 flex items-center gap-2">
          {labelEl("min-w-0 flex-1")}
          <div className="-my-2 flex shrink-0">{labelAside}</div>
        </div>
      ) : (
        labelEl("mb-1.5")
      )}
      {children}
      {hintEl}
      {errorEl}
    </div>
  );
}
