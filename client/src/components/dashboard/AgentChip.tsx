"use client";

import { Wifi, WifiOff } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Device } from "@/types";

interface Props {
  online: boolean;
  device: Device | null;
  recording: boolean;
}

/* Vive sobre la barra oscura de la navbar flotante: fondo *-soft y un borde
   del mismo tono al 35% (color-mix sobre currentColor), para que el estado se
   lea sin el contorno saturado que en la barra oscura quedaba "pegado". */
const PILL =
  "inline-flex h-8 items-center gap-1.5 rounded-full border border-[color:color-mix(in_srgb,currentColor_35%,transparent)] px-3 text-xs font-medium select-none";

/**
 * Estado del agente Tatana en la navbar (desde `sm`). Conectado usa el tono
 * de éxito y no disponible el de advertencia, siempre con ícono + texto; si
 * hay una grabación en curso, se suma la píldora "REC".
 */
export function AgentChip({ online, device, recording }: Props) {
  const deviceLabel = device
    ? `${device.manufacturer} ${device.model} · ${
        device.platform === "ios"
          ? `iOS ${device.ios_version ?? device.android_version}`
          : `Android ${device.android_version}`
      }`
    : "Tatana activo · esperando dispositivo";

  return (
    <div
      className="hidden sm:flex items-center gap-1.5 min-w-0 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
      role="status"
      aria-live="polite"
    >
      {online ? (
        <span className={cn(PILL, "min-w-0 bg-fx-success-soft text-fx-success")}>
          <Wifi className="h-3 w-3 shrink-0" aria-hidden="true" />
          <span className="truncate max-w-[160px] lg:max-w-[280px]">{deviceLabel}</span>
        </span>
      ) : (
        <span className={cn(PILL, "bg-fx-warning-soft text-fx-warning")}>
          <WifiOff className="h-3 w-3 shrink-0" aria-hidden="true" />
          Tatana no disponible
        </span>
      )}
      {recording && (
        <span className={cn(PILL, "bg-fx-danger-soft text-fx-danger font-bold")}>
          <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-fx-danger motion-safe:animate-pulse" />
          REC
        </span>
      )}
    </div>
  );
}
