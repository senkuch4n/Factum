"use client";

import { Button } from "primereact/button";
import { Smartphone, Calendar, Clock, UserCheck, Archive, FileText, Play, HardDrive } from "lucide-react";
import type { Case } from "@/lib/api";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate, formatTime } from "@/lib/format";
import { caseStatusOf } from "@/lib/case-status";
import { FX_BUTTON_PRIMARY, FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";
import { StatusBadge } from "./StatusBadge";
import { useAgentIdentity } from "@/hooks/useAgentIdentity";
import { EvidenceHostChip } from "./CaseCard";
import { ZipLocalActions } from "./ZipLocalActions";

/* La tarjeta no es un control: se eleva al hover y cuando el foco está en sus
   acciones. Con reduced motion, --fx-lift vale 0 y solo cambian sombra y borde. */
const LIFT = [
  "transition-[transform,box-shadow,border-color] duration-fx-base ease-fx",
  // Hover solo con puntero real (como .fx-card-interactive): en touch quedaría "pegado".
  "[@media(hover:hover)_and_(pointer:fine)]:hover:[transform:translateY(var(--fx-lift))]",
  "[@media(hover:hover)_and_(pointer:fine)]:hover:shadow-fx-3 [@media(hover:hover)_and_(pointer:fine)]:hover:border-fx-accent",
  "focus-within:[transform:translateY(var(--fx-lift))] focus-within:shadow-fx-3 focus-within:border-fx-accent",
].join(" ");

const LINK_SMALL = "flex-1 min-h-11 px-3 py-1.5 text-xs";

/** Inspección en la vista cuadrícula. El padre es un `ul`. */
export function CaseGridCard({ cas, onResume }: { cas: Case; onResume: (c: Case) => void }) {
  const dlURL = (filename: string) => api.downloadURL(cas.id, filename);
  const isDone = cas.status === "completed";
  const isDraft = cas.status === "draft";
  const hasDownloads = isDone && (cas.zip_filename || cas.pdf_filename);
  // Flujo agent (zip-local-informe-servidor §7.7): sin consultar el estado; el botón reacciona al 404.
  const agentFlow = cas.evidence_storage === "agent";
  const identity = useAgentIdentity();
  const sameHost = agentFlow && identity.isSameHost(cas);
  const draftHost = agentFlow && isDraft && cas.evidence_host?.hostname && identity.status === "online" && !sameHost
    ? cas.evidence_host.hostname
    : null;

  return (
    <li className={cn("fx-card relative flex flex-col overflow-hidden", LIFT)}>
      <span className={cn("h-1 w-full", caseStatusOf(cas.status).stripe)} aria-hidden="true" />

      <div className="flex-1 p-5 space-y-3">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="text-fx-h3 text-fx-text truncate">{cas.nro_referencia}</p>
            <p className="mt-0.5 text-fx-body-sm text-fx-text-2 truncate">
              {cas.caratula || cas.nombre_denunciante}
              {cas.dni_denunciante && ` · DNI ${cas.dni_denunciante}`}
            </p>
          </div>
          <StatusBadge status={cas.status} />
        </div>

        <div className="space-y-1 text-xs text-fx-text-3">
          <p className="flex items-center gap-1.5 min-w-0">
            <Smartphone className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
            <span className="truncate">{cas.device.manufacturer} {cas.device.model}</span>
          </p>
          <p className="flex items-center gap-3">
            <span className="flex items-center gap-1.5">
              <Calendar className="w-3.5 h-3.5 shrink-0" aria-hidden="true" /> {formatDate(cas.created_at)}
            </span>
            <span className="flex items-center gap-1.5">
              <Clock className="w-3.5 h-3.5 shrink-0" aria-hidden="true" /> {formatTime(cas.created_at)}
            </span>
          </p>
          <p className="flex items-center gap-1.5 min-w-0">
            <UserCheck className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
            <span className="truncate"><span className="sr-only">Perito: </span>{cas.perito?.nombre ?? cas.officer.name}</span>
          </p>
          {isDone && agentFlow && cas.zip_location && (
            <p className="flex items-center gap-1.5 min-w-0" title={`Guardado en ${cas.zip_location.hostname} · ${cas.zip_location.path}`}>
              <HardDrive className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
              <span className="truncate">
                Guardado en <span translate="no">{cas.zip_location.hostname}</span> · <span translate="no" className="font-mono">{cas.zip_location.path}</span>
              </span>
            </p>
          )}
          {draftHost && <EvidenceHostChip hostname={draftHost} />}
        </div>
      </div>

      {isDone && agentFlow && sameHost && cas.zip_filename && (
        <div className="px-4 pt-3 border-t border-fx-border">
          <ZipLocalActions
            caseId={cas.id}
            caseRef={cas.nro_referencia}
            zipFilename={cas.zip_filename}
            size="compact"
            label={`de la causa ${cas.nro_referencia}`}
          />
        </div>
      )}
      {(hasDownloads || isDraft) && (
        <div className={cn("px-4 pb-4 pt-3 flex gap-2", !(isDone && agentFlow && sameHost && cas.zip_filename) && "border-t border-fx-border")}>
          {isDraft && (
            <Button
              size="small"
              icon={<Play className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Retomar"
              onClick={() => onResume(cas)}
              className="flex-1 min-h-11"
            />
          )}
          {isDone && cas.zip_filename && !agentFlow && (
            <a
              href={dlURL(cas.zip_filename)}
              aria-label={`Descargar ZIP de la causa ${cas.nro_referencia}`}
              className={cn(FX_BUTTON_PRIMARY, LINK_SMALL)}
            >
              <Archive className="w-3.5 h-3.5" aria-hidden="true" /> ZIP
            </a>
          )}
          {isDone && cas.pdf_filename && (
            <a
              href={dlURL(cas.pdf_filename)}
              aria-label={`Descargar Word de la causa ${cas.nro_referencia}`}
              className={cn(FX_BUTTON_SECONDARY, LINK_SMALL)}
            >
              <FileText className="w-3.5 h-3.5" aria-hidden="true" /> Word
            </a>
          )}
        </div>
      )}
    </li>
  );
}
