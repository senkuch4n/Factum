"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import {
  Archive, FileText, Key, Hash, RotateCcw, ShieldCheck, CheckCircle2, HardDrive, Server, Download, CloudAlert,
} from "lucide-react";
import { api, type EvidenceStorage, type ZipLocation } from "@/lib/api";
import { agent } from "@/lib/agent";
import { useAgentIdentity } from "@/hooks/useAgentIdentity";
import { FxBanner } from "@/components/feedback/FxBanner";
import { ZipLocalActions } from "@/components/ZipLocalActions";
import { ZipCompatNotice } from "@/components/ZipCompatNotice";
import { cn } from "@/lib/utils";
import { CopyButton } from "@/components/feedback/CopyButton";
import { FX_BUTTON_PRIMARY, FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";

const FADE_IN = "motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]";
const TILE = "flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md";

/** Fila de la lista de datos: tile con ícono + término + valor. */
function DataRow({ tile, icon, term, children }: {
  tile: string; icon: React.ReactNode; term: string; children: React.ReactNode;
}) {
  return (
    <div className="flex items-start gap-3">
      <div className={cn(TILE, tile)}>{icon}</div>
      <div className="min-w-0">
        <dt className="text-fx-label uppercase text-fx-text-2">{term}</dt>
        <dd className="m-0 mt-1">{children}</dd>
      </div>
    </div>
  );
}

interface Props {
  caseNumber: string;
  zipFile: string;
  pdfFile: string;
  /** Contraseña del ZIP; `null` si el ZIP se generó sin cifrar. Vive solo en memoria. */
  password: string | null;
  /** `true` solo si el ZIP salió cifrado (`case.zip_encrypted === true`). */
  encrypted: boolean;
  /** SHA-256 del ZIP de evidencia (el que figura en el informe). */
  hash: string;
  /** SHA-256 del DOCX generado. */
  reportHash: string;
  caseId: string;
  backendURL: string;
  onNewCase: () => void;
  // ── Flujo agent (zip-local-informe-servidor §7.7) ──
  /** `server` (o sin dato) = igual que antes de la HU, con el ZIP del servidor. */
  evidenceStorage?: EvidenceStorage | null;
  zipLocation?: ZipLocation | null;
  /** `nro_referencia` (Tatana recalcula la carpeta `<causa>_<id8>` con esto). */
  caseRef?: string;
  /** `pending`: el commit en Tatana falló; se reintenta al montar. */
  zipState?: "final" | "pending" | "unknown";
  /** Tatana de esta PC es la PC del ZIP. */
  sameHost?: boolean;
  /** Archivos sueltos a borrar al completar el commit pendiente (D12). */
  pendingDeleteFiles?: string[];
}

/** Paso 6: cierre sobrio con los datos de la entrega y las descargas. */
export function ResultStep(props: Props) {
  if (props.evidenceStorage === "agent") return <AgentResult {...props} />;
  return <ServerResult {...props} />;
}

/** Flujo `server` (casos viejos): igual que antes de zip-local-informe-servidor. */
function ServerResult({ caseNumber, zipFile, pdfFile, password, encrypted, hash, reportHash, caseId, onNewCase }: Props) {
  const downloadURL = (file: string) => api.downloadURL(caseId, file);
  // Solo se muestra si el ZIP salió cifrado y vino la contraseña.
  const zipPassword = encrypted && password ? password : null;
  const checks = [
    "Hash SHA-256 por archivo",
    "Hash del ZIP verificable",
    ...(encrypted ? ["ZIP cifrado con AES-256"] : []),
    "Informe pericial generado",
  ];

  return (
    <div className="space-y-6 text-center">
      <div className="mx-auto flex h-20 w-20 items-center justify-center rounded-fx-xl border border-fx-success bg-fx-success-soft motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]">
        <ShieldCheck className="h-10 w-10 text-fx-success" strokeWidth={1.5} aria-hidden="true" />
      </div>

      <div className={FADE_IN}>
        <h2 className="m-0 text-fx-h1 text-fx-text text-balance">¡Informe pericial generado!</h2>
        <p className="m-0 mt-2 text-fx-body-sm text-fx-text-2">
          Descargá el ZIP de evidencia y el informe del caso <span className="font-semibold text-fx-text">{caseNumber}</span>.
        </p>
      </div>

      <section
        aria-labelledby="result-data-title"
        className={cn("space-y-4 rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 text-left sm:p-5", FADE_IN)}
      >
        <h3 id="result-data-title" className="m-0 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
          <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" /> Datos que te van a ser útiles
        </h3>

        <dl className="m-0 space-y-4">
          {zipPassword && (
            <DataRow tile="bg-fx-warning-soft text-fx-warning" icon={<Key className="h-4 w-4" aria-hidden="true" />} term="Contraseña del ZIP">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                <span translate="no" className="select-all break-all font-mono text-fx-body font-bold text-fx-text">
                  {zipPassword}
                </span>
                <CopyButton text={zipPassword} label="Copiar contraseña del ZIP" />
              </div>
              <p className="m-0 mt-1.5 text-xs text-fx-text-3">
                Sin esta contraseña la evidencia no se puede abrir. Entregala por un canal distinto al del ZIP (no en el mismo correo ni en el mismo pendrive).
              </p>
            </DataRow>
          )}

          <DataRow tile="bg-fx-surface-3 text-fx-text-2" icon={<Hash className="h-4 w-4" aria-hidden="true" />} term="Hash SHA-256 del ZIP de evidencia">
            <p translate="no" className="m-0 select-all break-all font-mono text-xs text-fx-text-2">{hash}</p>
            <p className="m-0 mt-1 text-xs text-fx-text-3">Es el que figura en el informe.</p>
          </DataRow>

          <DataRow tile="bg-fx-surface-3 text-fx-text-2" icon={<FileText className="h-4 w-4" aria-hidden="true" />} term="Hash SHA-256 del informe (DOCX)">
            <p translate="no" className="m-0 select-all break-all font-mono text-xs text-fx-text-2">{reportHash || "—"}</p>
            <p className="m-0 mt-1 text-xs text-fx-text-3">
              El informe no puede contener su propio hash: guardalo junto con la entrega.
            </p>
          </DataRow>
        </dl>

        <ul className="m-0 grid list-none grid-cols-1 gap-2 p-0 pt-1 sm:grid-cols-2">
          {checks.map(t => (
            <li key={t} className="flex items-center gap-2 text-xs text-fx-text-2">
              <CheckCircle2 className="h-3.5 w-3.5 shrink-0 text-fx-success" aria-hidden="true" /> {t}
            </li>
          ))}
        </ul>
      </section>

      {/* Entre la contraseña y la descarga: donde el oficial va a abrir el ZIP. */}
      {encrypted && <ZipCompatNotice id="result-zip-compat" className={FADE_IN} />}

      <div className={cn("grid grid-cols-1 gap-3 sm:grid-cols-2", FADE_IN)}>
        <a
          href={downloadURL(zipFile)}
          aria-describedby={encrypted ? "result-zip-compat" : undefined}
          className={cn(FX_BUTTON_PRIMARY, "h-auto min-h-20 flex-col gap-1.5 whitespace-normal py-4")}
        >
          <Archive className="h-6 w-6" aria-hidden="true" />
          Descargar ZIP
          <span className="text-xs font-normal opacity-90">Evidencia</span>
        </a>

        <a
          href={downloadURL(pdfFile)}
          className={cn(FX_BUTTON_SECONDARY, "h-auto min-h-20 flex-col gap-1.5 whitespace-normal py-4")}
        >
          <FileText className="h-6 w-6" aria-hidden="true" />
          Informe Word
          <span className="text-xs font-normal opacity-90">Informe pericial (.docx)</span>
        </a>

      </div>

      <Button
        type="button"
        text
        severity="secondary"
        icon={<RotateCcw className="h-4 w-4" aria-hidden="true" />}
        label="Iniciar nueva inspección"
        onClick={onNewCase}
        className={cn("w-full min-h-11", FADE_IN)}
      />
    </div>
  );
}

const BLOCK = "space-y-3 rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 text-left sm:p-5";
const BLOCK_TITLE = "m-0 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2";

/**
 * Flujo `agent`: dos bloques separados (SDD §7.7). **Informe** se descarga del
 * servidor; **Evidencia ZIP** quedó en esta PC (o en la PC indicada).
 */
function AgentResult({
  caseNumber, zipFile, pdfFile, password, encrypted, hash, reportHash, caseId, onNewCase,
  zipLocation, caseRef = caseNumber, zipState = "final", sameHost = false, pendingDeleteFiles = [],
}: Props) {
  const identity = useAgentIdentity();
  const zipPassword = encrypted && password ? password : null;
  const [state, setState] = useState(zipState);
  const triedCommit = useRef(false);

  // Auto-commit del ZIP pendiente (D-T2): el hash ya está registrado, mover es un rename.
  useEffect(() => {
    if (state !== "pending" || !sameHost || triedCommit.current) return;
    triedCommit.current = true;
    agent.commitZip(caseId, { case_ref: caseRef, zip_filename: zipFile, zip_hash: hash, delete_files: pendingDeleteFiles })
      .then(() => setState("final"))
      .catch(() => { /* queda el aviso; se completa al abrir el caso en esta PC */ });
  }, [state, sameHost, caseId, caseRef, zipFile, hash, pendingDeleteFiles]);

  const hostLabel = zipLocation?.hostname ?? "otra PC";
  const synced = identity.status === "online" && identity.info?.evidence_directory_synced === true;
  const evidenceDir = identity.info?.evidence_directory ?? zipLocation?.directory ?? "";

  return (
    <div className="space-y-6 text-center">
      <div className="mx-auto flex h-20 w-20 items-center justify-center rounded-fx-xl border border-fx-success bg-fx-success-soft motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]">
        <ShieldCheck className="h-10 w-10 text-fx-success" strokeWidth={1.5} aria-hidden="true" />
      </div>

      <div className={FADE_IN}>
        <h2 className="m-0 text-fx-h1 text-fx-text text-balance">¡Informe pericial generado!</h2>
        <p className="m-0 mt-2 text-fx-body-sm text-fx-text-2 text-pretty">
          El informe del caso <span className="font-semibold text-fx-text">{caseNumber}</span> quedó en el servidor
          y el ZIP de evidencia, {sameHost ? "en esta PC" : <>en la PC <span translate="no" className="font-semibold text-fx-text">{hostLabel}</span></>}.
        </p>
      </div>

      {synced && (
        <FxBanner tone="warn" className="text-left" icon={<CloudAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
          {syncedFolderMessage(evidenceDir)}
        </FxBanner>
      )}

      {state === "pending" && (
        <FxBanner tone="warn" className="text-left">
          El ZIP quedó verificado en esta PC pero no se pudo mover a su carpeta final. Abrí este caso en esta PC con Tatana para completarlo.
        </FxBanner>
      )}

      <div className={cn("grid grid-cols-1 gap-4", FADE_IN)}>
        {/* ── Informe: servidor ── */}
        <section aria-labelledby="result-report-title" className={BLOCK}>
          <h3 id="result-report-title" className={BLOCK_TITLE}>
            <Server className="h-3.5 w-3.5" aria-hidden="true" /> Informe · en el servidor
          </h3>
          <dl className="m-0 space-y-4">
            <DataRow tile="bg-fx-surface-3 text-fx-text-2" icon={<FileText className="h-4 w-4" aria-hidden="true" />} term="Hash SHA-256 del informe (DOCX)">
              <p translate="no" className="m-0 select-all break-all font-mono text-xs text-fx-text-2">{reportHash || "—"}</p>
              <p className="m-0 mt-1 text-xs text-fx-text-3">
                El informe no puede contener su propio hash: guardalo junto con la entrega.
              </p>
            </DataRow>
          </dl>
          <a href={api.downloadURL(caseId, pdfFile)} className={cn(FX_BUTTON_SECONDARY, "min-h-11 w-full sm:w-auto")}>
            <Download className="h-4 w-4" aria-hidden="true" /> Descargar informe Word (.docx)
          </a>
        </section>

        {/* ── Evidencia ZIP: esta PC ── */}
        <section aria-labelledby="result-zip-title" className={BLOCK}>
          <h3 id="result-zip-title" className={BLOCK_TITLE}>
            <HardDrive className="h-3.5 w-3.5" aria-hidden="true" />
            {sameHost ? "Evidencia ZIP · en esta PC" : <>Evidencia ZIP · en <span translate="no" className="normal-case">{hostLabel}</span></>}
          </h3>
          <dl className="m-0 space-y-4">
            <DataRow tile="bg-fx-surface-3 text-fx-text-2" icon={<Archive className="h-4 w-4" aria-hidden="true" />} term="Archivo">
              <p translate="no" className="m-0 break-all font-mono text-fx-body-sm font-semibold text-fx-text">{zipFile}</p>
              {zipLocation?.path && (
                <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1.5">
                  <span translate="no" className="min-w-0 select-all break-all font-mono text-xs text-fx-text-2">
                    {!sameHost && <span className="font-sans">Guardado en {hostLabel} · </span>}{zipLocation.path}
                  </span>
                  <CopyButton text={zipLocation.path} label="Copiar la ruta del ZIP" />
                </div>
              )}
            </DataRow>

            {zipPassword && (
              <DataRow tile="bg-fx-warning-soft text-fx-warning" icon={<Key className="h-4 w-4" aria-hidden="true" />} term="Contraseña del ZIP">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                  <span translate="no" className="select-all break-all font-mono text-fx-body font-bold text-fx-text">
                    {zipPassword}
                  </span>
                  <CopyButton text={zipPassword} label="Copiar contraseña del ZIP" />
                </div>
                <p className="m-0 mt-1.5 text-xs text-fx-text-3">
                  Sin esta contraseña la evidencia no se puede abrir. Entregala por un canal distinto al del ZIP (no en el mismo correo ni en el mismo pendrive).
                </p>
              </DataRow>
            )}

            <DataRow tile="bg-fx-surface-3 text-fx-text-2" icon={<Hash className="h-4 w-4" aria-hidden="true" />} term="Hash SHA-256 del ZIP de evidencia">
              <p translate="no" className="m-0 select-all break-all font-mono text-xs text-fx-text-2">{hash}</p>
              <p className="m-0 mt-1 text-xs text-fx-text-3">Es el que figura en el informe.</p>
            </DataRow>
          </dl>

          {/* Justo antes de "Mostrar en carpeta" / "Guardar una copia…". */}
          {encrypted && <ZipCompatNotice />}

          {sameHost && state === "final" && (
            <ZipLocalActions caseId={caseId} caseRef={caseRef} zipFilename={zipFile} />
          )}
        </section>
      </div>

      <Button
        type="button"
        text
        severity="secondary"
        icon={<RotateCcw className="h-4 w-4" aria-hidden="true" />}
        label="Iniciar nueva inspección"
        onClick={onNewCase}
        className={cn("w-full min-h-11", FADE_IN)}
      />
    </div>
  );
}

/** Aviso de OneDrive/iCloud (DP4): no cambia la carpeta, solo avisa. */
export function syncedFolderMessage(dir: string): string {
  return `La carpeta de evidencia (${dir || "configurada en Tatana"}) está dentro de una carpeta sincronizada con la nube (OneDrive/iCloud): el ZIP se puede subir a ese servicio. Pedí que configuren Agent:EvidenceDirectory fuera de esa carpeta.`;
}
