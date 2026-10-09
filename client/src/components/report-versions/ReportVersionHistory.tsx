"use client";

/**
 * Panel de historial de versiones de una sección del informe (versionado-informe,
 * HU6). Diálogo modal (PrimeReact `Dialog`: role=dialog, aria-modal, foco
 * atrapado, Esc, máscara) con tres vistas:
 *  - Lista de versiones (desc., "Actual" marcada) con Ver / Comparar / Restaurar.
 *  - Ver: la versión en solo lectura con el mismo render del editor (D8).
 *  - Comparar: diff por palabra sobre el texto visible (D7), con `diff` cargado
 *    dinámicamente.
 *
 * Se carga con `next/dynamic` desde los dos puntos de entrada (ReportStep y el
 * detalle del caso), así no entra al bundle del dashboard si no se abre.
 */

import { useCallback, useEffect, useId, useMemo, useState, type ComponentType } from "react";
import dynamic from "next/dynamic";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import {
  ArrowLeft, Eye, GitCompare, Loader2, RotateCcw,
} from "lucide-react";
import type { ReportTextVersion, ReportTextVersionTexts, ReportVersionTrigger } from "@/lib/api";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate, formatTime } from "@/lib/format";
import { markdownToVisibleText } from "@/lib/report-markdown";
import { MAX_LEN_TEXT } from "@/lib/pericial";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";
import { FxBanner } from "@/components/feedback/FxBanner";
import type { RichTextEditorProps } from "@/components/editor/RichTextEditor";
import { VersionDiff } from "./VersionDiff";

/** Clave de una de las ocho secciones del informe. */
type SectionKey = keyof Omit<ReportTextVersionTexts, "formato">;

/** Render de una versión en solo lectura: reusa el editor (D8). En un chunk aparte. */
const RichTextEditor = dynamic(() => import("@/components/editor/RichTextEditor"), {
  ssr: false,
  loading: () => (
    <div aria-hidden="true" className="h-24 rounded-fx-md border border-fx-border bg-fx-surface-2 motion-safe:animate-pulse" />
  ),
});

interface Props {
  open: boolean;
  onClose: () => void;
  caseId: string;
  /** Sección cuyo historial se mira. */
  sectionKey: SectionKey;
  sectionLabel: string;
  /** Markdown actual de la sección en el editor (para "Actual" y el diff). */
  currentText: string;
  /**
   * Solo lectura: en casos `Completed` no se puede restaurar (D6). El resto de
   * las vistas (Ver/Comparar) siguen disponibles.
   */
  readOnly?: boolean;
  /**
   * Restaura el texto de una versión en el editor (solo si no es `readOnly`).
   * El guardado con `trigger:"restore"` lo dispara el llamador (ReportStep).
   */
  onRestore?: (versionId: string, text: string) => void;
}

type LoadState =
  | { kind: "loading" }
  | { kind: "error" }
  | { kind: "ready"; versions: ReportTextVersion[] };

type View =
  | { mode: "list" }
  | { mode: "view"; version: ReportTextVersion }
  | { mode: "compare"; from: ReportTextVersion | "current"; to: ReportTextVersion | "current" };

const TRIGGER_LABEL: Record<ReportVersionTrigger, string> = {
  save: "Guardado",
  restore: "Restauración",
  generate: "Generación del informe",
};

export default function ReportVersionHistory({
  open, onClose, caseId, sectionKey, sectionLabel, currentText, readOnly = false, onRestore,
}: Props) {
  const uid = useId();
  const titleId = `${uid}-title`;
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [view, setView] = useState<View>({ mode: "list" });
  const [restoreTarget, setRestoreTarget] = useState<ReportTextVersion | null>(null);

  const load = useCallback((signal?: { aborted: boolean }) => {
    setState({ kind: "loading" });
    api.getReportTextVersions(caseId)
      .then(({ versions }) => { if (!signal?.aborted) setState({ kind: "ready", versions }); })
      .catch(() => { if (!signal?.aborted) setState({ kind: "error" }); });
  }, [caseId]);

  // Carga al abrir; al cerrar vuelve a la lista para la próxima apertura.
  useEffect(() => {
    if (!open) { setView({ mode: "list" }); return; }
    const signal = { aborted: false };
    load(signal);
    return () => { signal.aborted = true; };
  }, [open, load]);

  const sectionTextOf = useCallback(
    (v: ReportTextVersion | "current"): string =>
      v === "current" ? currentText : v.texts[sectionKey] ?? "",
    [currentText, sectionKey],
  );

  function confirmRestore() {
    const v = restoreTarget;
    setRestoreTarget(null);
    if (!v || readOnly || !onRestore) return;
    onRestore(v.id, v.texts[sectionKey] ?? "");
    onClose();
  }

  const header = (
    <span className="flex min-w-0 items-center gap-2">
      {view.mode !== "list" && (
        <button
          type="button"
          onClick={() => setView({ mode: "list" })}
          aria-label="Volver a la lista de versiones"
          className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md text-fx-text-2 hover:bg-fx-surface-2 fx-focus-ring"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
        </button>
      )}
      <span className="min-w-0">
        <span id={titleId} className="block truncate text-fx-h3 text-fx-text">Historial · {sectionLabel}</span>
        <span className="block text-xs font-normal text-fx-text-3">
          {view.mode === "list" ? "Versiones guardadas de esta sección"
            : view.mode === "view" ? "Versión en solo lectura"
            : "Comparación por palabra"}
        </span>
      </span>
    </span>
  );

  return (
    <>
      <Dialog
        visible={open}
        onHide={onClose}
        modal
        closable
        dismissableMask
        draggable={false}
        resizable={false}
        header={header}
        pt={{ root: { "aria-labelledby": titleId, className: "w-[min(44rem,100%)] max-h-[85dvh]" } }}
      >
        {state.kind === "loading" && (
          <div role="status" className="flex items-center justify-center gap-2 py-10 text-fx-body-sm text-fx-text-2">
            <Loader2 className="h-4 w-4 animate-spin text-fx-text-3" aria-hidden="true" /> Cargando el historial…
          </div>
        )}

        {state.kind === "error" && (
          <FxBanner tone="error">
            <p className="m-0">No se pudo cargar el historial de versiones.</p>
            <Button type="button" severity="secondary" size="small" label="Reintentar" onClick={() => load()} className="mt-2" />
          </FxBanner>
        )}

        {state.kind === "ready" && view.mode === "list" && (
          <VersionList
            versions={state.versions}
            sectionKey={sectionKey}
            currentText={currentText}
            readOnly={readOnly}
            onView={v => setView({ mode: "view", version: v })}
            onCompare={v => setView({ mode: "compare", from: v, to: "current" })}
            onRestore={v => setRestoreTarget(v)}
          />
        )}

        {state.kind === "ready" && view.mode === "view" && (
          <VersionView
            version={view.version}
            sectionKey={sectionKey}
            sectionLabel={sectionLabel}
            editor={RichTextEditor}
          />
        )}

        {state.kind === "ready" && view.mode === "compare" && (
          <VersionCompare
            fromLabel={view.from === "current" ? "Actual" : versionShortLabel(view.from)}
            toLabel={view.to === "current" ? "Actual" : versionShortLabel(view.to)}
            fromText={markdownToVisibleText(sectionTextOf(view.from))}
            toText={markdownToVisibleText(sectionTextOf(view.to))}
          />
        )}
      </Dialog>

      <ConfirmDialog
        open={restoreTarget !== null}
        onOpenChange={o => { if (!o) setRestoreTarget(null); }}
        onConfirm={confirmRestore}
        title="¿Restaurar esta versión?"
        description={
          restoreTarget
            ? `Se reemplaza el texto actual de "${sectionLabel}" por el de la versión del ${formatDate(restoreTarget.created_at)}. Queda registrada como una versión nueva; no se pierden las anteriores.`
            : undefined
        }
        confirmLabel="Restaurar"
        cancelLabel="Cancelar"
        tone="neutral"
      />
    </>
  );
}

function versionShortLabel(v: ReportTextVersion): string {
  return `${formatDate(v.created_at)}, ${formatTime(v.created_at)}`;
}

/* ── Vista de lista ── */

function VersionList({
  versions, sectionKey, currentText, readOnly, onView, onCompare, onRestore,
}: {
  versions: ReportTextVersion[];
  sectionKey: SectionKey;
  currentText: string;
  readOnly: boolean;
  onView: (v: ReportTextVersion) => void;
  onCompare: (v: ReportTextVersion) => void;
  onRestore: (v: ReportTextVersion) => void;
}) {
  // La más reciente coincide con el editor si su texto es igual al actual.
  const latestIsCurrent = useMemo(
    () => versions.length > 0 && (versions[0].texts[sectionKey] ?? "") === currentText,
    [versions, sectionKey, currentText],
  );

  if (versions.length === 0) {
    return (
      <p className="py-8 text-center text-fx-body-sm text-fx-text-2">
        Todavía no hay versiones guardadas de esta sección. Se crea una cada vez que se
        consolida un cambio del informe.
      </p>
    );
  }

  return (
    <ol role="list" className="divide-y divide-fx-border">
      {versions.map((v, i) => {
        const isLatest = i === 0;
        const marked = isLatest && latestIsCurrent;
        return (
          <li key={v.id} className="flex flex-wrap items-center gap-x-4 gap-y-2 py-3">
            <div className="min-w-0 flex-1">
              <p className="m-0 flex flex-wrap items-center gap-x-2 gap-y-1 text-fx-body-sm text-fx-text">
                <span className="font-medium">{formatDate(v.created_at)}, {formatTime(v.created_at)}</span>
                {marked && (
                  <span className="rounded-fx-sm border border-fx-success bg-fx-success-soft px-1.5 py-px text-[11px] font-medium text-fx-success">
                    Actual
                  </span>
                )}
                <span className="rounded-fx-sm border border-fx-border bg-fx-surface-2 px-1.5 py-px text-[11px] font-medium text-fx-text-2">
                  {TRIGGER_LABEL[v.trigger]}
                </span>
              </p>
              <p className="m-0 mt-0.5 text-xs text-fx-text-3" title={v.author_dni ? `DNI ${v.author_dni}` : undefined}>
                {v.author_name || "—"}
              </p>
            </div>
            <div className="flex shrink-0 flex-wrap items-center gap-1">
              <Button
                type="button" text severity="secondary" size="small"
                icon={<Eye className="h-3.5 w-3.5" aria-hidden="true" />}
                label="Ver" onClick={() => onView(v)}
              />
              <Button
                type="button" text severity="secondary" size="small"
                icon={<GitCompare className="h-3.5 w-3.5" aria-hidden="true" />}
                label="Comparar" onClick={() => onCompare(v)}
              />
              {!readOnly && (
                <Button
                  type="button" text severity="secondary" size="small"
                  icon={<RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
                  label="Restaurar" onClick={() => onRestore(v)}
                />
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

/* ── Vista "Ver" ── */

function VersionView({
  version, sectionKey, sectionLabel, editor: Editor,
}: {
  version: ReportTextVersion;
  sectionKey: SectionKey;
  sectionLabel: string;
  editor: ComponentType<RichTextEditorProps>;
}) {
  const labelId = `version-view-${version.id}`;
  return (
    <div className="space-y-2">
      <p id={labelId} className="m-0 text-xs text-fx-text-3">
        {sectionLabel} · versión del {formatDate(version.created_at)}, {formatTime(version.created_at)}
      </p>
      <Editor
        id={`version-view-editor-${version.id}`}
        labelId={labelId}
        label={sectionLabel}
        value={version.texts[sectionKey] ?? ""}
        onChange={() => {}}
        maxLength={MAX_LEN_TEXT}
        readOnly
      />
    </div>
  );
}

/* ── Vista "Comparar" ── */

function VersionCompare({
  fromLabel, toLabel, fromText, toText,
}: {
  fromLabel: string;
  toLabel: string;
  fromText: string;
  toText: string;
}) {
  return (
    <div className="space-y-3">
      <p className="m-0 text-xs text-fx-text-3">
        De <span className="font-medium text-fx-text-2">{fromLabel}</span> a{" "}
        <span className="font-medium text-fx-text-2">{toLabel}</span>. Lo
        <span className="mx-1 inline-flex items-center gap-1 align-middle">
          <span className="rounded-fx-sm bg-fx-success-soft px-1 text-fx-success">+ agregado</span>
        </span>
        y lo
        <span className="mx-1 inline-flex items-center gap-1 align-middle">
          <span className="rounded-fx-sm bg-fx-danger-soft px-1 text-fx-danger line-through">− quitado</span>
        </span>
        se marcan con color y símbolo.
      </p>
      <VersionDiff before={fromText} after={toText} />
    </div>
  );
}
