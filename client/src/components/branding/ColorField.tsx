"use client";

import { InputText } from "primereact/inputtext";
import { AlertTriangle, Check, Palette } from "lucide-react";
import { FormField, describedBy } from "@/components/FormField";
import {
  FACTUM_ACCENT, FACTUM_PRIMARY, checkColor, formatContrast, normalizeHex, type ColorRole,
} from "@/lib/branding";
import { cn } from "@/lib/utils";

interface Props {
  /** id del campo hex (el foco del error va acá). */
  id: string;
  label: string;
  role: ColorRole;
  /** "" = color de Factum; si no, lo que escribió el usuario. */
  value: string;
  onChange: (value: string) => void;
  onFocus?: () => void;
  onBlur?: () => void;
  error?: string;
  hint: string;
  disabled?: boolean;
}

/**
 * Color del informe: selector nativo + campo `#RRGGBB` y, al lado, el
 * contraste calculado en vivo en un badge con texto e ícono (no solo color),
 * anunciado con `aria-live="polite"` (marca-por-cliente §9.4).
 */
export function ColorField({ id, label, role, value, onChange, onFocus, onBlur, error, hint, disabled }: Props) {
  const check = checkColor(value, role);
  const fallback = `#${role === "primary" ? FACTUM_PRIMARY : FACTUM_ACCENT}`;
  const hex = normalizeHex(value);
  const swatch = hex ? `#${hex}` : fallback;
  const target = role === "primary" ? "con blanco" : "con el texto";

  return (
    <FormField id={id} icon={Palette} label={label} error={error} hint={hint}>
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <input
            type="color"
            aria-label={`${label}: selector de color`}
            value={swatch.toLowerCase()}
            disabled={disabled}
            onChange={e => onChange(e.target.value.toUpperCase())}
            onFocus={onFocus}
            onBlur={onBlur}
            className={cn(
              "h-11 w-11 shrink-0 cursor-pointer rounded-fx-md border border-fx-border-strong bg-fx-surface-2 p-1 md:h-10 md:w-10",
              "fx-focus-ring disabled:cursor-not-allowed disabled:opacity-60",
              "[&::-webkit-color-swatch-wrapper]:p-0 [&::-webkit-color-swatch]:rounded-fx-sm [&::-webkit-color-swatch]:border-0 [&::-moz-color-swatch]:rounded-fx-sm [&::-moz-color-swatch]:border-0",
            )}
          />
          <InputText
            id={id}
            name={`marca-${role}`}
            value={value}
            placeholder={value === "" ? "Color de Factum" : "#RRGGBB"}
            autoComplete="off"
            spellCheck={false}
            maxLength={9}
            disabled={disabled}
            invalid={!!error}
            aria-invalid={!!error || undefined}
            aria-describedby={[describedBy(id, { hint, error }), `${id}-contrast`].filter(Boolean).join(" ")}
            onChange={e => onChange(e.target.value)}
            onFocus={onFocus}
            onBlur={onBlur}
            className="min-w-0 flex-1 font-mono uppercase tabular-nums placeholder:normal-case placeholder:font-sans"
          />
        </div>
        <span id={`${id}-contrast`} aria-live="polite" className="inline-flex min-h-8 shrink-0 items-center">
          {check.kind === "default" && (
            <span className="inline-flex items-center gap-1.5 rounded-fx-pill border border-fx-border bg-fx-surface-2 px-2.5 py-1 text-xs font-medium text-fx-text-2">
              <span aria-hidden="true" className="h-3 w-3 rounded-full border border-fx-border" style={{ backgroundColor: fallback }} />
              Color de Factum
            </span>
          )}
          {check.kind === "ok" && (
            <span className="inline-flex items-center gap-1 rounded-fx-pill bg-fx-success-soft px-2.5 py-1 text-xs font-semibold tabular-nums text-fx-success">
              <Check className="h-3.5 w-3.5" aria-hidden="true" />
              Contraste {formatContrast(check.shown)}:1<span className="sr-only"> {target}, cumple el mínimo</span>
            </span>
          )}
          {check.kind === "low" && (
            <span className="inline-flex items-center gap-1 rounded-fx-pill bg-fx-warning-soft px-2.5 py-1 text-xs font-semibold tabular-nums text-fx-warning">
              <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
              {formatContrast(check.shown)}:1, mínimo 4.5:1<span className="sr-only"> {target}</span>
            </span>
          )}
        </span>
      </div>
    </FormField>
  );
}
