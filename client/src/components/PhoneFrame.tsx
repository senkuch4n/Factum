"use client";

import { useId } from "react";
import { Button } from "primereact/button";
import { AlertTriangle, RefreshCw, Loader2, Smartphone } from "lucide-react";
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

/**
 * Miniatura de la pantalla del equipo en el paso "Causa". Las props nuevas son
 * opcionales (el showcase del design system la usa con la firma original):
 * - `error`: motivo del último intento fallido (se muestra debajo del marco).
 * - `stale`: la imagen mostrada ya no es actual → atenuada + "Captura anterior".
 * - `disabledReason`: por qué no se puede capturar ahora (deshabilita el botón).
 */
export function PhoneFrame({ src, platform, loading, onRefresh, error = null, stale = false, disabledReason = null }: {
  src: string | null;
  platform: "android" | "ios";
  loading: boolean;
  onRefresh: () => void;
  error?: string | null;
  stale?: boolean;
  disabledReason?: string | null;
}) {
  const reasonId = useId();
  return (
    <div className="flex flex-col items-center gap-3">
      <PhoneShell platform={platform} className="max-w-[160px]">
        {loading ? (
          <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-fx-surface-2">
            <Loader2 className="h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
            <span className="text-xs text-fx-text-3">Capturando…</span>
          </div>
        ) : src ? (
          <>
            <img
              src={src}
              alt={stale ? "Pantalla del dispositivo (captura anterior)" : "Pantalla del dispositivo"}
              className={cn(
                "absolute inset-0 h-full w-full object-cover motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]",
                "transition-opacity duration-fx-base ease-fx motion-reduce:transition-none",
                stale && "opacity-40",
              )}
            />
            {stale && (
              <span
                aria-hidden="true"
                className="absolute bottom-4 left-1/2 z-10 -translate-x-1/2 whitespace-nowrap rounded-fx-sm bg-fx-surface-1 px-2 py-0.5 text-xs font-medium text-fx-text shadow-fx-1"
              >
                Captura anterior
              </span>
            )}
          </>
        ) : error ? (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-fx-surface-2 px-3 text-center">
            <AlertTriangle className="h-7 w-7 text-fx-warning" strokeWidth={1.5} aria-hidden="true" />
            <span className="text-xs text-fx-text-2">No se pudo capturar</span>
          </div>
        ) : (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-fx-surface-2">
            <Smartphone className="h-7 w-7 text-fx-text-3" strokeWidth={1.5} aria-hidden="true" />
            <span className="text-xs text-fx-text-3">Sin captura</span>
          </div>
        )}
      </PhoneShell>

      {/* Siempre montado (sin display:none, para que el lector anuncie el motivo al aparecer); vacío, el margen negativo compensa el gap. */}
      <div role="status" className="flex w-full justify-center empty:-mb-3">
        {error && (
          <p className="m-0 flex max-w-[16rem] items-start justify-center gap-1.5 text-center text-xs text-fx-text-2">
            <AlertTriangle className="mt-px h-3.5 w-3.5 shrink-0 text-fx-warning" aria-hidden="true" />
            <span className="min-w-0 break-words">{error}</span>
          </p>
        )}
      </div>

      <Button
        type="button"
        text
        severity="secondary"
        size="small"
        icon={<RefreshCw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />}
        label={error ? "Reintentar" : "Actualizar captura"}
        onClick={onRefresh}
        disabled={loading || !!disabledReason}
        aria-describedby={disabledReason ? reasonId : undefined}
        className="[@media(pointer:coarse)]:min-h-11"
      />
      {disabledReason && (
        <p id={reasonId} className="m-0 -mt-2 text-center text-xs text-fx-text-2">{disabledReason}</p>
      )}
    </div>
  );
}
