"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Button } from "primereact/button";
import {
  Archive, CheckCircle2, ChevronDown, Clock, ClockAlert, ClockCheck, Download, FileText, FolderInput, Info, Paperclip,
  type LucideIcon,
} from "lucide-react";
import { api, ApiError, type Case, type CaseTimestampsResponse } from "@/lib/api";
import {
  evidenceSummary, hasGranted, hasPending, syncCaseSeals, timestampLine, timestampPanelMode,
  type SyncSealsResult, type TimestampLine, type TimestampLineContext, type TimestampTone,
} from "@/lib/timestamps";
import { useTokenVersion } from "@/lib/session";
import { FOCUS_RING, FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";

/** Polling mientras haya sellos pendientes y la cola los esté reintentando (§9.3). */
const POLL_MS = 30_000;

const TONE_ICON: Record<TimestampTone, LucideIcon> = {
  success: ClockCheck,
  pending: Clock,
  neutral: Info,
  warning: ClockAlert,
};

const TONE_CLASS: Record<TimestampTone, string> = {
  success: "text-fx-success",
  pending: "text-fx-text-3",
  neutral: "text-fx-text-3",
  warning: "text-fx-warning",
};

/** Lectura de pantalla del tono: el estado nunca queda solo en el color o el ícono. */
const TONE_SR: Record<TimestampTone, string> = {
  success: "Sellado.",
  pending: "Pendiente.",
  neutral: "",
  warning: "Advertencia.",
};

const SYNC_MESSAGE: Record<Exclude<SyncSealsResult, "nothing">, { tone: TimestampTone; text: string }> = {
  written: { tone: "success", text: "Sellos copiados a la carpeta del caso." },
  outdated_agent: {
    tone: "neutral",
    text: "Para copiar los sellos a la carpeta del caso, actualizá Tatana. Mientras tanto, descargalos desde acá.",
  },
  error: { tone: "warning", text: "No se pudieron copiar los sellos; reintentá o descargalos desde acá." },
};

interface Props {
  /** Solo hacen falta el id y el estado (para distinguir borrador de generado). */
  cas: Pick<Case, "id" | "status">;
  /** Tatana de esta PC es la PC del ZIP: habilita la copia a `sellos/`. */
  sameHost: boolean;
  /** Estado del ZIP en esta PC: la copia solo va con el ZIP en su carpeta final. */
  zipState?: "final" | "pending" | "unknown";
  /**
   * Dentro de otra sección con borde (el "Paquete del informe" del detalle):
   * sin caja propia, separado por una línea y con `h4`.
   */
  embedded?: boolean;
  className?: string;
}

/**
 * Bloque "Sello de tiempo" (sellado-tiempo §9.4) del resultado y del detalle
 * del caso: ZIP, informe y evidencia, descarga de los `.tsr` y copia a la
 * carpeta del caso. Si el backend no conoce la función (404) o no responde, no
 * renderiza nada; con el sellado apagado y sin sellos, tampoco.
 */
export function TimestampPanel({ cas, sameHost, zipState, embedded = false, className }: Props) {
  // Re-render al cambiar el token: la URL de descarga lleva ?token= (sesion-larga §8.11).
  useTokenVersion();
  const caseId = cas.id;
  const generated = cas.status === "completed";
  const [data, setData] = useState<CaseTimestampsResponse | null>(null);
  const [sync, setSync] = useState<SyncSealsResult | "running" | null>(null);
  const syncedGranted = useRef(-1);
  const titleId = useId();

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const res = await api.getCaseTimestamps(caseId, signal);
      if (!signal?.aborted) setData(res);
    } catch (e) {
      if (signal?.aborted || (e instanceof DOMException && e.name === "AbortError")) return;
      // 404 = backend anterior a la función: el bloque no existe. Un error de red en
      // un refresco deja lo último que se mostró; en la carga inicial, nada.
      if (e instanceof ApiError && e.status === 404) setData(null);
    }
  }, [caseId]);

  // Carga al montar (y al cambiar de caso).
  useEffect(() => {
    setData(null);
    syncedGranted.current = -1;
    const ctrl = new AbortController();
    void load(ctrl.signal);
    return () => ctrl.abort();
  }, [load]);

  // Polling mientras haya pendientes que la cola reintenta; se corta solo cuando no quedan.
  const pending = data ? hasPending(data) : false;
  useEffect(() => {
    if (!pending) return;
    const ctrl = new AbortController();
    const t = window.setInterval(() => {
      if (document.visibilityState === "visible") void load(ctrl.signal);
    }, POLL_MS);
    return () => { window.clearInterval(t); ctrl.abort(); };
  }, [pending, load]);

  const granted = data ? countGranted(data) : 0;
  const canCopy = sameHost && zipState === "final" && granted > 0 && data?.evidence_storage === "agent";

  const runSync = useCallback(async () => {
    setSync("running");
    const r = await syncCaseSeals({ id: caseId });
    setSync(r);
  }, [caseId]);

  // Auto-sincronización: una vez al montar y otra cada vez que llega un sello nuevo
  // (idempotente en Tatana; deja en la carpeta los sellos que llegaron tarde).
  useEffect(() => {
    if (!canCopy || granted <= syncedGranted.current) return;
    syncedGranted.current = granted;
    void runSync();
  }, [canCopy, granted, runSync]);

  if (!data) return null;
  const mode = timestampPanelMode(data, generated);
  if (mode.kind === "hidden") return null;

  const ctx = (target: TimestampLineContext["target"]): TimestampLineContext => ({
    target, enabled: data.enabled, caseMode: data.case_mode, storage: data.evidence_storage, generated,
  });
  const zipLine = mode.kind === "rows" ? timestampLine(data.zip, ctx("zip")) : null;
  const reportLine = mode.kind === "rows" ? timestampLine(data.report, ctx("report")) : null;
  const syncMsg = sync && sync !== "running" && sync !== "nothing" ? SYNC_MESSAGE[sync] : null;
  const Heading = embedded ? "h4" : "h3";

  return (
    <section
      aria-labelledby={titleId}
      className={cn(
        "space-y-3 text-left",
        embedded
          ? "border-t border-fx-border pt-3.5"
          : "rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 sm:p-5",
        className,
      )}
    >
      <Heading id={titleId} className="m-0 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
        <ClockCheck className="h-3.5 w-3.5" aria-hidden="true" /> Sello de tiempo
      </Heading>

      {mode.kind === "summary" ? (
        <StatusText line={mode.line} />
      ) : (
        <dl className="m-0 space-y-3">
          {zipLine && <Row icon={Archive} term="ZIP de evidencia"><StatusText line={zipLine} /></Row>}
          {reportLine && <Row icon={FileText} term="Informe (DOCX)"><StatusText line={reportLine} /></Row>}
          {data.evidence_total > 0 && (
            <Row icon={Paperclip} term="Evidencia">
              <EvidenceRows data={data} lineCtx={ctx("evidence")} />
            </Row>
          )}
        </dl>
      )}

      {(granted > 0 || canCopy) && (
        <div className="flex flex-col gap-2 pt-1 sm:flex-row sm:flex-wrap">
          {granted > 0 && (
            <a
              href={api.timestampBundleURL(caseId)}
              download
              className={cn(FX_BUTTON_SECONDARY, "min-h-11 w-full sm:w-auto")}
            >
              <Download className="h-4 w-4" aria-hidden="true" /> Descargar sellos (.tsr)
            </a>
          )}
          {canCopy && (
            <Button
              type="button"
              severity="secondary"
              outlined
              icon={<FolderInput className="h-4 w-4" aria-hidden="true" />}
              label={sync === "running" ? "Copiando sellos…" : "Copiar sellos a la carpeta del caso"}
              loading={sync === "running"}
              disabled={sync === "running"}
              onClick={() => { void runSync(); }}
              className="min-h-11 w-full sm:w-auto"
            />
          )}
        </div>
      )}

      <p role="status" aria-live="polite" className={cn("m-0 text-xs", !syncMsg && "sr-only")}>
        {syncMsg && (
          <span className={cn("inline-flex items-start gap-1.5", syncMsg.tone === "warning" ? "text-fx-warning" : "text-fx-text-2")}>
            {syncMsg.tone === "success"
              ? <CheckCircle2 className="mt-px h-3.5 w-3.5 shrink-0 text-fx-success" aria-hidden="true" />
              : syncMsg.tone === "warning"
                ? <ClockAlert className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                : <Info className="mt-px h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />}
            <span className="text-pretty">{syncMsg.text}</span>
          </span>
        )}
      </p>
    </section>
  );
}

function countGranted(d: CaseTimestampsResponse): number {
  if (!hasGranted(d)) return 0;
  return (d.zip?.status === "granted" ? 1 : 0)
    + (d.report?.status === "granted" ? 1 : 0)
    + d.evidence.filter(r => r.timestamp?.status === "granted").length;
}

function Row({ icon: Icon, term, children }: { icon: LucideIcon; term: string; children: React.ReactNode }) {
  return (
    <div className="flex items-start gap-3">
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md bg-fx-surface-3 text-fx-text-2">
        <Icon className="h-4 w-4" aria-hidden="true" />
      </div>
      <div className="min-w-0 flex-1">
        <dt className="text-fx-label uppercase text-fx-text-2">{term}</dt>
        <dd className="m-0 mt-1">{children}</dd>
      </div>
    </div>
  );
}

function StatusText({ line, compact = false }: { line: TimestampLine; compact?: boolean }) {
  const Icon = TONE_ICON[line.tone];
  return (
    <div className="min-w-0">
      <p className={cn("m-0 flex items-start gap-1.5", compact ? "text-xs" : "text-fx-body-sm", line.tone === "neutral" ? "text-fx-text-2" : "text-fx-text")}>
        <Icon className={cn("mt-0.5 h-3.5 w-3.5 shrink-0", TONE_CLASS[line.tone])} aria-hidden="true" />
        <span className="min-w-0 break-words">
          {TONE_SR[line.tone] && <span className="sr-only">{TONE_SR[line.tone]} </span>}
          {line.text}
        </span>
      </p>
      {line.detail && (
        <p className={cn("m-0 mt-0.5 break-words pl-5 text-xs", line.tone === "warning" ? "text-fx-text-2" : "text-fx-text-3")}>
          {line.detail}
        </p>
      )}
    </div>
  );
}

/** Resumen "N de M archivos sellados" + la lista por archivo, desplegable con teclado. */
function EvidenceRows({ data, lineCtx }: { data: CaseTimestampsResponse; lineCtx: TimestampLineContext }) {
  const [open, setOpen] = useState(false);
  const listId = useId();
  const lines = data.evidence.map(r => ({ row: r, line: timestampLine(r.timestamp, lineCtx) }));
  const tone: TimestampTone = lines.some(l => l.line?.tone === "warning")
    ? "warning"
    : lines.some(l => l.line?.tone === "pending")
      ? "pending"
      : data.evidence_sealed === data.evidence_total ? "success" : "neutral";

  return (
    <div className="min-w-0">
      <StatusText line={{ tone, text: evidenceSummary(data) }} />
      <button
        type="button"
        aria-expanded={open}
        aria-controls={listId}
        onClick={() => setOpen(o => !o)}
        className={cn(
          "mt-1.5 inline-flex min-h-8 cursor-pointer items-center gap-1 rounded-fx-sm border-0 bg-transparent px-1 -mx-1",
          "text-xs font-semibold text-fx-accent-text hover:underline",
          FOCUS_RING,
        )}
      >
        <ChevronDown
          className={cn("h-3.5 w-3.5 motion-safe:transition-transform motion-safe:duration-fx-fast", open && "rotate-180")}
          aria-hidden="true"
        />
        {open ? "Ocultar el detalle por archivo" : "Ver el detalle por archivo"}
      </button>
      <ul id={listId} hidden={!open} className="m-0 mt-2 list-none space-y-2 p-0">
        {lines.map(({ row, line }) => (
          <li key={`${row.filename}:${row.sha256}`} className="rounded-fx-md border border-fx-border bg-fx-surface-1 px-2.5 py-2">
            <p translate="no" className="m-0 break-all font-mono text-xs font-semibold text-fx-text">{row.filename}</p>
            {line && <div className="mt-1"><StatusText line={line} compact /></div>}
          </li>
        ))}
      </ul>
    </div>
  );
}
