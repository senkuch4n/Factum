"use client";

import { Button } from "primereact/button";
import {
  Clock, ClockCheck, FileCheck2, FileText, FolderOpen, Hash, Link2, Link2Off, Smartphone, type LucideIcon,
} from "lucide-react";
import type { AgentExtractionSnapshot, AgentDeviceModification } from "@/lib/agent";
import type { Extraction, ExtractionDeviceModification } from "@/lib/api";
import { extractionStatusView, interruptionReasonLabel } from "@/lib/extraction";
import { ProgressBar, StatusChip } from "@/components/extraction/common";
import { CategoryRow, fromDtoCategory, fromSnapshotCategory, type CategoryView } from "@/components/extraction/CategoryRow";
import { FileErrorsList } from "@/components/extraction/FileErrorsList";
import { CopyButton } from "@/components/feedback/CopyButton";
import { FxBanner } from "@/components/feedback/FxBanner";
import { formatBytes, formatDate, formatTime } from "@/lib/format";

/** Aviso transitorio del resultado de "Generar informe" (lo arma `ExtractionDetailScreen`). */
export interface ReportFeedback {
  tone: "success" | "warn" | "info";
  text: string;
  /** Si viene, el aviso ofrece un botón "Reintentar" (llama a `onGenerateReport`). */
  retry?: boolean;
}

interface Props {
  id: string;
  snapshot: AgentExtractionSnapshot | null;
  dto: Extraction | null;
  canReveal: boolean;
  onReveal: () => void;
  revealing: boolean;
  onOpenLink: () => void;
  onUnlink: () => void;
  unlinking: boolean;
  /** Genera el informe: lo descarga y, si Tatana lo tiene local, lo guarda en la carpeta. */
  onGenerateReport: () => void;
  generatingReport: boolean;
  /** `true` si el informe ya quedó en la carpeta (de `report_written_at` o de una generación de esta sesión). */
  reportWritten: boolean;
  reportFeedback: ReportFeedback | null;
}

export function ExtractionResult({
  id, snapshot, dto, canReveal, onReveal, revealing, onOpenLink, onUnlink, unlinking,
  onGenerateReport, generatingReport, reportWritten, reportFeedback,
}: Props) {
  const status = snapshot?.status ?? dto?.status ?? "completed";
  const manufacturer = snapshot?.manufacturer ?? dto?.device.manufacturer ?? "";
  const model = snapshot?.model ?? dto?.device.model ?? "";
  const categories: CategoryView[] = snapshot
    ? snapshot.categories.map(fromSnapshotCategory)
    : (dto?.categories ?? []).map(fromDtoCategory);
  const totalBytes = snapshot?.totals.bytes_total ?? dto?.totals.bytes ?? 0;
  const totalErrors = snapshot?.totals.errors ?? dto?.totals.errors ?? 0;
  const manifestSha = snapshot?.manifest_sha256 ?? dto?.manifest_sha256 ?? null;
  const folderPath = snapshot?.folder_path ?? (dto ? `${dto.destination_path}/${dto.folder_name}` : null);
  const interruption = snapshot?.interruption ?? dto?.interruption ?? null;
  const modifications: (AgentDeviceModification | ExtractionDeviceModification)[] =
    snapshot?.device_modifications ?? dto?.device_modifications ?? [];
  const onlyApi = !snapshot && !!dto;

  return (
    <div className="space-y-6">
      {/* Estado */}
      <div className="fx-card space-y-3 p-4 sm:p-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">
            <Smartphone className="h-4 w-4 text-fx-text-2" aria-hidden="true" />
            {manufacturer} {model}
          </span>
          <StatusChip view={extractionStatusView(status)} />
        </div>
        {interruption && (
          <FxBanner tone="warn" role="status">
            {interruptionReasonLabel(interruption.reason_code)}. Iniciá una extracción nueva para completar lo que falta.
          </FxBanner>
        )}
        {onlyApi && (
          <p className="m-0 text-xs text-fx-text-3">
            Esta extracción se hizo en otra PC: se muestran los datos registrados en Factum.
          </p>
        )}
      </div>

      {/* Resumen por categoría */}
      {categories.length > 0 && (
        <section aria-labelledby="result-cats" className="space-y-2">
          <div className="flex items-center justify-between">
            <h2 id="result-cats" className="text-fx-label uppercase text-fx-text-2">Resumen</h2>
            <span className="text-xs tabular-nums text-fx-text-2">{formatBytes(totalBytes)} en total</span>
          </div>
          <ul className="m-0 list-none space-y-1.5 p-0">
            {categories.map(c => <CategoryRow key={c.key} category={c} />)}
          </ul>
        </section>
      )}

      {/* Errores por archivo (solo si Tatana tiene la extracción) */}
      {snapshot && totalErrors > 0 && <FileErrorsList id={id} total={totalErrors} />}

      {/* Integridad: hash + sello */}
      <section aria-labelledby="result-integrity" className="space-y-3">
        <h2 id="result-integrity" className="text-fx-label uppercase text-fx-text-2">Integridad</h2>
        {manifestSha && (
          <div className="rounded-fx-md border border-fx-border bg-fx-surface-2 p-3">
            <p className="m-0 flex items-center gap-1.5 text-xs text-fx-text-3">
              <Hash className="h-3.5 w-3.5" aria-hidden="true" /> SHA-256 del manifiesto
            </p>
            <p className="m-0 mt-1 break-all font-mono text-xs text-fx-text" translate="no">{manifestSha}</p>
            <div className="mt-2">
              <CopyButton text={manifestSha} label="Copiar el hash del manifiesto" />
            </div>
          </div>
        )}
        <SealChip dto={dto} />
      </section>

      {/* Ubicación */}
      {folderPath && (
        <section aria-labelledby="result-location" className="space-y-2">
          <h2 id="result-location" className="text-fx-label uppercase text-fx-text-2">Ubicación</h2>
          <div className="rounded-fx-md border border-fx-border bg-fx-surface-2 p-3">
            <p className="m-0 break-all font-mono text-xs text-fx-text" translate="no">{folderPath}</p>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              {snapshot && canReveal && (
                <Button
                  severity="secondary"
                  size="small"
                  icon={<FolderOpen className="h-3.5 w-3.5" aria-hidden="true" />}
                  label={revealing ? "Abriendo…" : "Abrir carpeta"}
                  onClick={onReveal}
                  disabled={revealing}
                  className="min-h-11"
                />
              )}
              <Button
                severity="secondary"
                size="small"
                icon={<FileText className="h-3.5 w-3.5" aria-hidden="true" />}
                label={generatingReport ? "Generando…" : "Generar informe"}
                onClick={onGenerateReport}
                disabled={generatingReport || status === "running"}
                aria-busy={generatingReport}
                loading={generatingReport}
                title={status === "running" ? "Disponible al terminar la extracción" : undefined}
                className="min-h-11"
              />
              <CopyButton text={folderPath} label="Copiar la ruta de la carpeta" buttonLabel="Copiar ruta" />
            </div>
            {reportWritten && !reportFeedback && (
              <p className="m-0 mt-2 inline-flex items-center gap-1.5 text-xs text-fx-text-3">
                <FileCheck2 className="h-3.5 w-3.5 text-fx-success" aria-hidden="true" />
                Informe en la carpeta
              </p>
            )}
            {reportFeedback && (
              <FxBanner
                tone={reportFeedback.tone}
                className="mt-2"
                role={reportFeedback.tone === "warn" ? "alert" : "status"}
              >
                <span>{reportFeedback.text}</span>
                {reportFeedback.retry && (
                  <button
                    type="button"
                    onClick={onGenerateReport}
                    disabled={generatingReport}
                    className="ml-1.5 font-semibold underline underline-offset-2 hover:no-underline disabled:opacity-60 fx-focus-ring"
                  >
                    Reintentar
                  </button>
                )}
              </FxBanner>
            )}
          </div>
        </section>
      )}

      {/* Caso asociado */}
      <section aria-labelledby="result-case" className="space-y-2">
        <h2 id="result-case" className="text-fx-label uppercase text-fx-text-2">Caso</h2>
        <div className="flex flex-wrap items-center gap-2 rounded-fx-md border border-fx-border bg-fx-surface-2 p-3">
          <span className="flex-1 text-fx-body-sm text-fx-text">
            {dto?.case_id ? `Asociada a la causa ${dto.case_ref ?? dto.case_id}` : "Sin caso asociado"}
          </span>
          <Button
            severity="secondary"
            size="small"
            icon={<Link2 className="h-3.5 w-3.5" aria-hidden="true" />}
            label={dto?.case_id ? "Cambiar caso" : "Asociar a un caso"}
            onClick={onOpenLink}
            disabled={!dto || status === "running"}
            className="min-h-11"
          />
          {dto?.case_id && (
            <Button
              severity="secondary"
              size="small"
              text
              icon={<Link2Off className="h-3.5 w-3.5" aria-hidden="true" />}
              label={unlinking ? "Desasociando…" : "Desasociar"}
              onClick={onUnlink}
              disabled={unlinking}
              className="min-h-11"
            />
          )}
        </div>
      </section>

      {/* Modificaciones al dispositivo */}
      <section aria-labelledby="result-mods" className="space-y-2">
        <h2 id="result-mods" className="text-fx-label uppercase text-fx-text-2">Modificaciones al dispositivo</h2>
        {modifications.length === 0 ? (
          <p className="m-0 rounded-fx-md border border-fx-border bg-fx-surface-2 p-3 text-fx-body-sm text-fx-text">
            Ninguna.
          </p>
        ) : (
          <ul className="m-0 list-none space-y-1.5 p-0">
            {modifications.map((m, i) => (
              <li key={`${m.kind}-${m.at}-${i}`} className="rounded-fx-md border border-fx-border bg-fx-surface-2 px-3 py-2">
                <p className="m-0 text-fx-body-sm text-fx-text">
                  {modKindLabel(m.kind)}{m.after_close ? " (después del cierre)" : ""}
                </p>
                <p className="m-0 mt-0.5 text-xs text-fx-text-3">
                  {formatDate(m.at)} · {formatTime(m.at)}{m.detail ? ` · ${m.detail}` : ""}
                </p>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

const MOD_LABEL: Record<string, string> = {
  connector_installed: "Se instaló la app auxiliar de Factum",
  connector_uninstalled: "Se desinstaló la app auxiliar de Factum",
  stay_awake_set: "Se mantuvo la pantalla encendida",
  stay_awake_restored: "Se restauró el tiempo de pantalla",
};
function modKindLabel(kind: string): string {
  return MOD_LABEL[kind] ?? kind;
}

function SealChip({ dto }: { dto: Extraction | null }) {
  const ts = dto?.manifest_timestamp ?? null;
  let Icon: LucideIcon = Clock;
  let label = "Sin sellado de tiempo";
  let cls = "bg-fx-surface-3 text-fx-text-2";
  if (ts) {
    if (ts.status === "granted") {
      Icon = ClockCheck;
      label = `Sellado el ${ts.gen_time_display ?? (ts.granted_at ? `${formatDate(ts.granted_at)} ${formatTime(ts.granted_at)}` : "")}`.trim();
      cls = "bg-fx-success-soft text-fx-success";
    } else if (ts.status === "pending") {
      label = "Sello de tiempo pendiente";
      cls = "bg-fx-warning-soft text-fx-warning";
    } else {
      label = "No se pudo obtener el sello de tiempo";
      cls = "bg-fx-danger-soft text-fx-danger";
    }
  }
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-fx-pill px-2.5 py-1 text-xs font-semibold ${cls}`}>
      <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      {label}
    </span>
  );
}
