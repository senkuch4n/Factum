"use client";

import { Button } from "primereact/button";
import { Archive, FileText, Key, Hash, RotateCcw, ShieldCheck, CheckCircle2, Lock } from "lucide-react";
import { api } from "@/lib/api";
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
}

/** Paso 6: cierre sobrio con los datos de la entrega y las descargas. */
export function ResultStep({ caseNumber, zipFile, pdfFile, password, encrypted, hash, reportHash, caseId, onNewCase }: Props) {
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

        {encrypted && (
          <p
            id="result-zip-compat"
            className="m-0 flex items-start gap-2 text-left text-xs leading-relaxed text-fx-text-3 sm:col-span-2"
          >
            <Lock className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            <span>
              Cifrado AES-256. Se abre con 7-Zip o WinRAR (Windows) y con Keka o The Unarchiver (macOS). El Explorador de Windows y la Utilidad de Archivo de macOS no lo abren.
            </span>
          </p>
        )}
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
