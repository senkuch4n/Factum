"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import {
  ChevronDown, FileText, Archive, Key, Hash,
  Smartphone, Calendar, Clock, FolderOpen, Shield, Play, Eye, Loader2, AlertCircle, Info,
} from "lucide-react";
import type { Case } from "@/lib/api";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate, formatTime } from "@/lib/format";
import { FX_BUTTON_PRIMARY, FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";
import { StatusBadge } from "./StatusBadge";
import { CopyButton } from "@/components/ui/CopyButton";

const FADE_IN = "motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]";

/**
 * Inspección en la vista lista: disclosure (`aria-expanded` + `aria-controls`)
 * con el detalle, el paquete del informe y las descargas. El padre es un `ul`.
 */
export function CaseCard({ cas, onResume }: { cas: Case; onResume: (c: Case) => void }) {
  const [open, setOpen] = useState(false);
  const dlURL = (filename: string) => api.downloadURL(cas.id, filename);
  const isDone = cas.status === "completed";
  const isDraft = cas.status === "draft";
  // Solo `=== true` es "cifrado" (casos viejos no traen el campo).
  const isEncrypted = cas.zip_encrypted === true;
  const detailId = `case-${cas.id}-detail`;

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
    <li className="fx-card overflow-hidden">
      <button
        type="button"
        className="w-full text-left p-4 flex items-center gap-4 hover:bg-fx-surface-2 transition-colors duration-fx-fast ease-fx fx-focus-ring"
        onClick={() => setOpen(o => !o)}
        aria-expanded={open}
        aria-controls={detailId}
        aria-label={`Causa ${cas.nro_referencia} — ${open ? "ocultar" : "ver"} detalle`}
      >
        <span
          className={cn(
            "w-10 h-10 rounded-fx-md border flex items-center justify-center shrink-0",
            isDone
              ? "bg-fx-success-soft border-fx-success text-fx-success"
              : "bg-fx-surface-2 border-fx-border text-fx-text-3",
          )}
        >
          <FolderOpen className="w-5 h-5" strokeWidth={1.5} aria-hidden="true" />
        </span>

        <span className="flex-1 min-w-0">
          <span className="flex items-center gap-2 flex-wrap">
            <span className="text-fx-body-sm font-semibold text-fx-text">{cas.nro_referencia}</span>
            <StatusBadge status={cas.status} />
          </span>
          <span className="block mt-0.5 text-xs text-fx-text-2 truncate">
            {cas.caratula || cas.nombre_denunciante}
            {cas.dni_denunciante && ` · DNI ${cas.dni_denunciante}`}
          </span>
          <span className="flex items-center gap-x-3 gap-y-1 mt-1 flex-wrap text-xs text-fx-text-3">
            <span className="flex items-center gap-1">
              <Smartphone className="w-3 h-3" aria-hidden="true" /> {cas.device.manufacturer} {cas.device.model}
            </span>
            <span className="flex items-center gap-1">
              <Calendar className="w-3 h-3" aria-hidden="true" /> {formatDate(cas.created_at)}
            </span>
            <span className="flex items-center gap-1">
              <Clock className="w-3 h-3" aria-hidden="true" /> {formatTime(cas.created_at)}
            </span>
          </span>
        </span>

        <ChevronDown
          className={cn(
            "w-4 h-4 shrink-0 text-fx-text-3 transition-transform duration-fx-fast ease-fx motion-reduce:transition-none",
            open && "rotate-180",
          )}
          aria-hidden="true"
        />
      </button>

      {open && (
        <div id={detailId} className={cn("border-t border-fx-border bg-fx-surface-2 px-4 pt-4 pb-5 space-y-4", FADE_IN)}>
          {isDraft && (
            <Button
              icon={<Play className="h-4 w-4" aria-hidden="true" />}
              label="Retomar inspección"
              onClick={() => onResume(cas)}
              className="w-full min-h-11"
            />
          )}

          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            {fields.map(({ label, value, full }) => (
              <div key={label} className={full ? "sm:col-span-2" : undefined}>
                <dt className="text-fx-label uppercase text-fx-text-3">{label}</dt>
                <dd className="mt-1 text-fx-body-sm text-fx-text break-words">{value}</dd>
              </div>
            ))}
          </dl>

          {isDone && (
            <div className="rounded-fx-lg border border-fx-border bg-fx-surface-1 p-4 space-y-3.5">
              <p className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
                <Shield className="w-3.5 h-3.5" aria-hidden="true" /> Paquete del informe
              </p>
              {isEncrypted && <ZipPasswordRow caseId={cas.id} />}
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
              {(cas.zip_filename || cas.pdf_filename) && (
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-1">
                  {cas.zip_filename && (
                    <a href={dlURL(cas.zip_filename)} className={cn(FX_BUTTON_PRIMARY, "min-h-11")}>
                      <Archive className="w-4 h-4" aria-hidden="true" />
                      {isEncrypted ? "ZIP cifrado" : "ZIP de evidencia"}
                    </a>
                  )}
                  {cas.pdf_filename && (
                    <a href={dlURL(cas.pdf_filename)} className={cn(FX_BUTTON_SECONDARY, "min-h-11")}>
                      <FileText className="w-4 h-4" aria-hidden="true" /> Informe Word
                    </a>
                  )}
                </div>
              )}
              {!isEncrypted && cas.zip_filename && (
                <p className="flex items-center gap-1.5 text-xs text-fx-text-3">
                  <Info className="w-3.5 h-3.5 shrink-0" aria-hidden="true" /> Este ZIP se generó sin cifrar
                </p>
              )}
            </div>
          )}
        </div>
      )}
    </li>
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
 * Fila "Contraseña ZIP" del historial: la contraseña ya no viene en el `Case`,
 * se pide bajo demanda a `GET /api/cases/{id}/zip-password`. Vive solo en el
 * estado de este componente (que se desmonta al cerrar el card): nada de
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
