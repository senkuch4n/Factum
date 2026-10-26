"use client";

import { useState } from "react";
import { Checkbox } from "primereact/checkbox";
import { Button } from "primereact/button";
import { FolderTree, Loader2, X } from "lucide-react";
import type { AgentExtractionEstimate, AgentExtractionPreflight, ExtractionCategoryKey } from "@/lib/agent";
import {
  FILE_CATEGORIES, STRUCTURED_CATEGORIES, isCategoryAvailable, type ExtractionSelection,
} from "@/lib/extraction";
import { StepHeader } from "@/components/wizard/StepHeader";
import { DeviceFolderPicker } from "./DeviceFolderPicker";
import { formatBytes } from "@/lib/format";

interface Props {
  serial: string;
  preflight: AgentExtractionPreflight;
  selection: ExtractionSelection;
  onChange: (next: ExtractionSelection) => void;
  estimate: AgentExtractionEstimate | null;
  estimateLoading: boolean;
}

/** Paso 2 (§10.4.2): elegir categorías, carpetas y la verificación en el celular. */
export function SelectionStep({ serial, preflight, selection, onChange, estimate, estimateLoading }: Props) {
  const [pickerOpen, setPickerOpen] = useState(false);

  function toggleKey(key: ExtractionCategoryKey) {
    const has = selection.keys.includes(key);
    onChange({ ...selection, keys: has ? selection.keys.filter(k => k !== key) : [...selection.keys, key] });
  }

  const connectorReason = preflight.connector.reason;

  return (
    <div className="space-y-6">
      <StepHeader title="¿Qué extraer?" description="Elegí las categorías y, si querés, carpetas puntuales del celular." />

      {/* Datos del sistema (estructurados) */}
      <section aria-labelledby="sel-structured" className="space-y-2">
        <h3 id="sel-structured" className="text-fx-label uppercase text-fx-text-2">Datos del sistema</h3>
        <ul className="m-0 list-none space-y-1.5 p-0">
          {STRUCTURED_CATEGORIES.map(c => {
            const available = isCategoryAvailable(c.key, preflight);
            const checked = selection.keys.includes(c.key);
            const hint = c.key === "call_log" && !available
              ? (connectorReason === "not_released"
                ? "Esta versión de Tatana todavía no trae la app auxiliar"
                : "La app auxiliar no está disponible en este celular")
              : undefined;
            return (
              <CategoryRow
                key={c.key}
                id={`cat-${c.key}`}
                label={c.label}
                checked={checked && available}
                disabled={!available}
                hint={hint}
                onToggle={() => toggleKey(c.key)}
              />
            );
          })}
        </ul>
      </section>

      {/* Archivos del celular */}
      <section aria-labelledby="sel-files" className="space-y-2">
        <h3 id="sel-files" className="text-fx-label uppercase text-fx-text-2">Archivos del celular</h3>
        <ul className="m-0 list-none space-y-1.5 p-0">
          {FILE_CATEGORIES.map(c => {
            const available = isCategoryAvailable(c.key, preflight);
            const checked = selection.keys.includes(c.key);
            const bytes = estimate?.by_category?.[c.key];
            const size = checked && typeof bytes === "number" ? formatBytes(bytes) : undefined;
            return (
              <CategoryRow
                key={c.key}
                id={`cat-${c.key}`}
                label={c.label}
                checked={checked && available}
                disabled={!available}
                hint={!available ? "No existe en este celular" : undefined}
                trailing={estimateLoading && checked
                  ? <Loader2 className="h-3.5 w-3.5 animate-spin text-fx-text-3" aria-hidden="true" />
                  : size
                    ? <span className="text-xs tabular-nums text-fx-text-2">{size}</span>
                    : undefined}
                onToggle={() => toggleKey(c.key)}
              />
            );
          })}
        </ul>

        {/* Carpetas elegidas */}
        <div className="rounded-fx-md border border-fx-border bg-fx-surface-2 p-3">
          <div className="flex items-center justify-between gap-2">
            <span className="flex items-center gap-2 text-fx-body-sm font-medium text-fx-text">
              <FolderTree className="h-4 w-4 text-fx-text-2" aria-hidden="true" />
              Carpetas elegidas
            </span>
            <Button
              type="button"
              severity="secondary"
              size="small"
              label={selection.customPaths.length > 0 ? "Cambiar" : "Elegir carpetas"}
              onClick={() => setPickerOpen(true)}
              className="min-h-11"
            />
          </div>
          {selection.customPaths.length > 0 && (
            <ul className="m-0 mt-2 list-none space-y-1 p-0">
              {selection.customPaths.map(p => (
                <li key={p} className="flex items-center gap-2">
                  <span className="min-w-0 flex-1 truncate font-mono text-xs text-fx-text-2" translate="no">{p}</span>
                  <button
                    type="button"
                    onClick={() => onChange({ ...selection, customPaths: selection.customPaths.filter(x => x !== p) })}
                    aria-label={`Quitar ${p}`}
                    className="flex h-11 w-11 shrink-0 items-center justify-center rounded-fx-md text-fx-text-3 hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring"
                  >
                    <X className="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      {/* Verificación en el celular (§5.4.5) */}
      <section aria-labelledby="sel-verify" className="space-y-2">
        <h3 id="sel-verify" className="text-fx-label uppercase text-fx-text-2">Verificación</h3>
        <CategoryRow
          id="verify-on-device"
          label="Verificar también el hash en el celular (más lento)"
          checked={selection.verifyOnDevice && preflight.device_sha256_available}
          disabled={!preflight.device_sha256_available}
          hint={!preflight.device_sha256_available ? "Este celular no permite calcular el hash" : undefined}
          onToggle={() => onChange({ ...selection, verifyOnDevice: !selection.verifyOnDevice })}
        />
      </section>

      {/* Total estimado (barra fija abajo del contenido) */}
      <div className="sticky bottom-0 -mx-1 rounded-fx-md border border-fx-border bg-fx-surface-2/95 px-4 py-3 backdrop-blur">
        <p className="m-0 flex items-center justify-between gap-2 text-fx-body-sm">
          <span className="text-fx-text-2">Total estimado</span>
          <span className="font-semibold tabular-nums text-fx-text">
            {estimateLoading ? (
              <span className="inline-flex items-center gap-1.5 text-fx-text-2">
                <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> calculando…
              </span>
            ) : estimate ? formatBytes(estimate.total_bytes) : "—"}
          </span>
        </p>
      </div>

      <DeviceFolderPicker
        open={pickerOpen}
        serial={serial}
        initialSelected={selection.customPaths}
        onClose={() => setPickerOpen(false)}
        onConfirm={paths => { onChange({ ...selection, customPaths: paths }); setPickerOpen(false); }}
      />
    </div>
  );
}

function CategoryRow({
  id, label, checked, disabled, hint, trailing, onToggle,
}: {
  id: string;
  label: string;
  checked: boolean;
  disabled?: boolean;
  hint?: string;
  trailing?: React.ReactNode;
  onToggle: () => void;
}) {
  return (
    <li className={`flex items-center gap-3 rounded-fx-md px-2 py-1 ${disabled ? "opacity-60" : "hover:bg-fx-surface-2"}`}>
      <Checkbox inputId={id} checked={checked} disabled={disabled} onChange={() => { if (!disabled) onToggle(); }} />
      <label htmlFor={id} className={`flex min-w-0 flex-1 items-center gap-2 py-2 ${disabled ? "" : "cursor-pointer"}`}>
        <span className="min-w-0 flex-1 text-fx-body-sm text-fx-text">{label}</span>
        {trailing}
      </label>
      {hint && (
        <span className="shrink-0 text-right text-xs text-fx-text-3">{hint}</span>
      )}
    </li>
  );
}
