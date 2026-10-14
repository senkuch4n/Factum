"use client";

import { useState } from "react";
import { Button } from "primereact/button";
import { SelectButton } from "primereact/selectbutton";
import { AlertTriangle, Info, Loader2, Play, Trash2 } from "lucide-react";
import type { RecordingValidation, VideoVariant, VideoVariantStatus } from "@/lib/agent";
import { formatElapsed } from "./capture/ElapsedTime";
import { agentFileURL } from "@/lib/agent";
import { MEDIA_SURFACE } from "./capture/media";
import type { CapturedFile } from "@/types";

/** Orden de presentación de las variantes (las desconocidas, al final). */
const LABEL_ORDER = ["Suavizado", "RIFE"];
const labelRank = (label: string) => {
  const i = LABEL_ORDER.indexOf(label);
  return i === -1 ? LABEL_ORDER.length : i;
};

/** Nombre visible de una variante ("versión suavizada", "versión RIFE"…). */
export function variantDisplayName(label: string) {
  return label === "Suavizado" ? "versión suavizada" : `versión ${label}`;
}

/**
 * Nota discreta para una variante terminada sin pestaña (SDD §6.6), o `null` si
 * no se muestra nada: `ready` (aparece su pestaña), quitada por el perito, o RIFE
 * omitido por duración (es un extra opcional).
 */
function variantNote(s: VideoVariantStatus): string | null {
  const name = variantDisplayName(s.label);
  switch (s.state) {
    case "failed":
    case "timeout":
      return `No se pudo generar la ${name}. El original no se modificó.`;
    case "cancelled":
      if (s.reason === "removed") return null;
      if (s.reason === "agent_update" || s.reason === "agent_stopping") {
        return `Se canceló la ${name} porque Tatana se reinició. El original no se modificó.`;
      }
      return `No se pudo generar la ${name}. El original no se modificó.`;
    case "skipped":
      if (s.reason === "insufficient_disk") {
        return `Sin espacio en disco: no se generó la ${name}. El original no se modificó.`;
      }
      if (s.reason === "too_long") return s.label === "Suavizado" ? `Video largo: no se genera la ${name}.` : null;
      return null;
    default:
      return null;
  }
}

/** Reproductor de una grabación del agente con sus versiones (original + variantes). */
export function VideoCard({
  file, variants, variantStatus = NO_STATUS, storedInCase = false, onRemove, removeDisabled, removable = true, originalURL, validation,
}: {
  file: CapturedFile;
  /**
   * URL del "Original". Guardado en la carpeta del caso (flujo agent), sale de
   * ahí; si no, de la raíz de Tatana. Las variantes siguen siempre en la raíz.
   */
  originalURL?: string;
  variants: VideoVariant[];
  /**
   * Estado del post-proceso de cada variante (ios-grabacion-larga). Solo visual:
   * el original se guarda sin esperarlo. Vacío en AirPlay, `on_device`, Android
   * o con un Tatana anterior a `video_postprocess_v1`.
   */
  variantStatus?: VideoVariantStatus[];
  /** El original ya está en la carpeta del caso (cambia el texto de ayuda). */
  storedInCase?: boolean;
  onRemove: () => void;
  /** Durante el envío de evidencia no se puede quitar. */
  removeDisabled?: boolean;
  /** `false`: sin botón de quitar (tramos de la grabación de la sesión, D-T6). */
  removable?: boolean;
  /**
   * Validación de Tatana (grabacion-android-robusta §7.8). `ok` muestra la
   * duración; `incomplete`, un aviso no bloqueante. Sin ella (iOS, Tatana viejo
   * o recarga sin `sessionStorage`), como antes.
   */
  validation?: RecordingValidation;
}) {
  const [activeTab, setActiveTab] = useState("original");

  const allTabs = [
    { id: "original", label: "Original", url: originalURL ?? agentFileURL(file.name) },
    ...variants.map(v => ({ id: v.label, url: agentFileURL(v.filename), label: v.label })),
  ];
  const current = allTabs.find(t => t.id === activeTab) ?? allTabs[0];
  const ordered = [...variantStatus].sort((a, b) => labelRank(a.label) - labelRank(b.label));
  const inProgress = ordered.find(s => s.state === "pending" || s.state === "running");
  const notes = ordered.map(variantNote).filter((n): n is string => n !== null);
  const progress = inProgress?.state === "running" && typeof inProgress.progress === "number"
    ? Math.max(0, Math.min(100, Math.round(inProgress.progress)))
    : null;
  const inProgressName = inProgress ? variantDisplayName(inProgress.label) : "";
  const hasIndicator = !!inProgress || notes.length > 0;
  const showTabs = allTabs.length > 1 || hasIndicator;
  const duration = typeof validation?.duration_seconds === "number" ? validation.duration_seconds : null;
  const incomplete = validation?.status === "incomplete";

  return (
    <div className="overflow-hidden rounded-fx-lg border border-fx-border">
      <div className={MEDIA_SURFACE}>
        {/* contenido de imagen */}
        <video
          key={current.url}
          src={current.url}
          controls
          playsInline
          className="block max-h-[400px] w-full object-contain"
          preload="metadata"
        />
      </div>
      <div className="space-y-2 bg-fx-surface-2 px-3 py-2.5">
        {showTabs && (
          <div className="flex flex-wrap items-center gap-2">
            <SelectButton
              value={activeTab}
              onChange={e => e.value && setActiveTab(e.value)}
              allowEmpty={false}
              options={allTabs}
              optionLabel="label"
              optionValue="id"
              pt={{ root: { "aria-label": "Versión del video" } }}
            />
            {inProgress && (
              <span className="inline-flex items-center gap-1.5 text-xs text-fx-text-2">
                <Loader2 className="h-3 w-3 shrink-0 animate-spin motion-reduce:animate-none" aria-hidden="true" />
                {/* El texto anunciado va en el `role="status"` de abajo; el porcentaje, aparte. */}
                <span aria-hidden="true">Generando {inProgressName}…</span>
                {progress !== null && (
                  <span
                    role="progressbar"
                    aria-label={`Avance de la ${inProgressName}`}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-valuenow={progress}
                    className="inline-flex items-center gap-1.5"
                  >
                    <span className="relative h-1 w-16 overflow-hidden rounded-full bg-fx-surface-3" aria-hidden="true">
                      <span
                        className="absolute inset-0 origin-left rounded-full bg-fx-accent motion-safe:transition-transform motion-safe:duration-fx-base motion-safe:ease-fx"
                        style={{ transform: `scaleX(${progress / 100})` }}
                      />
                    </span>
                    <span className="tabular-nums" aria-hidden="true">{progress}&nbsp;%</span>
                  </span>
                )}
              </span>
            )}
          </div>
        )}
        {inProgress && (
          <p className="m-0 text-xs text-fx-text-3">
            {storedInCase ? "El original ya está guardado en el caso." : "El original no se modifica."}{" "}
            La {inProgressName} es solo para ver mejor el video.
          </p>
        )}
        {/* Siempre montado y solo con el estado: el lector de pantalla anuncia los cambios
            sin robar el foco ni leer el porcentaje cada pocos segundos. */}
        <div role="status" aria-atomic="true" className="space-y-1 empty:hidden">
          {inProgress && <span className="sr-only">Generando {inProgressName}</span>}
          {notes.map(n => (
            <p key={n} className="m-0 flex items-start gap-1.5 text-xs text-fx-text-3">
              <Info className="mt-px h-3 w-3 shrink-0" aria-hidden="true" />
              {n}
            </p>
          ))}
        </div>
        {incomplete && (
          <p role="status" className="m-0 flex items-start gap-2 rounded-fx-md border border-fx-warning bg-fx-warning-soft px-2.5 py-2 text-xs font-medium text-fx-text">
            <AlertTriangle className="mt-px h-3.5 w-3.5 shrink-0 text-fx-warning" aria-hidden="true" />
            <span>
              Grabación incompleta: dura {duration !== null ? formatElapsed(duration) : "menos"} de{" "}
              {formatElapsed(validation.recorded_seconds)} grabados. Se guardó lo que se pudo capturar.
            </span>
          </p>
        )}
        <div className="flex items-center gap-2">
          <Play className="h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
          <span translate="no" className="min-w-0 flex-1 truncate font-mono text-xs text-fx-text-2" title={file.name}>
            {file.name}
          </span>
          {validation?.status === "ok" && duration !== null && (
            <span className="shrink-0 text-xs tabular-nums text-fx-text-2" title="Duración verificada por Tatana">
              <span className="sr-only">Duración: </span>{formatElapsed(duration)}
            </span>
          )}
          {removable && (
            <Button
              type="button"
              text
              severity="danger"
              size="small"
              icon={<Trash2 className="h-3.5 w-3.5" aria-hidden="true" />}
              aria-label={`Eliminar ${file.name}`}
              onClick={onRemove}
              disabled={removeDisabled}
            />
          )}
        </div>
      </div>
    </div>
  );
}

/* Default estable (un `[]` literal en el destructuring sería nuevo en cada render). */
const NO_STATUS: VideoVariantStatus[] = [];
