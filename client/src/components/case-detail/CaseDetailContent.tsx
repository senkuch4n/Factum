"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import {
  FileText, Archive, Key, Hash, Shield, Play, Eye, Loader2, AlertCircle, Info, HardDrive,
} from "lucide-react";
import { agent } from "@/lib/agent";
import { ZipLocalActions } from "@/components/ZipLocalActions";
import { ZipCompatNotice } from "@/components/ZipCompatNotice";
import type { Case } from "@/lib/api";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { FX_BUTTON_PRIMARY, FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";
import { CopyButton } from "@/components/feedback/CopyButton";
import { useCaseAgentFlow } from "./useCaseAgentFlow";
import { CaseTimeline } from "./CaseTimeline";
import { CaseVersionHistoryButton } from "./CaseVersionHistoryButton";

/** Id del aviso de compatibilidad (lo referencia el botón "ZIP cifrado" del pie). */
const compatId = (cas: Case) => `zip-compat-${cas.id}`;

/**
 * Cuerpo del detalle de una inspección (antes vivía en el disclosure de
 * `CaseCard`): "Retomar inspección" para borradores, los datos del caso y el
 * paquete del informe (hashes, contraseña bajo demanda y, en el flujo agent,
 * dónde quedó el ZIP y sus acciones locales). Las descargas del servidor
 * (ZIP / Informe Word) van en el pie fijo: `CaseDetailFooter`.
 */
export function CaseDetailContent({ cas, onResume }: { cas: Case; onResume: (c: Case) => void }) {
  const isDone = cas.status === "completed";
  const isDraft = cas.status === "draft";
  // Solo `=== true` es "cifrado" (casos viejos no traen el campo).
  const isEncrypted = cas.zip_encrypted === true;
  const { agentFlow, sameHost } = useCaseAgentFlow(cas);

  const fields: { label: string; value: string; full?: boolean }[] = [
    { label: "Perito",      value: cas.perito?.nombre ?? cas.officer.name },
    { label: "Unidad",      value: cas.officer.sigla },
    { label: "Titular",     value: cas.nombre_denunciante || "—" },
    { label: "Sistema op.", value: cas.device.os_version || `Android ${cas.device.android_version}` },
    ...(cas.caratula ? [{ label: "Carátula", value: cas.caratula, full: true }] : []),
    { label: "IMEI",        value: cas.device.imei || "—", full: true },
    ...(cas.observaciones ? [{ label: "Observaciones", value: cas.observaciones, full: true }] : []),
  ];

  return (
    <div className="space-y-5">
      {isDraft && (
        <Button
          icon={<Play className="h-4 w-4" aria-hidden="true" />}
          label="Retomar inspección"
          onClick={() => onResume(cas)}
          className="w-full min-h-11"
        />
      )}

      <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-4">
        {fields.map(({ label, value, full }) => (
          <div key={label} className={full ? "sm:col-span-2" : undefined}>
            <dt className="text-fx-label uppercase text-fx-text-3">{label}</dt>
            <dd className="mt-1 text-fx-body-sm text-fx-text break-words">{value}</dd>
          </div>
        ))}
      </dl>

      {/* versionado-informe (HU6, D7-B): historial de las secciones del informe,
          accesible desde el detalle; solo lectura en casos completados. */}
      <CaseVersionHistoryButton cas={cas} />

      {/* trazabilidad-caso (HU5, D7-A): cadena de custodia / actividad del caso,
          antes del paquete del informe para que la narrativa preceda a los artefactos. */}
      <CaseTimeline cas={cas} />

      {isDone && (
        <section
          aria-labelledby={`case-${cas.id}-package`}
          className="rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 space-y-3.5"
        >
          <h3 id={`case-${cas.id}-package`} className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
            <Shield className="w-3.5 h-3.5" aria-hidden="true" /> Paquete del informe
          </h3>
          {isEncrypted && <ZipPasswordRow caseId={cas.id} />}
          {isEncrypted && <ZipCompatNotice id={compatId(cas)} />}
          {cas.zip_hash && (
            <HashRow
              icon={<Hash className="w-3.5 h-3.5 shrink-0 mt-0.5 text-fx-text-3" aria-hidden="true" />}
              label={(cas.schema_version ?? 0) >= 1
                ? "Hash SHA-256 del ZIP"
                : "Hash SHA-256 de la evidencia (sin el informe)"}
              value={cas.zip_hash}
            />
          )}
          {(cas.schema_version ?? 0) >= 1 && cas.report_hash && (
            <HashRow
              icon={<FileText className="w-3.5 h-3.5 shrink-0 mt-0.5 text-fx-text-3" aria-hidden="true" />}
              label="Hash SHA-256 del informe"
              value={cas.report_hash}
            />
          )}
          {agentFlow && <AgentZipBlock cas={cas} sameHost={sameHost} isEncrypted={isEncrypted} />}
          {!agentFlow && !isEncrypted && cas.zip_filename && (
            <p className="flex items-center gap-1.5 text-xs text-fx-text-3">
              <Info className="w-3.5 h-3.5 shrink-0" aria-hidden="true" /> Este ZIP se generó sin cifrar
            </p>
          )}
        </section>
      )}
    </div>
  );
}

/**
 * Descargas del servidor que van en el pie fijo. En el flujo agent el ZIP no
 * está en el servidor (zip-local-informe-servidor §7.7): solo el Word.
 */
function serverDownloads(cas: Case) {
  if (cas.status !== "completed") return { zip: null, pdf: null };
  const agentFlow = cas.evidence_storage === "agent";
  return {
    zip: !agentFlow && cas.zip_filename ? cas.zip_filename : null,
    pdf: cas.pdf_filename || null,
  };
}

/** Hay algo para el pie fijo (si no, el modal no lo monta). */
export function hasFooterDownloads(cas: Case) {
  const { zip, pdf } = serverDownloads(cas);
  return !!(zip || pdf);
}

/**
 * Pie fijo del detalle: ZIP (primario) + Informe Word (secundario), siempre a
 * la vista. Si hay uno solo, ocupa todo el ancho.
 */
export function CaseDetailFooter({ cas }: { cas: Case }) {
  const { zip, pdf } = serverDownloads(cas);
  const isEncrypted = cas.zip_encrypted === true;
  const dlURL = (filename: string) => api.downloadURL(cas.id, filename);

  return (
    <div className="flex gap-2">
      {zip && (
        <a
          href={dlURL(zip)}
          aria-describedby={isEncrypted ? compatId(cas) : undefined}
          className={cn(FX_BUTTON_PRIMARY, "min-h-11 flex-1")}
        >
          <Archive className="w-4 h-4" aria-hidden="true" />
          {isEncrypted ? "ZIP cifrado" : "ZIP de evidencia"}
        </a>
      )}
      {pdf && (
        <a href={dlURL(pdf)} className={cn(zip ? FX_BUTTON_SECONDARY : FX_BUTTON_PRIMARY, "min-h-11 flex-1")}>
          <FileText className="w-4 h-4" aria-hidden="true" /> Informe Word
        </a>
      )}
    </div>
  );
}

/**
 * ZIP de un caso `agent` en el historial: dónde quedó, acciones si es esta PC y
 * auto-commit de un ZIP pendiente con el hash registrado (sin borrar sueltos:
 * la lista no está a mano). El informe se baja del servidor desde el pie.
 */
function AgentZipBlock({ cas, sameHost, isEncrypted }: { cas: Case; sameHost: boolean; isEncrypted: boolean }) {
  const loc = cas.zip_location;
  const zipName = cas.zip_filename;
  const [zipState, setZipState] = useState<"checking" | "final" | "none" | "unknown">(sameHost ? "checking" : "unknown");

  useEffect(() => {
    if (!sameHost || !zipName) return;
    let alive = true;
    setZipState("checking");
    (async () => {
      try {
        const st = await agent.zipStatus(cas.id, cas.nro_referencia, zipName);
        if (st.state === "pending" && st.pending_hash && st.pending_hash === cas.zip_hash) {
          await agent.commitZip(cas.id, { case_ref: cas.nro_referencia, zip_filename: zipName, zip_hash: cas.zip_hash, delete_files: [] });
          if (alive) setZipState("final");
        } else if (alive) {
          setZipState(st.state === "final" ? "final" : "none");
        }
      } catch {
        if (alive) setZipState("unknown");
      }
    })();
    return () => { alive = false; };
  }, [sameHost, cas.id, cas.nro_referencia, cas.zip_hash, zipName]);

  return (
    <div className="space-y-3 pt-1">
      {loc && (
        <div className="flex items-start gap-2.5">
          <HardDrive className="w-3.5 h-3.5 shrink-0 mt-0.5 text-fx-text-3" aria-hidden="true" />
          <div className="min-w-0">
            <p className="text-fx-label uppercase text-fx-text-3">ZIP de evidencia</p>
            <p className="mt-1 text-xs text-fx-text-2 break-all">
              Guardado en <span translate="no" className="font-semibold text-fx-text">{loc.hostname}</span>
              {" · "}<span translate="no" className="font-mono select-all">{loc.path}</span>
            </p>
          </div>
        </div>
      )}
      {sameHost && zipName && zipState === "final" && (
        <ZipLocalActions caseId={cas.id} caseRef={cas.nro_referencia} zipFilename={zipName} />
      )}
      {sameHost && zipState === "checking" && (
        <p role="status" className="flex items-center gap-1.5 text-xs text-fx-text-3">
          <Loader2 className="w-3.5 h-3.5 shrink-0 motion-safe:animate-spin" aria-hidden="true" /> Buscando el ZIP en esta PC…
        </p>
      )}
      {sameHost && zipState === "none" && (
        <p className="flex items-center gap-1.5 text-xs text-fx-warning">
          <AlertCircle className="w-3.5 h-3.5 shrink-0" aria-hidden="true" /> El ZIP ya no está en esta carpeta
        </p>
      )}
      {!isEncrypted && zipName && (
        <p className="flex items-center gap-1.5 text-xs text-fx-text-3">
          <Info className="w-3.5 h-3.5 shrink-0" aria-hidden="true" /> Este ZIP se generó sin cifrar
        </p>
      )}
    </div>
  );
}

function HashRow({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="flex items-start gap-2.5">
      {icon}
      <div className="min-w-0">
        <p className="text-fx-label uppercase text-fx-text-3">{label}</p>
        <p translate="no" className="mt-1 font-mono text-xs text-fx-text-2 break-all select-all">{value}</p>
      </div>
    </div>
  );
}

type PasswordState =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error" }
  | { kind: "shown"; password: string };

/**
 * Fila "Contraseña ZIP": la contraseña no viene en el `Case`, se pide bajo
 * demanda a `GET /api/cases/{id}/zip-password`. Vive solo en el estado de
 * este componente (que se desmonta al cerrar el detalle): nada de
 * `localStorage` ni de logs.
 */
function ZipPasswordRow({ caseId }: { caseId: string }) {
  const [state, setState] = useState<PasswordState>({ kind: "idle" });
  const valueRef = useRef<HTMLParagraphElement>(null);
  const active = useRef(true);

  useEffect(() => {
    active.current = true;
    return () => { active.current = false; };
  }, []);

  // El botón desaparece al mostrar la contraseña: el foco pasa al valor para
  // no perderlo (y el lector de pantalla lo lee).
  useEffect(() => {
    if (state.kind === "shown") valueRef.current?.focus();
  }, [state.kind]);

  async function reveal() {
    if (state.kind === "loading") return;
    setState({ kind: "loading" });
    try {
      const { password } = await api.getZipPassword(caseId);
      if (active.current) setState({ kind: "shown", password });
    } catch {
      // Sin loguear el error: no aporta y no hace falta exponer nada.
      if (active.current) setState({ kind: "error" });
    }
  }

  const errorId = `zip-password-error-${caseId}`;
  const loading = state.kind === "loading";

  return (
    <div className="flex items-start gap-2.5">
      <Key className="w-3.5 h-3.5 shrink-0 mt-0.5 text-fx-text-3" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-fx-label uppercase text-fx-text-3">Contraseña ZIP</p>
        {state.kind === "shown" ? (
          <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1.5">
            <p
              translate="no"
              ref={valueRef}
              tabIndex={-1}
              className="font-mono text-fx-body-sm font-bold text-fx-text select-all break-all rounded-fx-sm fx-focus-ring"
            >
              {state.password}
            </p>
            <CopyButton text={state.password} label="Copiar contraseña del ZIP" />
          </div>
        ) : (
          <>
            {/* Botón nativo con `aria-disabled` (no `disabled`) para que el foco
                no se pierda mientras carga; `reveal` ignora los clics en ese estado. */}
            <button
              type="button"
              onClick={reveal}
              aria-disabled={loading}
              aria-busy={loading}
              aria-describedby={state.kind === "error" ? errorId : undefined}
              className={cn(FX_BUTTON_SECONDARY, "mt-1.5 min-h-8 px-3.5 py-1.5 text-xs", loading && "cursor-wait opacity-60")}
            >
              {loading
                ? <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" />
                : <Eye className="w-3.5 h-3.5" aria-hidden="true" />}
              {loading ? "Obteniendo contraseña…" : "Mostrar contraseña"}
            </button>
            {state.kind === "error" && (
              <p id={errorId} role="alert" className="mt-1.5 flex items-center gap-1.5 text-xs text-fx-danger">
                <AlertCircle className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
                No se pudo obtener la contraseña. Probá de nuevo.
              </p>
            )}
          </>
        )}
      </div>
    </div>
  );
}
