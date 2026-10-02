"use client";

import { useEffect, useState } from "react";
import { Loader2, Wifi, WifiOff } from "lucide-react";
import { agent } from "@/lib/agent";
import { cn } from "@/lib/utils";

const POLL_MS = 5000;

/**
 * Estado del agente local Tatana en el login: "Verificando agente…",
 * "Tatana conectado" (tokens de éxito) o "Tatana sin conexión" (advertencia).
 * Siempre ícono + texto. Sondea `agent.isOnline()` al montar y cada 5 s
 * (timeout de 2 s en agent.ts). Como el estado es un primitivo, React no
 * re-renderiza si no cambió y el lector de pantalla no re-anuncia.
 */
export function AgentStatusChip({ className }: { className?: string }) {
  const [online, setOnline] = useState<boolean | null>(null);

  useEffect(() => {
    let active = true;
    const check = () => {
      agent.isOnline().then((value) => {
        if (active) setOnline(value);
      });
    };
    check();
    const id = setInterval(check, POLL_MS);
    return () => {
      active = false;
      clearInterval(id);
    };
  }, []);

  const tone =
    online === null
      ? "bg-fx-surface-2 border-fx-border text-fx-text-2"
      : online
        ? "bg-fx-success-soft border-fx-success text-fx-success"
        : "bg-fx-warning-soft border-fx-warning text-fx-warning";

  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        "inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-fx-body-sm font-medium",
        "transition-colors duration-fx-base ease-fx",
        tone,
        className,
      )}
    >
      {online === null ? (
        <Loader2 className="h-3.5 w-3.5 shrink-0 motion-safe:animate-spin" aria-hidden="true" />
      ) : online ? (
        <Wifi className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      ) : (
        <WifiOff className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      )}
      <span>{online === null ? "Verificando agente…" : online ? "Tatana conectado" : "Tatana sin conexión"}</span>
    </div>
  );
}
