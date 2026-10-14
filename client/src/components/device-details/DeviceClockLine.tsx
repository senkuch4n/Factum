"use client";

import { AlertTriangle, Clock, Info, RefreshCw } from "lucide-react";
import { cn } from "@/lib/utils";
import { FxBanner } from "@/components/feedback/FxBanner";
import {
  clockReasonLabel, describeDeviceSkew, formatDuration, type DeviceDetailsUi,
} from "@/lib/device-details";

/** Botón de texto de la fila (mismo look que "Ver todo" de la ficha). */
const TEXT_BUTTON =
  "inline-flex min-h-8 cursor-pointer items-center gap-1 rounded-fx-sm border-0 bg-transparent p-0 text-xs font-medium text-fx-accent-text underline-offset-2 hover:underline fx-focus-ring disabled:cursor-wait disabled:opacity-60 [@media(pointer:coarse)]:min-h-11";

/**
 * Fila compacta del paso 2 (metadatos-dispositivo §10.2): diferencia de reloj
 * del celular con esta PC + "Ver datos técnicos". Estados: leyendo (con
 * `aria-busy`), lista, lectura fallida (con "Volver a leer") y Tatana viejo.
 * El aviso de umbral va aparte (`ClockSkewWarning`) para que cada contenedor
 * lo ubique con sus avisos.
 */
export function DeviceClockLine({ ui, onOpenDetails, className }: {
  ui: DeviceDetailsUi;
  onOpenDetails: () => void;
  className?: string;
}) {
  const { status, view } = ui;

  if (status === "unsupported" && !view) {
    return (
      <p className={cn("m-0 flex items-start gap-1.5 text-xs text-fx-text-2", className)}>
        <Info className="mt-px h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
        Actualizá Tatana para registrar los datos técnicos del dispositivo.
      </p>
    );
  }

  // Leyendo sin nada previo que mostrar: esqueleto de alto fijo (sin salto de layout).
  if (status === "loading" && !view) {
    return (
      <div role="status" aria-busy="true" className={cn("flex min-h-8 items-center gap-2", className)}>
        <Clock className="h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
        <span className="min-w-0 flex-1">
          <span className="block text-xs text-fx-text-2">Leyendo datos del dispositivo…</span>
          <span aria-hidden="true" className="mt-1 block h-1.5 w-2/3 rounded-full bg-fx-surface-3 motion-safe:animate-pulse" />
        </span>
      </div>
    );
  }

  if (status === "error" && !view) {
    return (
      <div role="alert" className={cn("space-y-1", className)}>
        <p className="m-0 flex items-start gap-1.5 text-xs font-medium text-fx-warning">
          <AlertTriangle className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span>No se pudieron leer los datos técnicos. <span className="font-normal text-fx-text-2">{ui.error}</span></span>
        </p>
        {ui.canReread && (
          <button type="button" onClick={ui.onReread} className={cn(TEXT_BUTTON, "pl-5")}>
            <RefreshCw className="h-3 w-3" aria-hidden="true" /> Volver a leer
          </button>
        )}
      </div>
    );
  }

  if (!view) {
    // Edición de un caso sin ficha (Tatana viejo al crearlo, o la lectura no se registró).
    if (status === "idle" && ui.canReread) {
      return (
        <div className={cn("flex flex-wrap items-center gap-x-2", className)}>
          <p className="m-0 flex items-center gap-1.5 text-xs text-fx-text-3">
            <Clock className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> Sin datos técnicos registrados
          </p>
          <button type="button" onClick={ui.onReread} className={TEXT_BUTTON}>Leer ahora</button>
        </div>
      );
    }
    return null;
  }

  const c = view.clockStart;
  const clockText = c && c.deviceMinusPcMs !== null
    ? describeDeviceSkew(c.deviceMinusPcMs, c.marginMs)
    : `No disponible (${clockReasonLabel(c?.reason)})`;
  const n = view.unavailable;

  return (
    <div className={cn("space-y-0.5", className)} aria-busy={status === "loading" || undefined}>
      <p className="m-0 flex items-start gap-1.5 text-xs text-fx-text">
        <Clock className="mt-px h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
        {/* Región viva: anuncia el resultado cuando termina "Volver a leer". */}
        <span className="min-w-0 tabular-nums" aria-live="polite">
          <span className="sr-only">Reloj del celular: </span>
          {status === "loading" ? <span className="text-fx-text-2">Leyendo datos del dispositivo…</span> : clockText}
        </span>
      </p>
      <button type="button" onClick={onOpenDetails} aria-haspopup="dialog" className={cn(TEXT_BUTTON, "pl-5")}>
        Ver datos técnicos
        {n > 0 && <span className="font-normal text-fx-text-2">· {n} no {n === 1 ? "disponible" : "disponibles"}</span>}
      </button>
    </div>
  );
}

/**
 * Aviso de umbral (D6): no bloquea y nunca sugiere corregir la hora del celular
 * (eso modificaría el dispositivo). Ícono + texto, no solo color.
 */
export function ClockSkewWarning({ thresholdSeconds, className }: { thresholdSeconds: number; className?: string }) {
  return (
    <FxBanner tone="warn" role="status" className={className}>
      <p className="m-0">El reloj del celular difiere más de {formatDuration(thresholdSeconds)} del de esta PC.</p>
      <p className="m-0 mt-0.5 text-xs font-normal leading-relaxed text-fx-text-2">
        Las horas visibles en las capturas corresponden al reloj del celular; la diferencia queda registrada en el informe.
      </p>
    </FxBanner>
  );
}
