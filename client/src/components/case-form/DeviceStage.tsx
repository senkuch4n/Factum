"use client";

import { memo, useCallback, useEffect, useState } from "react";
import dynamic from "next/dynamic";
import { Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { useMediaQuery } from "@/hooks/useMediaQuery";
import { PhoneScreen } from "@/components/PhoneFrame";
import { Phone3DBoundary } from "./Phone3DBoundary";
import { canUsePhone3D, disablePhone3D } from "./phone3d-support";

/**
 * Único import del modelo 3D (chunk aparte con three + fiber). `next/dynamic`
 * solo pide el chunk cuando se renderiza: en `< lg`, con movimiento reducido o
 * sin WebGL acelerado nunca se descarga (§6.8 D4).
 */
const PhoneModel3D = dynamic(() => import("./PhoneModel3D"), { ssr: false, loading: () => null });

interface Props {
  src: string | null;
  platform: "android" | "ios";
  loading: boolean;
  error: string | null;
  stale: boolean;
  /** "aside" (ficha en lg+) admite 3D; "drawer" (< lg) siempre 2D. */
  variant: "aside" | "drawer";
  /**
   * Tamaño del escenario (la proporción 236/470 la pone el componente). Default
   * `w-full`; la ficha lo fija por alto de viewport para que el teléfono sea
   * protagonista sin hacerla scrollear.
   */
  sizeClassName?: string;
  /** Clases del contenedor (en la ficha: columna flexible que llena el alto disponible). */
  className?: string;
}

type SaveDataNavigator = Navigator & { connection?: { saveData?: boolean } };

/** Espera a que el navegador esté libre: el formulario y la ficha se pintan sin esperar al 3D. */
function useIdle(): boolean {
  const [idle, setIdle] = useState(false);
  useEffect(() => {
    if (typeof window.requestIdleCallback === "function") {
      const id = window.requestIdleCallback(() => setIdle(true), { timeout: 1500 });
      return () => window.cancelIdleCallback(id);
    }
    const t = window.setTimeout(() => setIdle(true), 300);
    return () => window.clearTimeout(t);
  }, []);
  return idle;
}

function usePhone3DEligible(variant: Props["variant"]): boolean {
  const reducedMotion = useMediaQuery("(prefers-reduced-motion: reduce)");
  const idle = useIdle();
  if (variant !== "aside" || reducedMotion || !idle) return false;
  // Interruptor de despliegue (se inlinea en build): NEXT_PUBLIC_DEVICE_3D=off.
  if (process.env.NEXT_PUBLIC_DEVICE_3D === "off") return false;
  if ((navigator as SaveDataNavigator).connection?.saveData) return false;
  return canUsePhone3D();
}

/**
 * Escenario del equipo en la ficha: marco 2D siempre montado (sin salto de
 * layout) y, si se puede, el teléfono 3D encima, que funde al estar listo.
 * `memo` + props primitivas: tipear en el formulario no re-renderiza la escena.
 */
export const DeviceStage = memo(function DeviceStage({ src, platform, loading, error, stale, variant, sizeClassName, className }: Props) {
  const eligible = usePhone3DEligible(variant);
  const [failed, setFailed] = useState(false);
  const [ready, setReady] = useState(false);
  const show3D = eligible && !failed;
  const visible3D = show3D && ready;

  const fail = useCallback(() => {
    disablePhone3D();
    setFailed(true);
    setReady(false);
  }, []);
  const onReady = useCallback(() => setReady(true), []);

  // Si el 3D deja de ser elegible (p. ej. se activó "reducir movimiento"), vuelve el 2D.
  if (!show3D && ready) setReady(false);

  const chip = loading ? "Capturando…" : stale ? "Captura anterior" : !src ? (error ? "No se pudo capturar" : "Sin captura") : null;

  return (
    <div className={className}>
      <div className={cn("relative", sizeClassName ?? "w-full")} style={{ aspectRatio: "236 / 470" }}>
        {/* Capa 1: 2D (fallback y placeholder). Oculta (también para el lector) cuando el 3D está listo. */}
        <div className={cn("absolute inset-0", visible3D && "invisible")}>
          <PhoneScreen src={src} platform={platform} loading={loading} error={error} stale={stale} className="max-w-none" />
        </div>

        {/* Capa 2: 3D (decorativo; se puede girar arrastrando, ±25°). */}
        {show3D && (
          <div
            className={cn(
              "absolute inset-0 opacity-0 transition-opacity duration-fx-base ease-fx motion-reduce:transition-none",
              visible3D && "opacity-100",
            )}
          >
            <Phone3DBoundary onError={fail}>
              <PhoneModel3D src={src} dim={stale} platform={platform} onReady={onReady} onError={fail} />
            </Phone3DBoundary>
          </div>
        )}

        {/* Capa 3: estado de la captura sobre el 3D (mismos textos que el 2D). El motivo va en CaptureControls. */}
        {visible3D && chip && (
          <span
            aria-hidden="true"
            className="pointer-events-none absolute bottom-3 left-1/2 z-10 inline-flex -translate-x-1/2 items-center gap-1 whitespace-nowrap rounded-fx-sm bg-fx-surface-1 px-2 py-0.5 text-xs font-medium text-fx-text shadow-fx-1"
          >
            {loading && <Loader2 className="h-3 w-3 motion-safe:animate-spin" aria-hidden="true" />}
            {chip}
          </span>
        )}
      </div>

      {/* Pista del gesto: lugar reservado en la ficha (sin salto de layout), visible solo con el 3D. */}
      {variant === "aside" && (
        <p
          aria-hidden="true"
          className={cn(
            "m-0 mt-1.5 shrink-0 text-center text-xs leading-tight text-fx-text-3 transition-opacity duration-fx-base ease-fx motion-reduce:transition-none",
            visible3D ? "opacity-100" : "invisible opacity-0",
          )}
        >
          Arrastrá para girar
        </p>
      )}
    </div>
  );
});
