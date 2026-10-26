"use client";

import { Button } from "primereact/button";
import { AlertTriangle, BatteryCharging, BatteryLow, Loader2, Lock, RefreshCw, Smartphone } from "lucide-react";
import type { Device } from "@/lib/agent";
import type { AgentExtractionPreflight } from "@/lib/agent";
import type { DeviceConnection } from "@/hooks/useDeviceConnection";
import { DeviceConnect } from "@/components/DeviceConnect";
import { StepHeader } from "@/components/wizard/StepHeader";
import { FxBanner } from "@/components/feedback/FxBanner";

interface Props {
  devices: Device[];
  agentOnline: boolean;
  loadingDev: boolean;
  onRefresh: () => void;
  device: Device | null;
  onSelect: (d: Device) => void;
  onClear: () => void;
  connection: DeviceConnection;
  preflight: AgentExtractionPreflight | null;
  preflightLoading: boolean;
  preflightError: string | null;
  onRetryPreflight: () => void;
}

const IOS_HINT = "La extracción de iPhone todavía no está disponible";

/** Paso 1 (§10.4.1): elegir el Android conectado y leer su preflight. */
export function DeviceStep({
  devices, agentOnline, loadingDev, onRefresh, device, onSelect, onClear, connection,
  preflight, preflightLoading, preflightError, onRetryPreflight,
}: Props) {
  if (!device) {
    return (
      <DeviceConnect
        devices={devices}
        agentOnline={agentOnline}
        loading={loadingDev}
        onSelect={onSelect}
        onRefresh={onRefresh}
        disabledPlatform="ios"
        disabledHint={IOS_HINT}
      />
    );
  }

  const battery = preflight?.battery_level;
  const lowBattery = typeof battery === "number" && battery < 20 && !preflight?.battery_charging;

  return (
    <div className="space-y-6">
      <StepHeader title="Dispositivo elegido" description="Revisá el estado antes de seguir." />

      <div className="fx-card space-y-4 p-4 sm:p-5">
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 items-center gap-3">
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-fx-md border border-fx-border bg-fx-surface-2">
              <Smartphone className="h-5 w-5 text-fx-text-2" strokeWidth={1.5} aria-hidden="true" />
            </span>
            <div className="min-w-0">
              <p className="m-0 text-fx-body-sm font-semibold text-fx-text">{device.manufacturer} {device.model}</p>
              <p className="m-0 mt-0.5 text-xs text-fx-text-3">
                {preflight ? `Android ${preflight.android_version}` : `Android ${device.android_version}`}
                <span className="mx-1.5" aria-hidden="true">·</span>
                <span className="font-mono" translate="no">{device.serial.slice(0, 16)}{device.serial.length > 16 ? "…" : ""}</span>
              </p>
            </div>
          </div>
          <Button
            text
            size="small"
            icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
            label="Cambiar"
            onClick={onClear}
            className="shrink-0"
          />
        </div>

        {connection !== "connected" && (
          <FxBanner tone="warn" role="status">
            {connection === "agent_offline"
              ? "Tatana dejó de responder. Abrí la aplicación y esperá unos segundos."
              : "El celular se desconectó. Reconectalo por USB."}
          </FxBanner>
        )}

        {preflightLoading && (
          <p className="m-0 flex items-center gap-2 text-fx-body-sm text-fx-text-2">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            Leyendo el estado del celular…
          </p>
        )}

        {preflightError && (
          <FxBanner tone="error">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span>{preflightError}</span>
              <Button text size="small" label="Reintentar" onClick={onRetryPreflight} className="min-h-11" />
            </div>
          </FxBanner>
        )}

        {preflight && (
          <ul className="m-0 grid list-none grid-cols-1 gap-2 p-0 sm:grid-cols-2">
            <StatusRow
              icon={lowBattery ? <BatteryLow className="h-4 w-4 text-fx-warning" aria-hidden="true" />
                : <BatteryCharging className="h-4 w-4 text-fx-text-2" aria-hidden="true" />}
              label="Batería"
              value={typeof battery === "number"
                ? `${battery}%${preflight.battery_charging ? " · cargando" : ""}`
                : "No disponible"}
              tone={lowBattery ? "warn" : "normal"}
            />
            <StatusRow
              icon={<Lock className="h-4 w-4 text-fx-text-2" aria-hidden="true" />}
              label="Pantalla"
              value={preflight.screen_state === "on" ? "Desbloqueada"
                : preflight.screen_state === "locked" ? "Bloqueada"
                : preflight.screen_state === "off" ? "Apagada" : "Desconocida"}
            />
          </ul>
        )}

        {lowBattery && (
          <FxBanner tone="warn" role="status" icon={<BatteryLow className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
            La batería está baja. Conectá el cargador: la extracción puede durar horas.
          </FxBanner>
        )}

        <FxBanner tone="info" role="note" icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
          Mantené la pantalla desbloqueada y no desconectes el celular durante la extracción.
        </FxBanner>
      </div>
    </div>
  );
}

function StatusRow({
  icon, label, value, tone = "normal",
}: { icon: React.ReactNode; label: string; value: string; tone?: "normal" | "warn" }) {
  return (
    <li className="flex items-center gap-2 rounded-fx-md border border-fx-border bg-fx-surface-2 px-3 py-2">
      {icon}
      <span className="text-xs text-fx-text-3">{label}:</span>
      <span className={`text-xs font-semibold ${tone === "warn" ? "text-fx-warning" : "text-fx-text"}`}>{value}</span>
    </li>
  );
}
