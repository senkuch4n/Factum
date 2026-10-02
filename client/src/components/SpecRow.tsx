"use client";

import { cn } from "@/lib/utils";

/**
 * Fila de una lista de definición (`<dl>` en el paso "Causa").
 * `accent` = dato destacado (no es selección ni CTA: sin color de acento).
 */
export function SpecRow({ icon: Icon, label, value, mono = false, accent = false }: {
  icon: React.ElementType; label: string; value: string; mono?: boolean; accent?: boolean;
}) {
  return (
    <div className="flex items-center gap-2.5 border-b border-fx-border py-2 last:border-0">
      <Icon className={cn("h-3.5 w-3.5 shrink-0", accent ? "text-fx-text-2" : "text-fx-text-3")} aria-hidden="true" />
      <dt className="w-16 shrink-0 text-fx-label uppercase text-fx-text-3">{label}</dt>
      <dd
        title={value}
        translate={mono ? "no" : undefined}
        className={cn(
          "m-0 flex-1 truncate text-right text-xs",
          mono && "font-mono",
          accent ? "font-semibold text-fx-text" : "text-fx-text-2",
        )}
      >
        {value}
      </dd>
    </div>
  );
}
