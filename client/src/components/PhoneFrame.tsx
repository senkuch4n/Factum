"use client";

import { Button } from "primereact/button";
import { RefreshCw, Loader2, Smartphone } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * Marco neutro de teléfono (tokens). Lo usa PhoneFrame (paso 2) y, desde la
 * parte 3, el escenario de CaptureStep. La proporción 236/470 es la del marco
 * de CaptureStep, para que lo adopte sin saltos de layout.
 */
export function PhoneShell({ platform = "android", className, children }: {
  platform?: "android" | "ios";
  className?: string;
  children: React.ReactNode;
}) {
  const isIOS = platform === "ios";
  return (
    <div className={cn("relative mx-auto w-full", className)} style={{ aspectRatio: "236 / 470" }}>
      {/* Botones laterales decorativos (mismas posiciones que el marco de CaptureStep). */}
      <span aria-hidden="true" className="absolute -left-[3px] top-[16%] h-[6%] w-[3px] rounded-fx-sm bg-fx-border-strong" />
      <span aria-hidden="true" className="absolute -left-[3px] top-[26%] h-[11%] w-[3px] rounded-fx-sm bg-fx-border-strong" />
      <span aria-hidden="true" className="absolute -right-[3px] top-[22%] h-[15%] w-[3px] rounded-fx-sm bg-fx-border-strong" />

      {/* Radios del chasis (hardware, no UI): fuera de la escala --fx-radius-* a propósito (D1). */}
      <div className="h-full w-full rounded-[2.4rem] border border-fx-border-strong bg-fx-surface-3 p-[8px] shadow-fx-2">
        <div className="relative h-full w-full overflow-hidden rounded-[2rem] bg-fx-bg">
          {isIOS ? (
            <span
              aria-hidden="true"
              className="absolute left-1/2 top-2 z-10 h-[14px] w-[60px] -translate-x-1/2 rounded-full bg-fx-surface-3"
            />
          ) : (
            <span
              aria-hidden="true"
              className="absolute left-1/2 top-2.5 z-10 h-2 w-2 -translate-x-1/2 rounded-full bg-fx-surface-3"
            />
          )}
          {children}
        </div>
      </div>
    </div>
  );
}

/** Miniatura de la pantalla del equipo en el paso "Causa" (misma firma de siempre). */
export function PhoneFrame({ src, platform, loading, onRefresh }: {
  src: string | null;
  platform: "android" | "ios";
  loading: boolean;
  onRefresh: () => void;
}) {
  return (
    <div className="flex flex-col items-center gap-3">
      <PhoneShell platform={platform} className="max-w-[160px]">
        {loading ? (
          <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-fx-surface-2">
            <Loader2 className="h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
            <span className="text-xs text-fx-text-3">Capturando…</span>
          </div>
        ) : src ? (
          <img
            src={src}
            alt="Pantalla del dispositivo"
            className="absolute inset-0 h-full w-full object-cover motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
          />
        ) : (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-fx-surface-2">
            <Smartphone className="h-7 w-7 text-fx-text-3" strokeWidth={1.5} aria-hidden="true" />
            <span className="text-xs text-fx-text-3">Sin captura</span>
          </div>
        )}
      </PhoneShell>
      <Button
        type="button"
        text
        severity="secondary"
        size="small"
        icon={<RefreshCw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />}
        label="Actualizar captura"
        onClick={onRefresh}
        disabled={loading}
      />
    </div>
  );
}
