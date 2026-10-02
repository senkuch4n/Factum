"use client";

import { AlertCircle } from "lucide-react";

/** `aria-describedby` para un control dentro de `FormField` (ayuda + error). */
export function describedBy(id: string, opts: { hint?: string; error?: string }): string | undefined {
  const ids = [opts.hint ? `${id}-hint` : null, opts.error ? `${id}-error` : null].filter(Boolean);
  return ids.length ? ids.join(" ") : undefined;
}

export function FormField({
  id, label, sublabel, icon: Icon, error, required, hint, labelAside, children,
}: {
  id?: string; label: string; sublabel?: string; icon: React.ElementType;
  error?: string; required?: boolean;
  /** Ayuda persistente debajo del control (id `${id}-hint`, para aria-describedby). */
  hint?: string;
  /**
   * Control chico alineado a la derecha de la etiqueta (p. ej. "Administrar
   * sugerencias"). Va fuera del `<label>` para no anidar controles interactivos.
   */
  labelAside?: React.ReactNode;
  children: React.ReactNode;
}) {
  const labelEl = (className: string) => (
    <label htmlFor={id} className={`${className} flex items-start gap-1.5`}>
      <Icon className="mt-px h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
      <span className="min-w-0">
        <span className="text-fx-label uppercase text-fx-text-2">{label}</span>
        {required && <span aria-hidden="true" className="ml-1 text-fx-danger">*</span>}
        {sublabel && <span className="ml-1.5 text-xs normal-case text-fx-text-3">{sublabel}</span>}
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
      {hint && (
        <p id={id ? `${id}-hint` : undefined} className="m-0 mt-1.5 text-xs leading-snug text-fx-text-3">
          {hint}
        </p>
      )}
      {error && (
        <p
          id={id ? `${id}-error` : undefined}
          role="alert"
          className="m-0 mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-danger motion-safe:animate-[fx-fade-in_var(--fx-dur-fast)_var(--fx-ease-out)_both]"
        >
          <AlertCircle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {error}
        </p>
      )}
    </div>
  );
}
