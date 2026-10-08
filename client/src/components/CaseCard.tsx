"use client";

import { ChevronRight, Smartphone, Calendar, Clock, FolderOpen } from "lucide-react";
import type { Case } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate, formatTime } from "@/lib/format";
import { StatusBadge } from "./StatusBadge";
import { EvidenceHostChip } from "./case-detail/EvidenceHostChip";
import { useCaseAgentFlow } from "./case-detail/useCaseAgentFlow";

/**
 * Inspección en la vista lista: una fila compacta que abre el detalle en el
 * modal compartido (`CaseDetailModal`, estado en `CaseHistory`). El padre es
 * un `ul`.
 */
export function CaseCard({ cas, onOpen }: { cas: Case; onOpen: (c: Case, trigger: HTMLElement) => void }) {
  const isDone = cas.status === "completed";
  const { draftHost } = useCaseAgentFlow(cas);

  return (
    <li className="fx-card overflow-hidden">
      <button
        type="button"
        className="group w-full text-left p-4 flex items-center gap-4 hover:bg-fx-surface-2 transition-colors duration-fx-fast ease-fx fx-focus-ring"
        onClick={(e) => onOpen(cas, e.currentTarget)}
        aria-haspopup="dialog"
        aria-label={`Causa ${cas.nro_referencia} — ver detalle`}
      >
        <span
          className={cn(
            "w-10 h-10 rounded-fx-md border flex items-center justify-center shrink-0",
            isDone
              ? "bg-fx-success-soft border-fx-success text-fx-success"
              : "bg-fx-surface-2 border-fx-border text-fx-text-3",
          )}
        >
          <FolderOpen className="w-5 h-5" strokeWidth={1.5} aria-hidden="true" />
        </span>

        <span className="flex-1 min-w-0">
          <span className="flex items-center gap-2 flex-wrap">
            <span className="text-fx-body-sm font-semibold text-fx-text">{cas.nro_referencia}</span>
            <StatusBadge status={cas.status} />
            {draftHost && <EvidenceHostChip hostname={draftHost} />}
          </span>
          <span className="block mt-0.5 text-xs text-fx-text-2 truncate">
            {cas.caratula || cas.nombre_denunciante}
            {cas.dni_denunciante && ` · DNI ${cas.dni_denunciante}`}
          </span>
          <span className="flex items-center gap-x-3 gap-y-1 mt-1 flex-wrap text-xs text-fx-text-3">
            <span className="flex items-center gap-1">
              <Smartphone className="w-3 h-3" aria-hidden="true" /> {cas.device.manufacturer} {cas.device.model}
            </span>
            <span className="flex items-center gap-1">
              <Calendar className="w-3 h-3" aria-hidden="true" /> {formatDate(cas.created_at)}
            </span>
            <span className="flex items-center gap-1">
              <Clock className="w-3 h-3" aria-hidden="true" /> {formatTime(cas.created_at)}
            </span>
          </span>
        </span>

        {/* Afordancia de "abrir": se corre un poco al pasar el puntero o enfocar. */}
        <ChevronRight
          className="w-4 h-4 shrink-0 text-fx-text-3 transition-[transform,color] duration-fx-fast ease-fx group-hover:text-fx-text-2 group-focus-visible:text-fx-text-2 motion-safe:group-hover:translate-x-0.5 motion-safe:group-focus-visible:translate-x-0.5"
          aria-hidden="true"
        />
      </button>
    </li>
  );
}
