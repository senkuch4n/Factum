import { HardDrive } from "lucide-react";

/** Chip "Evidencia en {hostname}" para un borrador cuya evidencia está en otra PC. */
export function EvidenceHostChip({ hostname }: { hostname: string }) {
  return (
    <span className="inline-flex max-w-full items-center gap-1 rounded-fx-sm border border-fx-border bg-fx-surface-2 px-1.5 py-px text-[11px] font-medium text-fx-text-2">
      <HardDrive className="h-3 w-3 shrink-0" aria-hidden="true" />
      <span className="truncate">Evidencia en <span translate="no">{hostname}</span></span>
    </span>
  );
}
