"use client";

import { Button } from "primereact/button";
import { Loader2, UploadCloud, X } from "lucide-react";
import { formatBytes, formatBytesPair } from "@/lib/format";
import type { UploadPhase, UploadProgress } from "@/types";

export const UPLOAD_PHASE_LABELS: Record<UploadPhase, string> = {
  checking: "Verificando espacio…",
  preparing: "Preparando desde el agente…",
  uploading: "Subiendo al servidor…",
  finishing: "Verificando en el servidor…",
};

/**
 * Panel del envío de evidencia (subida-archivos-grandes, SDD §6.7): archivo
 * actual "i de n", bytes, barra real, fase y "Cancelar envío". La barra anima
 * con `transform` (no `width`) y solo con motion-safe. Los anuncios para
 * lector de pantalla los hace `UploadLiveRegion`, montada siempre aparte.
 */
export function UploadProgressPanel({ progress, onCancel }: { progress: UploadProgress; onCancel: () => void }) {
  const { index, total, name, phase, loaded, totalBytes } = progress;
  const known = totalBytes != null && totalBytes > 0;
  const pct = known ? Math.min(100, Math.round((loaded / totalBytes) * 100)) : null;
  const ratio = known ? Math.min(1, loaded / totalBytes) : 0;
  const valueText = known
    ? formatBytesPair(loaded, totalBytes).replace(" / ", " de ")
    : loaded > 0 ? `${formatBytes(loaded)} recibidos` : UPLOAD_PHASE_LABELS[phase];
  const sent = index - 1;

  return (
    <section
      aria-labelledby="upload-progress-title"
      aria-busy="true"
      className="fx-card space-y-3 p-4 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
    >
      <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
        <div className="min-w-0">
          <h3 id="upload-progress-title" className="m-0 flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">
            <UploadCloud className="h-4 w-4 shrink-0 text-fx-accent-text" aria-hidden="true" />
            <span className="tabular-nums">Enviando {index} de {total}</span>
          </h3>
          <p className="m-0 mt-0.5 text-xs text-fx-text-3 tabular-nums">
            {sent} de {total} enviados
          </p>
        </div>
        <Button
          type="button"
          outlined
          severity="secondary"
          size="small"
          icon={<X className="h-3.5 w-3.5" aria-hidden="true" />}
          label="Cancelar envío"
          onClick={onCancel}
          className="min-h-11 w-full sm:w-auto"
        />
      </div>

      <div className="flex items-baseline justify-between gap-3 text-xs">
        <span translate="no" title={name} className="min-w-0 flex-1 truncate font-mono text-fx-text-2">
          {name}
        </span>
        {(known || loaded > 0) && (
          <span className="shrink-0 whitespace-nowrap tabular-nums text-fx-text-2">{formatBytesPair(loaded, known ? totalBytes : null)}</span>
        )}
      </div>

      <div
        role="progressbar"
        aria-label={`Progreso de ${name}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={pct ?? undefined}
        aria-valuetext={valueText}
        className="relative h-1.5 w-full overflow-hidden rounded-full bg-fx-surface-3"
      >
        {known ? (
          <span
            className="absolute inset-0 origin-left rounded-full bg-fx-accent motion-safe:transition-transform motion-safe:duration-fx-base motion-safe:ease-fx"
            style={{ transform: `scaleX(${ratio})` }}
          />
        ) : (
          // Sin tamaño conocido: barra indeterminada (estática con reduced motion).
          <span className="absolute inset-y-0 left-0 w-2/5 rounded-full bg-fx-accent motion-safe:animate-[fx-indeterminate_1.4s_var(--fx-ease-out)_infinite] motion-reduce:w-full motion-reduce:opacity-40" />
        )}
      </div>

      <p className="m-0 flex items-center gap-1.5 text-xs text-fx-text-2">
        <Loader2 className="h-3 w-3 shrink-0 motion-safe:animate-spin" aria-hidden="true" />
        {UPLOAD_PHASE_LABELS[phase]}
      </p>
    </section>
  );
}

/**
 * Región `aria-live` (visualmente oculta) del envío. Va montada siempre (así
 * el lector anuncia el primer cambio) y su texto solo cambia con el archivo o
 * la fase, nunca con cada porcentaje.
 */
export function UploadLiveRegion({ progress }: { progress: UploadProgress | null }) {
  const text = progress
    ? `Enviando ${progress.index} de ${progress.total}: ${progress.name}. ${UPLOAD_PHASE_LABELS[progress.phase]}`
    : "";
  return (
    <div aria-live="polite" aria-atomic="true" className="sr-only">
      {text}
    </div>
  );
}
