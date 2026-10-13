"use client";

import { Usb, Unplug, ServerOff } from "lucide-react";
import { cn } from "@/lib/utils";
import { FxBanner } from "@/components/feedback/FxBanner";
import type { DeviceConnection } from "@/hooks/useDeviceConnection";

/* ── Estado de conexión del equipo (indicador + aviso), extraído de CaseFormStep (#52 D2/D3) ── */
export const CONNECTION_UI: Record<DeviceConnection, {
  icon: React.ElementType<{ className?: string }>;
  label: string;
  tone: string;
  srExtra?: string;
  bannerTitle?: string;
  bannerBody?: string;
  disabledReason: string | null;
}> = {
  connected: {
    icon: Usb, label: "USB conectado", tone: "text-fx-success", disabledReason: null,
  },
  device_offline: {
    icon: Unplug, label: "Desconectado", tone: "text-fx-danger",
    srExtra: ". Reconectá el cable USB; podés seguir completando el formulario.",
    bannerTitle: "Equipo desconectado",
    bannerBody: "Reconectá el cable USB. La captura se actualiza sola al reconectar. Podés seguir completando y guardar el caso.",
    disabledReason: "Conectá el equipo para capturar.",
  },
  agent_offline: {
    icon: ServerOff, label: "Agente sin conexión", tone: "text-fx-warning",
    srExtra: ". Abrí Tatana en esta PC; podés seguir completando el formulario.",
    bannerTitle: "Tatana no responde",
    bannerBody: "Abrí Tatana en esta PC. Podés seguir completando y guardar el caso.",
    disabledReason: "Abrí Tatana para capturar.",
  },
};

/**
 * Única región que anuncia el cambio de conexión (siempre montada). En el
 * paso 2 hay una sola instancia: en la ficha (lg+) o en la franja superior (< lg).
 */
export function ConnectionIndicator({ connection, className }: { connection: DeviceConnection; className?: string }) {
  const ui = CONNECTION_UI[connection];
  const Icon = ui.icon;
  return (
    <span
      role="status"
      aria-atomic="true"
      className={cn("inline-flex shrink-0 items-center gap-1.5 text-xs font-medium", ui.tone, className)}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden="true" /> {ui.label}
      {ui.srExtra && <span className="sr-only">{ui.srExtra}</span>}
    </span>
  );
}

/** Aviso de desconexión (`role="note"`: el anuncio lo hace el indicador). Nada si está conectado. */
export function DisconnectedBanner({ connection, className }: { connection: DeviceConnection; className?: string }) {
  const ui = CONNECTION_UI[connection];
  if (!ui.bannerTitle) return null;
  const Icon = ui.icon;
  return (
    <FxBanner tone="warn" role="note" icon={<Icon className="h-4 w-4" aria-hidden="true" />} className={className}>
      <p className="m-0 font-semibold">{ui.bannerTitle}</p>
      <p className="m-0 mt-0.5 text-xs font-normal text-fx-text-2">{ui.bannerBody}</p>
    </FxBanner>
  );
}
