"use client";

import Link from "next/link";
import { Checkbox } from "primereact/checkbox";
import { CheckCircle2, ShieldAlert, Smartphone } from "lucide-react";
import type { AgentExtractionEstimate, AgentExtractionPreflight } from "@/lib/agent";
import { categoryLabel, type ExtractionSelection } from "@/lib/extraction";
import { StepHeader } from "@/components/wizard/StepHeader";
import { FxBanner } from "@/components/feedback/FxBanner";
import { formatBytes } from "@/lib/format";

export interface StartError { message: string; vigenteId?: string }

interface Props {
  preflight: AgentExtractionPreflight;
  selection: ExtractionSelection;
  estimate: AgentExtractionEstimate | null;
  finalFolderPreview: string | null;
  modifications: string[];
  confirmChecked: boolean;
  onConfirmChange: (v: boolean) => void;
  startError: StartError | null;
}

/** Paso 4 (§10.4.4): resumen, modificaciones al dispositivo y confirmación. */
export function ConfirmStep({
  preflight, selection, estimate, finalFolderPreview, modifications, confirmChecked, onConfirmChange, startError,
}: Props) {
  const selectedKeys = selection.keys;
  const labels = selectedKeys.map(k => categoryLabel(k));
  if (selection.customPaths.length > 0) {
    labels.push(`${selection.customPaths.length} carpeta${selection.customPaths.length === 1 ? "" : "s"} elegida${selection.customPaths.length === 1 ? "" : "s"}`);
  }
  const needsConfirm = modifications.length > 0;

  return (
    <div className="space-y-6">
      <StepHeader title="Confirmar" description="Revisá lo que se va a extraer antes de empezar." />

      <dl className="grid grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2">
        <SummaryItem label="Dispositivo" full>
          <span className="flex items-center gap-2">
            <Smartphone className="h-4 w-4 text-fx-text-2" aria-hidden="true" />
            {preflight.manufacturer} {preflight.model} · Android {preflight.android_version}
          </span>
        </SummaryItem>
        <SummaryItem label="Categorías" full>
          {labels.length > 0 ? labels.join(", ") : "Ninguna"}
        </SummaryItem>
        <SummaryItem label="Total estimado">
          <span className="tabular-nums">{estimate ? formatBytes(estimate.total_bytes) : "—"}</span>
        </SummaryItem>
        <SummaryItem label="Verificación">
          {selection.verifyOnDevice && preflight.device_sha256_available ? "También en el celular" : "Solo en la PC"}
        </SummaryItem>
        {finalFolderPreview && (
          <SummaryItem label="Destino" full>
            <span className="break-all font-mono text-xs" translate="no">{finalFolderPreview}</span>
          </SummaryItem>
        )}
      </dl>

      {/* Modificaciones al dispositivo */}
      <section aria-labelledby="confirm-mods" className="space-y-2">
        <h3 id="confirm-mods" className="text-fx-label uppercase text-fx-text-2">Modificaciones al dispositivo</h3>
        {needsConfirm ? (
          <ul className="m-0 list-none space-y-1.5 rounded-fx-md border border-fx-warning-soft bg-fx-warning-soft/40 p-3 pl-4">
            {modifications.map(m => (
              <li key={m} className="flex items-start gap-2 text-fx-body-sm text-fx-text">
                <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-fx-warning" aria-hidden="true" />
                {m}
              </li>
            ))}
          </ul>
        ) : (
          <p className="m-0 flex items-center gap-2 rounded-fx-md border border-fx-border bg-fx-surface-2 p-3 text-fx-body-sm text-fx-text">
            <CheckCircle2 className="h-4 w-4 shrink-0 text-fx-success" aria-hidden="true" />
            Ninguna: solo lectura.
          </p>
        )}

        {needsConfirm && (
          <label htmlFor="confirm-mods-check" className="flex cursor-pointer items-start gap-3 rounded-fx-md px-2 py-2 hover:bg-fx-surface-2">
            <Checkbox inputId="confirm-mods-check" checked={confirmChecked} onChange={e => onConfirmChange(!!e.checked)} className="mt-0.5" />
            <span className="text-fx-body-sm text-fx-text">
              Entiendo que Factum va a instalar temporalmente su app auxiliar y la va a desinstalar al terminar.
            </span>
          </label>
        )}
      </section>

      {startError && (
        <FxBanner tone="error">
          <div className="space-y-1">
            <span>{startError.message}</span>
            {startError.vigenteId && (
              <Link
                href={`/extracciones/${startError.vigenteId}`}
                className="block font-semibold text-fx-accent-text underline underline-offset-2"
              >
                Ver la extracción en curso
              </Link>
            )}
          </div>
        </FxBanner>
      )}
    </div>
  );
}

function SummaryItem({ label, children, full }: { label: string; children: React.ReactNode; full?: boolean }) {
  return (
    <div className={full ? "sm:col-span-2" : undefined}>
      <dt className="text-fx-label uppercase text-fx-text-3">{label}</dt>
      <dd className="mt-1 text-fx-body-sm text-fx-text">{children}</dd>
    </div>
  );
}
