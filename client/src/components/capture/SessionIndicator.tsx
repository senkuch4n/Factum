"use client";

import { useEffect, useState } from "react";
import { Button } from "primereact/button";
import { Camera, Loader2, Play, Square, Unplug } from "lucide-react";
import type { SessionStateDto } from "@/lib/agent";
import { elapsedText, hhmm, isSessionActive, isSessionRecording } from "@/lib/recording-session";
import { cn } from "@/lib/utils";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";

interface Props {
  state: SessionStateDto;
  /** El estado vino de Tatana sin que esta pestaña lo iniciara (recarga): "desde HH:MM". */
  recovered: boolean;
  /** Último cambio a anunciar (`role="status"` aparte del cronómetro). */
  announcement: { id: number; text: string } | null;
  stopping: boolean;
  starting: boolean;
  /** Detener con confirmación (`detenido_por_perito`). Sin él (header compacto), no hay botón. */
  onStop?: () => Promise<boolean>;
  /** "Reanudar" con la sesión detenida o interrumpida (paso 3). */
  onResume?: () => void;
  /** Versión de una línea para el header del asistente (fuera del paso 3). */
  compact?: boolean;
  className?: string;
}

/** Cronómetro de 1 s, decorativo para el lector (`aria-hidden`). */
function useNow(running: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!running) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [running]);
  return now;
}

/**
 * Indicador de la grabación de la sesión (grabacion-sesion-completa §9.4):
 * sticky arriba del paso 3. El cronómetro no se anuncia; un `role="status"`
 * aparte anuncia solo los cambios (iniciada, tramo guardado, reanudada, detenida).
 */
export function SessionIndicator({
  state, recovered, announcement, stopping, starting, onStop, onResume, compact = false, className,
}: Props) {
  const [confirmOpen, setConfirmOpen] = useState(false);
  const recording = isSessionRecording(state.state);
  const active = isSessionActive(state.state);
  const waiting = state.state === "waiting_device";
  const closing = state.state === "stopping" || stopping;
  const opening = state.state === "starting";
  const now = useNow(recording);
  const segment = state.screen_segment_index ?? null;
  const since = hhmm(state.session_started_at ?? state.run_started_at);

  const label = closing ? "Cerrando la grabación de la sesión…"
    : opening ? "Iniciando la grabación de la sesión…"
    : waiting ? `Sesión cortada${state.stopped_at ? ` a las ${hhmm(state.stopped_at)}` : ""}: esperando el celular`
    : recording ? (recovered && since ? `Grabando sesión desde ${since}` : "Grabando sesión")
    : "Grabación de la sesión detenida";

  return (
    <div
      className={cn(
        compact
          ? "flex min-h-11 items-center gap-2 rounded-fx-lg border px-3 py-1.5 text-xs"
          : "sticky top-2 z-30 flex min-h-11 flex-wrap items-center gap-x-3 gap-y-2 rounded-fx-lg border px-3 py-2 shadow-fx-1",
        recording ? "border-fx-danger bg-fx-danger-soft"
          : waiting ? "border-fx-warning bg-fx-warning-soft"
          : "border-fx-border bg-fx-surface-2",
        className,
      )}
    >
      {/* Anuncio de cambios: siempre montado (si se montara junto al texto, algunos lectores no lo leen). */}
      <span className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        <span key={announcement?.id ?? 0}>{announcement?.text ?? ""}</span>
      </span>

      <span className="flex min-w-0 flex-1 items-center gap-2">
        {opening || closing ? (
          <Loader2 className="h-3.5 w-3.5 shrink-0 text-fx-text-2 motion-safe:animate-spin" aria-hidden="true" />
        ) : waiting ? (
          <Unplug className="h-3.5 w-3.5 shrink-0 text-fx-warning" aria-hidden="true" />
        ) : (
          <span
            aria-hidden="true"
            className={cn(
              "h-2.5 w-2.5 shrink-0 rounded-full",
              recording ? "bg-fx-danger motion-safe:animate-pulse" : "bg-fx-text-3",
            )}
          />
        )}
        <span className={cn("min-w-0 font-semibold text-fx-text", compact ? "truncate" : "text-fx-body-sm")}>
          {label}
          {recording && (
            <>
              <span aria-hidden="true" className="font-normal text-fx-text-2"> · </span>
              <span aria-hidden="true" className="tabular-nums text-fx-danger">{elapsedText(state.run_started_at ?? state.screen_segment_started_at, now)}</span>
              {segment !== null && <span className="font-normal text-fx-text-2"> · tramo {segment}</span>}
            </>
          )}
        </span>
        {state.camera_state === "recording" && (
          <Camera className="h-3.5 w-3.5 shrink-0 text-fx-text-2" aria-label="Con cámara del procedimiento" />
        )}
      </span>

      {!compact && active && !closing && onStop && (
        <Button
          type="button"
          size="small"
          outlined
          severity="secondary"
          icon={<Square className="h-3 w-3" aria-hidden="true" />}
          label="Detener grabación de sesión"
          onClick={() => setConfirmOpen(true)}
          aria-haspopup="dialog"
          className="min-h-11 shrink-0 sm:min-h-9"
        />
      )}
      {!compact && !active && onResume && (
        <Button
          type="button"
          size="small"
          severity="secondary"
          icon={starting
            ? <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
            : <Play className="h-3.5 w-3.5" aria-hidden="true" />}
          label="Reanudar"
          onClick={() => { if (!starting) onResume(); }}
          aria-disabled={starting || undefined}
          className={cn("min-h-11 shrink-0 sm:min-h-9", starting && "cursor-not-allowed opacity-60")}
        />
      )}

      {onStop && (
        <ConfirmDialog
          open={confirmOpen}
          onOpenChange={setConfirmOpen}
          onConfirm={() => onStop()}
          tone="neutral"
          title="¿Detener la grabación de la sesión?"
          description="Va a quedar un hueco en el registro."
          confirmLabel="Detener"
          cancelLabel="Seguir grabando"
        />
      )}
    </div>
  );
}
