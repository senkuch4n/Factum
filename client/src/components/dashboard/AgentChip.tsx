"use client";

import { Unplug, Wifi, WifiOff, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import { FxTip } from "@/components/overlay/FxTip";
import { deriveAgentChip, type AgentChipIcon, type AgentChipTone } from "@/lib/agent-chip";
import type { DeviceConnection } from "@/hooks/useDeviceConnection";
import type { Device } from "@/types";

interface Props {
  online: boolean;
  devices: Device[];
  loading: boolean;
  /** Equipo de la inspección (pasos 2 a 6); null en el dashboard y el paso 1. */
  selected: Device | null;
  connection: DeviceConnection;
  /** Grabación de esta pestaña o de cualquier pestaña en la PC. */
  recording: boolean;
}

/* Vive sobre la barra oscura de la navbar flotante: fondo *-soft y un borde
   del mismo tono al 35% (color-mix sobre currentColor), para que el estado se
   lea sin el contorno saturado que en la barra oscura quedaba "pegado". */
const PILL =
  "inline-flex h-8 items-center gap-1.5 rounded-full border border-[color:color-mix(in_srgb,currentColor_35%,transparent)] px-3 text-xs font-medium select-none";

/* Mismas utilidades que `NAV_ICON_BUTTON` de app/dashboard/page.tsx (no se exporta). */
const NAV_ICON_BUTTON_CLASSES =
  "inline-flex items-center justify-center w-8 h-8 shrink-0 rounded-fx-md text-fx-text-2 hover:text-fx-text hover:bg-fx-surface-3 transition-colors duration-fx-fast ease-fx fx-focus-ring";

const TONE_PILL: Record<AgentChipTone, string> = {
  success: "bg-fx-success-soft text-fx-success",
  warning: "bg-fx-warning-soft text-fx-warning",
  neutral: "bg-fx-surface-3 text-fx-text-2",
};

const TONE_TEXT: Record<AgentChipTone, string> = {
  success: "text-fx-success",
  warning: "text-fx-warning",
  neutral: "text-fx-text-2",
};

const TONE_DOT: Record<AgentChipTone, string> = {
  success: "bg-fx-success",
  warning: "bg-fx-warning",
  neutral: "bg-fx-text-3",
};

const ICONS: Record<AgentChipIcon, LucideIcon> = {
  wifi: Wifi,
  "wifi-off": WifiOff,
  unplug: Unplug,
};

/**
 * Estado del agente Tatana en la navbar (SDD wizard-pulido-ux §8.4).
 *
 * - Una sola región viva (`role="status"`, siempre montada, también < `sm`)
 *   con el texto completo: React solo muta el nodo si cambia, así que el lector
 *   anuncia cada cambio una vez.
 * - ≥ `sm`: la píldora de siempre (ícono + texto) y "REC", ocultas al lector.
 * - < `sm` con `compactInline` (wizard, donde el centro es un breadcrumb que
 *   trunca): indicador de 32 px (ícono + punto) dentro de la barra. Sin
 *   `compactInline` (historial, donde las pestañas de sección no truncan y ya
 *   no entran a 360–390 px) el indicador va debajo de la barra: `AgentChipBelowNav`.
 */
export function AgentChip({ compactInline = true, ...props }: Props & { compactInline?: boolean }) {
  const v = deriveAgentChip(props);
  const Icon = ICONS[v.icon];

  const pill = (
    <span className={cn(PILL, "min-w-0", TONE_PILL[v.tone])}>
      <Icon className="h-3 w-3 shrink-0" aria-hidden="true" />
      <span className="truncate max-w-[160px] lg:max-w-[280px]" title={v.tip ? undefined : v.text} translate="no">
        {v.text}
      </span>
    </span>
  );

  return (
    <>
      <span className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {v.label}
      </span>

      <div
        aria-hidden="true"
        className="hidden sm:flex items-center gap-1.5 min-w-0 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
      >
        {v.tip ? <FxTip label={v.tip} side="bottom">{pill}</FxTip> : pill}
        {v.recording && (
          <span className={cn(PILL, "bg-fx-danger-soft text-fx-danger font-bold")}>
            <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-fx-danger motion-safe:animate-pulse" />
            REC
          </span>
        )}
      </div>

      {compactInline && (
        <FxTip label={v.tip ?? v.label} side="bottom">
          <button
            type="button"
            aria-label={v.label}
            className={cn("sm:hidden relative", NAV_ICON_BUTTON_CLASSES, TONE_TEXT[v.tone])}
          >
            <Icon className="h-4 w-4" aria-hidden="true" />
            <span
              aria-hidden="true"
              className={cn(
                "absolute top-1 right-1 h-2 w-2 rounded-full ring-2 ring-[color:var(--fx-nav-pill-bg,transparent)]",
                v.recording ? "bg-fx-danger motion-safe:animate-pulse" : TONE_DOT[v.tone],
              )}
            />
          </button>
        </FxTip>
      )}
    </>
  );
}

/**
 * Indicador compacto < `sm` debajo de la barra (historial del dashboard). Punto
 * de estado + ícono + texto corto truncado; el nombre accesible es el mismo
 * texto completo que la región viva de `AgentChip` (que sigue siendo la única
 * `role="status"`). No es una acción: tocar o enfocar muestra el FxTip.
 */
export function AgentChipBelowNav(props: Props) {
  const v = deriveAgentChip(props);
  const Icon = ICONS[v.icon];
  return (
    <FxTip label={v.tip ?? v.label} side="bottom">
      <button
        type="button"
        aria-label={v.label}
        className={cn(
          "inline-flex h-8 max-w-full min-w-0 items-center gap-1.5 rounded-full border px-3 text-xs font-medium select-none fx-focus-ring",
          "border-[color:color-mix(in_srgb,currentColor_35%,transparent)]",
          TONE_PILL[v.tone],
        )}
      >
        <span
          aria-hidden="true"
          className={cn(
            "h-2 w-2 shrink-0 rounded-full",
            v.recording ? "bg-fx-danger motion-safe:animate-pulse" : TONE_DOT[v.tone],
          )}
        />
        <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        <span className="min-w-0 truncate" translate="no">{v.text}</span>
        {v.recording && <span aria-hidden="true" className="shrink-0 font-bold text-fx-danger">· REC</span>}
      </button>
    </FxTip>
  );
}
