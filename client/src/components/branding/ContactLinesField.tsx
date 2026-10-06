"use client";

import { InputText } from "primereact/inputtext";
import { Button } from "primereact/button";
import { AlertCircle, AtSign, Plus, Trash2 } from "lucide-react";
import { BRANDING_LIMITS } from "@/lib/branding";
import { cn } from "@/lib/utils";

interface Props {
  /** Prefijo de ids: cada línea es `${idPrefix}-${i}`. */
  idPrefix: string;
  lines: string[];
  onChange: (lines: string[]) => void;
  onBlurLine?: (index: number) => void;
  /** Error de cada línea (por índice del formulario). */
  lineErrors: (string | undefined)[];
  /** Error de la lista (más de 6). */
  error?: string;
  disabled?: boolean;
}

const PLACEHOLDER = "Av. Siempre Viva 123, Salta · Tel. 387 …";

/**
 * Datos de contacto del membrete: hasta 6 líneas, con "Agregar línea" y
 * "Quitar" por línea (marca-por-cliente §9.4). Al quitar, el foco va a la
 * línea anterior (o a la primera), así no se pierde en el documento.
 */
export function ContactLinesField({ idPrefix, lines, onChange, onBlurLine, lineErrors, error, disabled }: Props) {
  const labelId = `${idPrefix}-label`;
  const hintId = `${idPrefix}-hint`;
  const errorId = `${idPrefix}-error`;
  const full = lines.length >= BRANDING_LIMITS.contactLines;

  function update(i: number, v: string) {
    onChange(lines.map((l, j) => (j === i ? v : l)));
  }

  function remove(i: number) {
    const next = lines.filter((_, j) => j !== i);
    onChange(next.length ? next : [""]);
    requestAnimationFrame(() => document.getElementById(`${idPrefix}-${Math.max(0, i - 1)}`)?.focus());
  }

  function add() {
    if (full) return;
    onChange([...lines, ""]);
    requestAnimationFrame(() => document.getElementById(`${idPrefix}-${lines.length}`)?.focus());
  }

  return (
    <div role="group" aria-labelledby={labelId} aria-describedby={[hintId, error ? errorId : null].filter(Boolean).join(" ")}>
      <p id={labelId} className="m-0 mb-1.5 flex items-start gap-1.5">
        <AtSign className="mt-px h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
        <span className="text-fx-label uppercase text-fx-text-2">Datos de contacto</span>
        <span className="ml-1.5 text-xs text-fx-text-3">(opcional)</span>
      </p>
      <ol className="m-0 flex list-none flex-col gap-2 p-0">
        {lines.map((line, i) => {
          const id = `${idPrefix}-${i}`;
          const lineError = lineErrors[i];
          const canRemove = lines.length > 1 || line !== "";
          return (
            <li key={i}>
              <div className="flex items-center gap-2">
                <InputText
                  id={id}
                  name={`marca-contacto-${i}`}
                  value={line}
                  placeholder={i === 0 ? PLACEHOLDER : undefined}
                  autoComplete="off"
                  disabled={disabled}
                  invalid={!!lineError}
                  aria-label={`Línea de contacto ${i + 1}`}
                  aria-invalid={!!lineError || undefined}
                  aria-describedby={lineError ? `${id}-error` : undefined}
                  onChange={e => update(i, e.target.value)}
                  onBlur={() => onBlurLine?.(i)}
                  className="min-w-0 flex-1"
                />
                <button
                  type="button"
                  onClick={() => remove(i)}
                  disabled={disabled || !canRemove}
                  aria-label={`Quitar la línea de contacto ${i + 1}`}
                  title="Quitar"
                  className={cn(
                    "inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-fx-md text-fx-text-2 md:h-10 md:w-10",
                    "transition-colors duration-fx-fast ease-fx hover:bg-fx-danger-soft hover:text-fx-danger fx-focus-ring",
                    "disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-fx-text-2",
                  )}
                >
                  <Trash2 className="h-4 w-4" aria-hidden="true" />
                </button>
              </div>
              {lineError && (
                <p id={`${id}-error`} role="alert" className="m-0 mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-danger">
                  <AlertCircle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {lineError}
                </p>
              )}
            </li>
          );
        })}
      </ol>
      <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
        <Button
          type="button"
          size="small"
          severity="secondary"
          text
          label="Agregar línea"
          icon={<Plus className="h-3.5 w-3.5" aria-hidden="true" />}
          disabled={disabled || full}
          onClick={add}
          className="max-md:min-h-11"
        />
        <p id={hintId} className="m-0 text-xs tabular-nums text-fx-text-3">
          {lines.length} de {BRANDING_LIMITS.contactLines} líneas
        </p>
      </div>
      {error && (
        <p id={errorId} role="alert" className="m-0 mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-danger">
          <AlertCircle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {error}
        </p>
      )}
    </div>
  );
}
