"use client";

import { cn } from "@/lib/utils";

export function SpecRow({ icon: Icon, label, value, mono = false, accent = false }: {
  icon: React.ElementType; label: string; value: string; mono?: boolean; accent?: boolean;
}) {
  return (
    <div className="flex items-center gap-2.5 py-2 last:border-0" style={{ borderBottom: "1px solid var(--border)" }}>
      <Icon className="w-3 h-3 flex-shrink-0" style={{ color: accent ? "var(--blue-lg)" : "var(--text-muted)" }} aria-hidden="true" />
      <span className="text-[10px] uppercase tracking-wider flex-shrink-0 w-14 font-semibold" style={{ color: "var(--text-muted)" }}>
        {label}
      </span>
      <span
        className={cn("text-[11px] font-medium flex-1 truncate text-right", mono && "font-mono")}
        style={{ color: accent ? "var(--blue-lg)" : "var(--text-secondary)" }}
        title={value}
      >
        {value}
      </span>
    </div>
  );
}
