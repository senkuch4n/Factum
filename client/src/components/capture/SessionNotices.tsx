"use client";

import { Button } from "primereact/button";
import { HardDrive, Loader2, Play, PlayCircle, Unplug } from "lucide-react";
import type { SessionStateDto, SessionWarning } from "@/lib/agent";
import { hhmm, isSessionActive, warningText } from "@/lib/recording-session";
import { cn } from "@/lib/utils";
import { FxBanner } from "@/components/feedback/FxBanner";
import type { SessionInterruption } from "@/hooks/useRecordingSession";

interface Props {
  state: SessionStateDto | null;
  interruption: SessionInterruption | null;
  resumeNotice: { at: string | null; index: number | null } | null;
  starting: boolean;
  onResume: () => void;
  onDismissInterruption: () => void;
  onDismissResume: () => void;
}

/** Orden fijo de los avisos de Tatana; uno por tipo. */
const WARNING_ORDER: SessionWarning["kind"][] = ["disk_low", "screen_off", "screen_locked", "rotation_delayed"];

/**
 * Avisos de la grabación de la sesión (grabacion-sesion-completa §9.4): `FxBanner`
 * no modales (nunca tapan "Pantalla") y sin robar foco. Las acciones "Reanudar"
 * van en el aviso del corte que las necesita.
 */
export function SessionNotices({
  state, interruption, resumeNotice, starting, onResume, onDismissInterruption, onDismissResume,
}: Props) {
  const active = isSessionActive(state?.state);
  const stoppedAt = hhmm(state?.stopped_at) ?? "--:--";
  const warnings = (state?.warnings ?? []).filter(w => active && warningText(w));
  const byKind = new Map(warnings.map(w => [w.kind, w] as const));

  const resumeButton = (
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
      className={cn("mt-2.5 min-h-11 sm:min-h-0", starting && "cursor-not-allowed opacity-60")}
    />
  );

  const stoppedReason = !active && state?.state === "stopped" ? state.stop_reason ?? null : null;

  return (
    <>
      {state?.state === "waiting_device" && (
        <FxBanner tone="warn" icon={<Unplug className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
          Cable desconectado: la grabación de la sesión se cortó a las {stoppedAt}. Se reanuda sola al volver a conectar el mismo celular.
        </FxBanner>
      )}

      {resumeNotice && (
        <FxBanner tone="info" icon={<PlayCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />} onClose={onDismissResume}>
          La grabación de la sesión se reanudó a las {hhmm(resumeNotice.at) ?? "--:--"}
          {resumeNotice.index !== null ? ` (tramo ${resumeNotice.index})` : ""}.
        </FxBanner>
      )}

      {interruption && (
        <FxBanner tone="warn" onClose={onDismissInterruption}>
          <p className="m-0">
            Tatana se cerró a las {hhmm(interruption.at) ?? "--:--"}. El tramo en curso se recuperó y se guardó en el caso.
          </p>
          {!active && state?.state === "interrupted" && resumeButton}
        </FxBanner>
      )}

      {stoppedReason === "sin_espacio" && (
        <FxBanner tone="error" icon={<HardDrive className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
          <p className="m-0">
            Se detuvo la grabación de la sesión por falta de espacio a las {stoppedAt}. Liberá espacio en el disco y tocá «Reanudar».
          </p>
          {resumeButton}
        </FxBanner>
      )}

      {stoppedReason === "fallo_grabacion" && (
        <FxBanner tone="warn">
          <p className="m-0">La grabación de la sesión se interrumpió por un error a las {stoppedAt}.</p>
          {resumeButton}
        </FxBanner>
      )}

      {WARNING_ORDER.map(kind => {
        const w = byKind.get(kind);
        if (!w) return null;
        return (
          <FxBanner
            key={kind}
            tone="warn"
            icon={kind === "disk_low" ? <HardDrive className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" /> : undefined}
          >
            {warningText(w)}
          </FxBanner>
        );
      })}
    </>
  );
}

/** El indicador ofrece "Reanudar" solo si ningún aviso lo ofrece ya (sin botones repetidos). */
export function noticeOffersResume(state: SessionStateDto | null, interruption: SessionInterruption | null): boolean {
  if (!state) return false;
  if (state.state === "interrupted" && interruption) return true;
  return state.state === "stopped" && (state.stop_reason === "sin_espacio" || state.stop_reason === "fallo_grabacion");
}
