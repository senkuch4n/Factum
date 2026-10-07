"use client";

import { Button } from "primereact/button";
import { Tag } from "primereact/tag";
import { Smartphone, RefreshCw, Wifi, ChevronRight, Cpu, Fingerprint, Signal, AlertTriangle, HelpCircle, Loader2 } from "lucide-react";
import type { Device } from "@/lib/agent";
import { FxBanner } from "@/components/feedback/FxBanner";
import { StepHeader } from "@/components/wizard/StepHeader";
import { IOS_APPLE_SERVICE_MISSING_MESSAGE, IOS_APPLE_SERVICE_MISSING_TITLE } from "@/lib/agent-messages";

interface Props {
  devices: Device[];
  agentOnline: boolean;
  loading: boolean;
  onSelect: (d: Device) => void;
  onRefresh: () => void;
  onOpenGuide?: () => void;
  /** Tatana informa que falta el servicio de dispositivos de Apple (`/health.ios.apple_service = "missing"`). */
  appleServiceMissing?: boolean;
}

const FADE_IN = "motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]";

/** Paso 1: elegir el dispositivo conectado por USB. */
export function DeviceConnect({ devices, agentOnline, loading, onSelect, onRefresh, onOpenGuide, appleServiceMissing = false }: Props) {
  return (
    <div className="space-y-6">
      <div className="pt-2 pb-2 text-center">
        <div className="mx-auto mb-5 flex h-16 w-16 items-center justify-center rounded-fx-xl border border-fx-border bg-fx-surface-2">
          <Smartphone className="h-8 w-8 text-fx-text-2" strokeWidth={1.5} aria-hidden="true" />
        </div>
        <StepHeader
          align="center"
          title="Conectá el celular"
          description="Enchufá el cable USB. El sistema detecta el dispositivo automáticamente."
        />
        {onOpenGuide && (
          <Button
            type="button"
            link
            size="small"
            icon={<HelpCircle className="h-4 w-4" aria-hidden="true" />}
            label="¿No sabés cómo preparar el celular?"
            onClick={onOpenGuide}
            className="mt-3"
          />
        )}
      </div>

      {!agentOnline && (
        <FxBanner tone="warn" role="status" icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
          <p className="m-0 font-semibold">Tatana no está activo en esta PC</p>
          <p className="m-0 mt-0.5 font-normal text-fx-text">
            Abrí la aplicación Tatana y esperá unos segundos; el estado se actualiza solo.
          </p>
          {onOpenGuide && (
            <Button
              type="button"
              link
              size="small"
              label="Ver la guía de uso"
              onClick={onOpenGuide}
              className="mt-1"
              pt={{ root: { className: "px-0" } }}
            />
          )}
        </FxBanner>
      )}

      {/* Sin el servicio de Apple el iPhone no aparece: se avisa aunque haya Androids en la lista. */}
      {agentOnline && appleServiceMissing && (
        <FxBanner tone="warn" role="status" icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
          <p className="m-0 font-semibold">{IOS_APPLE_SERVICE_MISSING_TITLE}</p>
          <p className="m-0 mt-0.5 font-normal text-fx-text">{IOS_APPLE_SERVICE_MISSING_MESSAGE}</p>
          {onOpenGuide && (
            <Button
              type="button"
              link
              size="small"
              label="Ver la guía"
              onClick={onOpenGuide}
              className="mt-1"
              pt={{ root: { className: "px-0" } }}
            />
          )}
        </FxBanner>
      )}

      {loading ? (
        <div role="status" className="flex flex-col items-center gap-3 py-10">
          <Loader2 className="h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
          <p className="m-0 text-fx-body-sm text-fx-text-2">Buscando dispositivos…</p>
        </div>
      ) : devices.length === 0 ? (
        <div className={`space-y-3 py-10 text-center ${FADE_IN}`}>
          <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-fx-lg border border-fx-border bg-fx-surface-2">
            <Smartphone className="h-7 w-7 text-fx-text-3" strokeWidth={1.5} aria-hidden="true" />
          </div>
          <p className="m-0 text-fx-body-sm text-fx-text-2">No se encontró ningún dispositivo</p>
          <Button
            type="button"
            severity="secondary"
            size="small"
            icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
            label="Buscar de nuevo"
            onClick={onRefresh}
          />
        </div>
      ) : (
        <div className={FADE_IN}>
          <h3 aria-live="polite" className="m-0 mb-3 text-fx-label uppercase text-fx-text-2">
            {devices.length === 1 ? "1 dispositivo encontrado" : `${devices.length} dispositivos`}
          </h3>
          <ul className="m-0 list-none space-y-2.5 p-0">
            {devices.map(device => {
              const isIOS = device.platform === "ios";
              return (
                <li key={device.serial}>
                  <button
                    type="button"
                    onClick={() => onSelect(device)}
                    className="fx-card fx-card-interactive group flex w-full min-h-11 items-center gap-4 p-4 text-left"
                  >
                    <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-fx-md border border-fx-border bg-fx-surface-2">
                      <Smartphone className="h-5 w-5 text-fx-text-2" strokeWidth={1.5} aria-hidden="true" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex flex-wrap items-center gap-2">
                        <span className="text-fx-body-sm font-semibold text-fx-text">
                          {device.manufacturer} {device.model}
                        </span>
                        {isIOS && <Tag severity="info" value="iOS" />}
                      </span>
                      <span className="mt-1 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-fx-text-3">
                        <span className="flex items-center gap-1">
                          <Signal className="h-3 w-3" aria-hidden="true" />
                          {isIOS ? `iOS ${device.ios_version ?? device.android_version}` : `Android ${device.android_version}`}
                        </span>
                        {device.imei && device.imei !== "INGRESAR_MANUALMENTE" && (
                          <span className="flex items-center gap-1 font-mono" translate="no">
                            <Fingerprint className="h-3 w-3" aria-hidden="true" />
                            <span className="sr-only">IMEI </span>
                            {device.imei}
                          </span>
                        )}
                        <span className="flex items-center gap-1 font-mono" translate="no">
                          <Cpu className="h-3 w-3" aria-hidden="true" />
                          <span className="sr-only">Serie </span>
                          {device.serial.slice(0, 20)}{device.serial.length > 20 ? "…" : ""}
                        </span>
                      </span>
                    </span>
                    <ChevronRight
                      className="h-4 w-4 shrink-0 text-fx-text-3 transition-transform duration-fx-fast motion-safe:group-hover:translate-x-0.5"
                      aria-hidden="true"
                    />
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}

      {agentOnline && devices.length === 0 && !loading && (
        <ul className={`m-0 list-none space-y-2 p-0 ${FADE_IN}`}>
          <li className="flex gap-3 rounded-fx-lg border border-fx-border bg-fx-surface-2 px-4 py-3">
            <Wifi className="mt-0.5 h-4 w-4 shrink-0 text-fx-text-2" aria-hidden="true" />
            <div>
              <p className="m-0 text-xs font-semibold text-fx-text">Android — ¿no aparece?</p>
              <p className="m-0 mt-0.5 text-xs text-fx-text-2">
                Activá <strong className="font-semibold text-fx-text">Depuración USB</strong> en Ajustes → Opciones de desarrollo. Si pide autorizar, tocá Permitir.
              </p>
            </div>
          </li>
          <li className="flex gap-3 rounded-fx-lg border border-fx-border bg-fx-surface-2 px-4 py-3">
            {/* contenido de imagen */}
            <img src="/apple.svg" alt="" width={16} height={16} className="mt-0.5 h-4 w-4 shrink-0 dark:invert" />
            <div>
              <p className="m-0 text-xs font-semibold text-fx-text">iPhone — ¿no aparece?</p>
              <p className="m-0 mt-0.5 text-xs text-fx-text-2">
                Conectá el cable y tocá <strong className="font-semibold text-fx-text">Confiar</strong> en la pantalla del iPhone. En iOS 16+ activá también <strong className="font-semibold text-fx-text">Modo Desarrollador</strong> en Ajustes → Privacidad.
              </p>
            </div>
          </li>
        </ul>
      )}
    </div>
  );
}
