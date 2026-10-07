"use client";

import { FlaskConical } from "lucide-react";
import { useAuthMode } from "@/hooks/useAuthMode";
import { APP_VERSION } from "@/lib/version";

/**
 * Línea de estado global: versión + badge "Dev" cuando el backend corre en
 * modo desarrollo. Píldora opaca fija abajo
 * a la izquierda (el fondo propio garantiza contraste sobre cualquier
 * contenido); `pointer-events-none` para no tapar el dock, el wizard ni los
 * toasts. No es un landmark <footer>: ese queda para SiteFooter.
 */
export function SystemStatusLine() {
  const devMode = useAuthMode().mode === "dev";

  return (
    <div
      aria-label="Estado del sistema"
      className="pointer-events-none fixed bottom-2 left-3 z-fx-statusline flex select-none items-center gap-1.5 rounded-fx-pill border border-fx-border bg-fx-surface-1 px-2 py-0.5 text-[11px] leading-4 text-fx-text-3 shadow-fx-1"
    >
      <span className="tabular-nums">
        <span className="hidden sm:inline">Factum </span>v{APP_VERSION}
      </span>
      {devMode && (
        <span className="inline-flex items-center gap-1 rounded-fx-pill border border-fx-warning bg-fx-warning-soft px-1.5 font-semibold uppercase tracking-wide text-fx-warning">
          <FlaskConical className="h-2.5 w-2.5" aria-hidden="true" /> Dev
        </span>
      )}
    </div>
  );
}
