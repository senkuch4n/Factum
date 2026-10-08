"use client";

import { ArrowRight, Smartphone, Plus, Usb } from "lucide-react";
import { Button } from "primereact/button";
import { cn } from "@/lib/utils";
import type { Device } from "@/types";

interface Props {
  online: boolean;
  device: Device | null;
  recording: boolean;
  /** Arranca el wizard (paso 1: dispositivo). */
  onStart: () => void;
  /** Abre la guía de uso (cómo conectar el dispositivo). */
  onOpenGuide: () => void;
  guideOpen?: boolean;
}

const ICON_BUTTON =
  "inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md border border-fx-border text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text transition-colors duration-fx-fast ease-fx fx-focus-ring";

function deviceLabel(device: Device): string {
  const os = device.platform === "ios"
    ? `iOS ${device.ios_version ?? device.android_version}`
    : `Android ${device.android_version}`;
  return `${device.manufacturer} ${device.model} · ${os}`;
}

/**
 * Tarjeta "Sesión actual": estado de Tatana y del dispositivo conectado, con
 * el mismo criterio de tonos que `AgentChip` (éxito / advertencia / peligro).
 */
export function CurrentSessionCard({ online, device, recording, onStart, onOpenGuide, guideOpen }: Props) {
  const state = recording ? "recording" : online ? "online" : "offline";

  const status = {
    recording: { label: "Grabando",             dot: "bg-fx-danger motion-safe:animate-pulse", badge: "bg-fx-danger-soft text-fx-danger" },
    online:    { label: "Tatana listo",         dot: "bg-fx-success",                         badge: device ? "bg-fx-success-soft text-fx-success" : "bg-fx-surface-3 text-fx-text-2" },
    offline:   { label: "Tatana no disponible", dot: "bg-fx-warning",                         badge: "bg-fx-warning-soft text-fx-warning" },
  }[state];

  const subtitle = device
    ? deviceLabel(device)
    : online ? "Esperando dispositivo" : "Abrí Tatana en esta PC";

  const helper = device
    ? "El dispositivo está listo para una nueva inspección."
    : online
      ? "Conectá un dispositivo para comenzar una nueva inspección."
      : "Iniciá Tatana y conectá un dispositivo para comenzar una nueva inspección.";

  return (
    <section aria-labelledby="current-session-title" className="fx-card rounded-fx-xl">
      <div className="flex items-center justify-between gap-3 border-b border-fx-border px-5 py-4">
        <h2 id="current-session-title" className="text-lg font-semibold leading-tight text-fx-text">
          Sesión actual
        </h2>
        <button
          type="button"
          onClick={onOpenGuide}
          aria-label="Cómo conectar un dispositivo (guía de uso)"
          aria-haspopup="dialog"
          aria-expanded={guideOpen}
          className={ICON_BUTTON}
        >
          <ArrowRight className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>

      <div className="space-y-4 px-5 py-5">
        <div className="flex items-center gap-4">
          <span className={cn("flex h-14 w-14 shrink-0 items-center justify-center rounded-full", status.badge)} aria-hidden="true">
            <Smartphone className="h-6 w-6" />
          </span>
          {/* Texto del estado + dispositivo: se anuncia cuando cambia. */}
          <div role="status" aria-live="polite" className="min-w-0">
            <p className="m-0 flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">
              <span className={cn("h-2 w-2 shrink-0 rounded-full", status.dot)} aria-hidden="true" />
              {status.label}
            </p>
            <p className="m-0 mt-0.5 truncate text-xs text-fx-text-3" title={subtitle}>{subtitle}</p>
          </div>
        </div>

        <div className="border-t border-fx-border pt-4">
          <p className="m-0 text-fx-body-sm text-fx-text-2">{helper}</p>
        </div>

        <Button
          type="button"
          outlined
          severity="secondary"
          icon={device
            ? <Plus className="h-4 w-4" aria-hidden="true" />
            : <Usb className="h-4 w-4" aria-hidden="true" />}
          label={device ? "Nueva inspección con este dispositivo" : "Conectar dispositivo"}
          onClick={onStart}
          disabled={recording}
          className="w-full min-h-11"
        />
      </div>
    </section>
  );
}
