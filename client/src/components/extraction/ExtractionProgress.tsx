"use client";

import { useEffect, useRef, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { AlertTriangle, Gauge, Loader2, Smartphone, XCircle } from "lucide-react";
import { agent, type AgentExtractionSnapshot } from "@/lib/agent";
import { EtaIcon, extractionStatusView, formatEta, globalProgress, phaseLabel } from "@/lib/extraction";
import { ProgressBar, StatusChip } from "@/components/extraction/common";
import { CategoryRow, fromSnapshotCategory } from "@/components/extraction/CategoryRow";
import { FxBanner } from "@/components/feedback/FxBanner";
import { formatBytes } from "@/lib/format";

/** Vista "en curso" (§10.5): barra global, fase, velocidad/ETA y filas por categoría. */
export function ExtractionProgress({ snapshot, onChanged }: {
  snapshot: AgentExtractionSnapshot;
  onChanged: () => void;
}) {
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const { pct, detail } = globalProgress(snapshot);

  // Fase anunciada en una región polite: se actualiza solo al cambiar de fase (no en cada tick).
  const [livePhase, setLivePhase] = useState("");
  const lastPhase = useRef<string>("");
  useEffect(() => {
    if (snapshot.phase !== lastPhase.current) {
      lastPhase.current = snapshot.phase;
      setLivePhase(phaseLabel(snapshot.phase));
    }
  }, [snapshot.phase]);

  const bps = snapshot.totals.bytes_per_second;
  const eta = formatEta(snapshot.totals.eta_seconds);

  async function cancel() {
    setCancelling(true);
    try { await agent.cancelExtraction(snapshot.id); onChanged(); }
    catch { /* la vista en vivo reflejará el estado */ }
    finally { setCancelling(false); setConfirmOpen(false); }
  }

  return (
    <div className="space-y-6">
      <div className="fx-card space-y-4 p-4 sm:p-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">
            <Smartphone className="h-4 w-4 text-fx-text-2" aria-hidden="true" />
            {snapshot.manufacturer} {snapshot.model}
          </span>
          <StatusChip view={extractionStatusView(snapshot.status)} />
        </div>

        <div className="space-y-2">
          <ProgressBar value={pct} label="Progreso de la extracción" valueText={detail} />
          <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-xs text-fx-text-2">
            <span className="flex items-center gap-1.5">
              <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
              {phaseLabel(snapshot.phase)}
            </span>
            <span className="flex flex-wrap items-center gap-x-3 gap-y-1 tabular-nums">
              <span>{detail}</span>
              {typeof bps === "number" && bps > 0 && (
                <span className="flex items-center gap-1">
                  <Gauge className="h-3.5 w-3.5" aria-hidden="true" />{formatBytes(bps)}/s
                </span>
              )}
              {eta && (
                <span className="flex items-center gap-1">
                  <EtaIcon className="h-3.5 w-3.5" aria-hidden="true" />{eta}
                </span>
              )}
            </span>
          </div>
        </div>

        {/* Fase anunciada a lectores de pantalla, como mucho al cambiar de fase. */}
        <p aria-live="polite" className="sr-only">{livePhase}</p>
      </div>

      <section aria-labelledby="progress-cats" className="space-y-2">
        <h2 id="progress-cats" className="text-fx-label uppercase text-fx-text-2">Categorías</h2>
        <ul className="m-0 list-none space-y-1.5 p-0">
          {snapshot.categories.map(c => (
            <CategoryRow key={c.key} category={fromSnapshotCategory(c)} />
          ))}
        </ul>
      </section>

      <FxBanner tone="info" role="note" icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
        Podés cerrar esta pestaña; la extracción sigue en Tatana. No desconectes el celular.
      </FxBanner>

      <Button
        severity="danger"
        outlined
        icon={<XCircle className="h-4 w-4" aria-hidden="true" />}
        label="Cancelar extracción"
        onClick={() => setConfirmOpen(true)}
        className="min-h-11"
      />

      <Dialog
        visible={confirmOpen}
        onHide={() => setConfirmOpen(false)}
        draggable={false}
        resizable={false}
        header="¿Cancelar la extracción?"
        pt={{ root: { className: "w-[min(26rem,100%)]" } }}
        footer={
          <>
            <Button severity="secondary" label="Seguir extrayendo" onClick={() => setConfirmOpen(false)} className="min-h-11" />
            <Button
              severity="danger"
              label={cancelling ? "Cancelando…" : "Cancelar extracción"}
              icon={cancelling ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : undefined}
              onClick={cancel}
              disabled={cancelling}
              className="min-h-11"
            />
          </>
        }
      >
        <p className="m-0 text-fx-body-sm text-fx-text-2">
          Lo que ya se copió no se borra: queda en la carpeta con un acta parcial. No vas a poder reanudar: tendrías que
          empezar una extracción nueva.
        </p>
      </Dialog>
    </div>
  );
}
