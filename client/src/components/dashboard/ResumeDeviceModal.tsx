"use client";

import { useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { Smartphone, CheckCircle2, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Case } from "@/types";

interface Props {
  /** Caso a retomar; `null` = cerrado (así el Dialog puede animar la salida). */
  cas: Case | null;
  deviceConnected: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Reconexión al retomar un borrador: espera el equipo original. "Continuar
 * inspección" está siempre visible y se habilita al detectar el equipo (la
 * detección la maneja `dashboard/page.tsx`).
 */
export function ResumeDeviceModal({ cas, deviceConnected, onConfirm, onCancel }: Props) {
  // Último caso no nulo: se sigue renderizando durante la animación de salida.
  // (Ajuste de estado durante el render, patrón recomendado por React.)
  const [shown, setShown] = useState<Case | null>(cas);
  if (cas && cas !== shown) setShown(cas);
  const c = cas ?? shown;

  const isIOS = c?.device.platform === "ios";
  const serial = c?.device.serial ?? "";
  const serialShort = serial.length > 10 ? serial.slice(0, 10) + "…" : serial;

  return (
    <Dialog
      visible={cas !== null}
      onHide={onCancel}
      dismissableMask={false}
      draggable={false}
      resizable={false}
      // Ancho por pt: se fusiona después del pt global y gana el merge.
      pt={{ root: { className: "w-[min(26rem,100%)]" } }}
      header={
        <span className="flex items-center gap-3">
          <span
            className={cn(
              "inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md",
              deviceConnected ? "bg-fx-success-soft text-fx-success" : "bg-fx-warning-soft text-fx-warning",
            )}
          >
            {deviceConnected
              ? <CheckCircle2 className="h-5 w-5" aria-hidden="true" />
              : <Smartphone className="h-4 w-4" aria-hidden="true" />}
          </span>
          {deviceConnected ? "Dispositivo detectado" : "Conectá el dispositivo"}
        </span>
      }
      footer={
        <>
          <Button severity="secondary" label="Cancelar" onClick={onCancel} />
          <Button
            icon={<CheckCircle2 className="h-4 w-4" aria-hidden="true" />}
            label="Continuar inspección"
            onClick={onConfirm}
            disabled={!deviceConnected}
          />
        </>
      }
    >
      {c && (
        <div className="space-y-3">
          <p role="status" aria-live="polite" className="m-0 text-fx-body-sm text-fx-text-2">
            {deviceConnected ? "Listo para retomar la inspección" : "Este caso requiere el dispositivo original"}
          </p>

          <div
            className={cn(
              "rounded-fx-lg border p-3.5 flex items-center gap-3 transition-colors duration-fx-base ease-fx",
              deviceConnected ? "bg-fx-success-soft border-fx-success" : "bg-fx-surface-1 border-fx-border",
            )}
          >
            <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-fx-md border border-fx-border bg-fx-surface-2">
              {/* contenido de imagen: SVG monocromo, se invierte en oscuro */}
              <img
                src={isIOS ? "/apple.svg" : "/android.svg"}
                alt={isIOS ? "iOS" : "Android"}
                width={20}
                height={20}
                className="h-5 w-5 opacity-70 dark:invert dark:opacity-65"
              />
            </span>
            <div className="min-w-0 flex-1">
              <p className="m-0 truncate text-fx-body-sm font-bold text-fx-text">
                {c.device.manufacturer} {c.device.model}
              </p>
              <p translate="no" className="m-0 mt-0.5 font-mono text-xs text-fx-text-3">
                {isIOS ? "UDID" : "Serial"}: {serialShort}
              </p>
              <p className="m-0 mt-0.5 text-xs text-fx-text-3">
                Expte: <span className="font-semibold text-fx-text-2">{c.nro_referencia}</span>
              </p>
            </div>
            {deviceConnected
              ? <CheckCircle2 className="h-5 w-5 shrink-0 text-fx-success" aria-hidden="true" />
              : <Loader2 className="h-5 w-5 shrink-0 text-fx-warning motion-safe:animate-spin" aria-hidden="true" />}
          </div>

          {deviceConnected ? (
            <p className="m-0 flex items-start gap-2 text-xs font-medium text-fx-success">
              <CheckCircle2 className="h-3.5 w-3.5 shrink-0 mt-px" aria-hidden="true" />
              Dispositivo conectado y reconocido — podés continuar la inspección
            </p>
          ) : (
            <div className="space-y-2.5">
              <p className="m-0 text-xs text-fx-text-2">Esperando conexión USB — se detectará automáticamente</p>
              <ol className="m-0 grid grid-cols-1 gap-2 p-0 sm:grid-cols-2">
                {[
                  isIOS
                    ? "Conectá el iPhone con el cable Lightning o USB-C"
                    : "Conectá el dispositivo con el cable USB",
                  isIOS
                    ? 'Desbloqueá y tocá "Confiar en este equipo"'
                    : "Desbloqueá el dispositivo si está en pantalla de bloqueo",
                ].map((tip, i) => (
                  <li key={i} className="flex items-start gap-2">
                    <span
                      className="inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-fx-warning-soft text-xs font-bold text-fx-warning"
                      aria-hidden="true"
                    >
                      {i + 1}
                    </span>
                    <span className="text-xs leading-snug text-fx-text-2">{tip}</span>
                  </li>
                ))}
              </ol>
            </div>
          )}
        </div>
      )}
    </Dialog>
  );
}
