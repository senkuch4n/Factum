"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { ChevronRight, HardDriveDownload, Link2, Loader2, Plus } from "lucide-react";
import { api, type Extraction } from "@/lib/api";
import { FxBanner } from "@/components/feedback/FxBanner";
import { StatusChip } from "@/components/extraction/common";
import { CopyButton } from "@/components/feedback/CopyButton";
import { extractionStatusView } from "@/lib/extraction";
import { formatBytes, formatDate, formatTime } from "@/lib/format";

/**
 * Bloque "Extracciones asociadas" del detalle del caso (§10.6). Lista las
 * extracciones del caso y permite asociar una que todavía no tenga caso.
 * Si el backend no admite extracciones (404), el bloque no se muestra.
 */
export function AssociatedExtractions({ caseId }: { caseId: string }) {
  const [list, setList] = useState<Extraction[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [unsupported, setUnsupported] = useState(false);
  const [attachOpen, setAttachOpen] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const r = await api.listExtractions({ caseId });
      setList(r.extractions);
      setUnsupported(false);
    } catch {
      setUnsupported(true);
    } finally {
      setLoading(false);
    }
  }, [caseId]);

  useEffect(() => { void load(); }, [load]);

  if (unsupported) return null;

  return (
    <section aria-labelledby={`ext-${caseId}`} className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <h3 id={`ext-${caseId}`} className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
          <HardDriveDownload className="h-3.5 w-3.5" aria-hidden="true" />
          Extracciones asociadas
        </h3>
        <Button
          severity="secondary"
          size="small"
          icon={<Plus className="h-3.5 w-3.5" aria-hidden="true" />}
          label="Asociar extracción"
          onClick={() => setAttachOpen(true)}
          className="min-h-11"
        />
      </div>

      {loading ? (
        <div role="status" className="flex justify-center py-6">
          <Loader2 className="h-5 w-5 animate-spin text-fx-text-3" aria-hidden="true" />
          <span className="sr-only">Cargando…</span>
        </div>
      ) : (list?.length ?? 0) === 0 ? (
        <p className="m-0 rounded-fx-md border border-dashed border-fx-border-strong p-4 text-center text-fx-body-sm text-fx-text-2">
          Este caso no tiene extracciones asociadas.
        </p>
      ) : (
        <ul className="m-0 list-none space-y-2.5 p-0">
          {(list ?? []).map(e => <ExtractionCard key={e.id} ext={e} />)}
        </ul>
      )}

      <AttachExtractionDialog
        open={attachOpen}
        caseId={caseId}
        onClose={() => setAttachOpen(false)}
        onAttached={() => { setAttachOpen(false); void load(); }}
      />
    </section>
  );
}

function ExtractionCard({ ext }: { ext: Extraction }) {
  return (
    <li className="fx-card space-y-2 p-3.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-fx-body-sm font-semibold text-fx-text">{ext.device.manufacturer} {ext.device.model}</span>
        <StatusChip view={extractionStatusView(ext.status)} />
      </div>
      <p className="m-0 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-fx-text-3">
        <span>{formatDate(ext.started_at_pc)} · {formatTime(ext.started_at_pc)}</span>
        <span className="tabular-nums">{ext.totals.files.toLocaleString("es-AR")} archivos · {formatBytes(ext.totals.bytes)}</span>
      </p>
      {ext.manifest_sha256 && (
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-mono text-xs text-fx-text-3" translate="no">sha256: {ext.manifest_sha256.slice(0, 16)}…</span>
          <CopyButton text={ext.manifest_sha256} label="Copiar el hash del manifiesto" buttonLabel="Copiar" />
        </div>
      )}
      <Link
        href={`/extracciones/${ext.id}`}
        className="inline-flex items-center gap-1 text-fx-body-sm font-semibold text-fx-accent-text no-underline"
      >
        Ver extracción
        <ChevronRight className="h-4 w-4" aria-hidden="true" />
      </Link>
    </li>
  );
}

function AttachExtractionDialog({
  open, caseId, onClose, onAttached,
}: { open: boolean; caseId: string; onClose: () => void; onAttached: () => void }) {
  const [list, setList] = useState<Extraction[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    setLoading(true);
    api.listExtractions({ unlinked: true })
      .then(r => setList(r.extractions))
      .catch(() => setError("No se pudo cargar la lista de extracciones."))
      .finally(() => setLoading(false));
  }, [open]);

  async function attach(id: string) {
    setBusyId(id);
    setError(null);
    try { await api.linkExtraction(id, caseId); onAttached(); }
    catch { setError("No se pudo asociar la extracción."); }
    finally { setBusyId(null); }
  }

  return (
    <Dialog
      visible={open}
      onHide={onClose}
      draggable={false}
      resizable={false}
      header="Asociar una extracción"
      pt={{ root: { className: "w-[min(32rem,100%)]" } }}
    >
      <div className="space-y-3">
        <FxBanner tone="info" role="note">Solo aparecen las extracciones terminadas y sin caso.</FxBanner>
        {error && <FxBanner tone="error">{error}</FxBanner>}
        {loading ? (
          <div role="status" className="flex justify-center py-8">
            <Loader2 className="h-5 w-5 animate-spin text-fx-text-3" aria-hidden="true" />
            <span className="sr-only">Cargando…</span>
          </div>
        ) : (list?.length ?? 0) === 0 ? (
          <p className="m-0 py-8 text-center text-fx-body-sm text-fx-text-2">No hay extracciones sin asociar.</p>
        ) : (
          <ul className="m-0 max-h-72 list-none space-y-1.5 overflow-y-auto p-0">
            {(list ?? []).map(e => (
              <li key={e.id}>
                <button
                  type="button"
                  onClick={() => void attach(e.id)}
                  disabled={busyId !== null}
                  className="fx-card fx-card-interactive flex min-h-11 w-full items-center gap-3 p-3 text-left disabled:opacity-60"
                >
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-fx-body-sm font-semibold text-fx-text">
                      {e.device.manufacturer} {e.device.model}
                    </span>
                    <span className="mt-0.5 block text-xs text-fx-text-3">
                      {formatDate(e.started_at_pc)} · {formatBytes(e.totals.bytes)}
                    </span>
                  </span>
                  {busyId === e.id
                    ? <Loader2 className="h-4 w-4 shrink-0 animate-spin text-fx-text-3" aria-hidden="true" />
                    : <Link2 className="h-4 w-4 shrink-0 text-fx-text-3" aria-hidden="true" />}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Dialog>
  );
}
