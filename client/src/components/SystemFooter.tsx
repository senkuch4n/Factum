"use client";

import { useEffect, useState } from "react";
import { FlaskConical } from "lucide-react";
import { api } from "@/lib/api";
import { APP_VERSION } from "@/lib/version";

/**
 * Línea de estado del sistema — versión + atribución institucional, y un badge
 * de advertencia cuando el backend corre en modo desarrollo. Fija en la esquina
 * inferior izquierda, `pointer-events-none` para no estorbar el flujo.
 */
export function SystemFooter() {
  const [devMode, setDevMode] = useState(false);

  useEffect(() => {
    api.getMode().then((m) => setDevMode(m === "dev")).catch(() => {});
  }, []);

  return (
    <div
      className="pointer-events-none fixed bottom-2 left-3 z-30 flex select-none items-center gap-2 text-[10px]"
      style={{ color: "var(--text-muted)" }}
    >
      <span>Factum v{APP_VERSION} · MPF Salta – GIF</span>
      {devMode && (
        <span
          className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 font-bold uppercase tracking-wide"
          style={{
            background: "rgba(245,158,11,0.14)",
            border: "1px solid rgba(245,158,11,0.32)",
            color: "#d97706",
          }}
        >
          <FlaskConical className="h-2.5 w-2.5" aria-hidden="true" /> Dev
        </span>
      )}
    </div>
  );
}
