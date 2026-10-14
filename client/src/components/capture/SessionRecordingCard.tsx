"use client";

import { useId } from "react";
import { Button } from "primereact/button";
import { AlertCircle, HardDrive, Loader2, Lock, RotateCcw, Video } from "lucide-react";
import type { SessionCapacity } from "@/lib/agent";
import { capacityText, noSpaceText } from "@/lib/recording-session";
import { screenBlockMessage } from "@/lib/agent-messages";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { FxBanner } from "@/components/feedback/FxBanner";
import type { SessionStartProblem } from "@/hooks/useRecordingSession";

/** Qué muestra la tarjeta (lo decide `CaptureStep` con el estado del hook). */
export type SessionCardMode =
  /** Tatana sin `recording_session_v1` (§7.8): aviso informativo, el resto del paso igual. */
  | "outdated"
  /** iPhone: la sesión en iPhone llega en la fase 2. */
  | "ios_unsupported"
  /** Normal: switch, espacio y errores de inicio. */
  | "ready";

interface Props {
  mode: SessionCardMode;
  /** La instalación exige la sesión (`required`): switch prendido y `aria-disabled`. */
  required: boolean;
  enabled: boolean;
  segmentMinutes: number;
  capacity: SessionCapacity | null;
  /** Hay una sesión en curso de este caso: el espacio lo informa Tatana con avisos. */
  active: boolean;
  problem: SessionStartProblem | null;
  busySettings: boolean;
  busyStart: boolean;
  disabled?: boolean;
  onToggle: (enabled: boolean) => void;
  onRetryStart: () => void;
  onForceStart: () => void;
  onDismissProblem: () => void;
}

const LABEL = "m-0 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2";

/**
 * "Grabación de la recolección" (grabacion-sesion-completa §9.4): tarjeta propia
 * arriba de "Capturar del celular". Los avisos durante la sesión (cable, pantalla,
 * espacio) van aparte, en `SessionNotices`.
 */
export function SessionRecordingCard({
  mode, required, enabled, segmentMinutes, capacity, active, problem, busySettings, busyStart, disabled = false,
  onToggle, onRetryStart, onForceStart, onDismissProblem,
}: Props) {
  const descId = useId();
  const requiredId = useId();
  const titleId = useId();

  if (mode === "outdated" || mode === "ios_unsupported") {
    return (
      <section aria-labelledby={titleId} className="fx-card space-y-2 p-4">
        <h3 id={titleId} className={LABEL}>
          <Video className="h-3.5 w-3.5" aria-hidden="true" /> Grabación de la recolección
        </h3>
        <FxBanner tone="info">
          <p className="m-0">
            {mode === "outdated"
              ? "Para grabar toda la recolección hace falta actualizar Tatana."
              : "La grabación de la sesión en iPhone llega en una próxima versión de Tatana."}
          </p>
          {required && (
            <p className="m-0 mt-1 text-xs font-normal">La configuración de esta instalación exige grabar la sesión.</p>
          )}
        </FxBanner>
      </section>
    );
  }

  const on = required || enabled;
  const locked = required || disabled || busySettings;
  const capText = !active ? capacityText(capacity) : null;
  const noRoom = !active && capacity?.can_start === false;

  return (
    <section aria-labelledby={titleId} className="fx-card space-y-3 p-4">
      <h3 id={titleId} className={LABEL}>
        <Video className="h-3.5 w-3.5" aria-hidden="true" /> Grabación de la recolección
      </h3>

      <div className="flex items-start gap-3">
        <button
          type="button"
          role="switch"
          aria-checked={on}
          aria-disabled={locked || undefined}
          aria-describedby={required ? `${descId} ${requiredId}` : descId}
          aria-labelledby={`${titleId}-switch-label`}
          onClick={() => { if (!locked) onToggle(!on); }}
          className={cn(
            "relative mt-0.5 inline-flex h-6 w-11 shrink-0 items-center rounded-full border",
            "transition-colors duration-fx-fast ease-fx motion-reduce:transition-none",
            // El área táctil llega a 44 px sin cambiar el dibujo del switch.
            "before:absolute before:-inset-x-1 before:-inset-y-2.5 before:content-['']",
            FOCUS_RING,
            on ? "border-fx-accent bg-fx-accent" : "border-fx-border-strong bg-fx-surface-3",
            locked && "cursor-not-allowed opacity-60",
          )}
        >
          <span
            aria-hidden="true"
            className={cn(
              "inline-block h-4 w-4 rounded-full shadow-sm",
              "transition-transform duration-fx-fast ease-fx motion-reduce:transition-none",
              on ? "translate-x-[22px] bg-fx-on-accent" : "translate-x-[3px] bg-fx-text-3",
            )}
          />
        </button>
        <div className="min-w-0">
          <p id={`${titleId}-switch-label`} className="m-0 flex items-center gap-1.5 text-fx-body-sm font-semibold text-fx-text">
            Grabar toda la recolección
            {busySettings && <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin text-fx-text-3" aria-hidden="true" />}
          </p>
          <p id={descId} className="m-0 mt-0.5 text-xs text-fx-text-2">
            Se graba la pantalla del celular durante todo este paso, en tramos de {segmentMinutes} min con hash y sello cada uno.
          </p>
          {required && (
            <p id={requiredId} className="m-0 mt-1 flex items-start gap-1 text-xs text-fx-text-2">
              <Lock className="mt-px h-3 w-3 shrink-0" aria-hidden="true" />
              La configuración de esta instalación exige grabar la recolección.
            </p>
          )}
        </div>
      </div>

      {on && capText && !noRoom && (
        <p className="m-0 flex items-start gap-1.5 text-xs text-fx-text-3">
          <HardDrive className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span>{capText}</span>
        </p>
      )}

      {on && noRoom && !problem && (
        <p role="alert" className="m-0 flex items-start gap-1.5 text-xs font-medium text-fx-danger">
          <AlertCircle className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span>{noSpaceText(capacity?.available_bytes)}</span>
        </p>
      )}

      {problem?.kind === "screen" && (
        <FxBanner tone="warn" role="alert" icon={<Lock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />} onClose={onDismissProblem}>
          <p className="m-0">{screenBlockMessage("session")}</p>
          <div className="mt-2.5 flex flex-wrap gap-2">
            <Button
              type="button"
              size="small"
              icon={busyStart ? <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" /> : <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Reintentar"
              onClick={() => { if (!busyStart) onRetryStart(); }}
              aria-disabled={busyStart || undefined}
              className={cn("min-h-11 sm:min-h-0", busyStart && "cursor-not-allowed opacity-60")}
            />
            <Button
              type="button"
              size="small"
              severity="secondary"
              label="Grabar igual"
              onClick={() => { if (!busyStart) onForceStart(); }}
              aria-disabled={busyStart || undefined}
              className={cn("min-h-11 sm:min-h-0", busyStart && "cursor-not-allowed opacity-60")}
            />
          </div>
        </FxBanner>
      )}
      {problem && problem.kind !== "screen" && (
        <FxBanner tone={problem.kind === "foreign" ? "warn" : "error"} onClose={onDismissProblem}>
          <p className="m-0">{problem.text}</p>
          {problem.kind !== "foreign" && (
            <Button
              type="button"
              size="small"
              severity="secondary"
              icon={busyStart ? <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" /> : <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Reintentar"
              onClick={() => { if (!busyStart) onRetryStart(); }}
              aria-disabled={busyStart || undefined}
              className={cn("mt-2.5 min-h-11 sm:min-h-0", busyStart && "cursor-not-allowed opacity-60")}
            />
          )}
        </FxBanner>
      )}
    </section>
  );
}
