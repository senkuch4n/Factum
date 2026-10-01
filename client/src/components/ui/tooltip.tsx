"use client";

/**
 * Tooltip — adaptado de scrollxui (docs/components/tooltip).
 *
 * El original cuelga de `@radix-ui/react-tooltip` con tokens Tailwind v4 y una
 * animación de "wobble" (x: [0,-4,4,-3,3,…]) que el design system de factum
 * evita. Acá se reconstruye sobre `@base-ui/react` (dep ya instalada) con tokens
 * de factum y una entrada sobria (fade + leve escala). Portal + Positioner
 * para que no lo recorte ningún contenedor (p. ej. la top bar con clip-path).
 *
 * Uso habitual:
 *   <Tip label="Actualizar"><button aria-label="Actualizar">…</button></Tip>
 */

import * as React from "react";
import { Tooltip as BaseTooltip } from "@base-ui/react/tooltip";
import { cn } from "@/lib/utils";

export function TooltipProvider({ children }: { children: React.ReactNode }) {
  return (
    <BaseTooltip.Provider delay={350} closeDelay={0}>
      {children}
    </BaseTooltip.Provider>
  );
}

interface TipProps {
  label: React.ReactNode;
  children: React.ReactElement;
  side?: "top" | "bottom" | "left" | "right";
  sideOffset?: number;
  /** desactiva el tooltip (p. ej. cuando no aporta) */
  disabled?: boolean;
}

export function Tip({ label, children, side = "top", sideOffset = 6, disabled }: TipProps) {
  if (disabled || label == null || label === "") return children;

  return (
    <BaseTooltip.Root>
      <BaseTooltip.Trigger render={children} />
      <BaseTooltip.Portal>
        <BaseTooltip.Positioner side={side} sideOffset={sideOffset} className="z-[60]">
          <BaseTooltip.Popup
            className={cn(
              "max-w-[220px] rounded-md px-2 py-1 text-xs font-medium shadow-md",
              "origin-[var(--transform-origin)] transition-[opacity,transform] duration-150",
              "data-[starting-style]:opacity-0 data-[starting-style]:scale-95",
              "data-[ending-style]:opacity-0 data-[ending-style]:scale-95",
            )}
            style={{
              background: "var(--text-primary)",
              color: "var(--bg-base)",
              border: "1px solid var(--border-md)",
            }}
          >
            {label}
          </BaseTooltip.Popup>
        </BaseTooltip.Positioner>
      </BaseTooltip.Portal>
    </BaseTooltip.Root>
  );
}

export const Tooltip = BaseTooltip;
