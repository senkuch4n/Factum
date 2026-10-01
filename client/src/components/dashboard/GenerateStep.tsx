"use client";

import { useState } from "react";
import { Button } from "primereact/button";
import { Tag } from "primereact/tag";
import {
  ArrowLeft, FileCheck2, ShieldCheck, Shield, User,
  FileText, Video, ImageIcon, Paperclip, Check, Lock, Fingerprint, FolderClosed,
  AlertTriangle, ArrowRight, Archive,
} from "lucide-react";
import type { Case, CapturedFile } from "@/types";
import { agentFileURL } from "@/lib/agent";
import { usePublicConfig } from "@/hooks/usePublicConfig";
import { describeMissing, getMissingRequirements, type MissingRequirement } from "@/lib/pericial";
import { StepHeader } from "@/components/wizard/StepHeader";
import { StepActions } from "@/components/wizard/StepActions";

interface Props {
  currentCase: Case;
  files: CapturedFile[];
  loading: boolean;
  /** Claves `missing` del último `400` del servidor (se suman a las calculadas acá). */
  serverMissing?: string[];
  onBack: () => void;
  onGenerate: () => void;
  /** Lleva al paso del dato que falta y enfoca su campo. */
  onGoToField: (step: number, fieldId: string) => void;
}

const isVideo = (n: string) => /\.(mp4|mkv|mov|avi)$/i.test(n);
const isImage = (n: string) => /\.(png|jpe?g|webp|gif|bmp|heic)$/i.test(n);
const isFuncionario = (n: string) => n.includes("funcionario");
const isDenunciante = (n: string) => n.includes("denunciante");
const isIdentity = (n: string) => isFuncionario(n) || isDenunciante(n);
const isCapture = (n: string) => !isIdentity(n) && (n.includes("screenshot") || n.includes("captura"));

const BLOCK_TITLE = "m-0 text-fx-label uppercase text-fx-text-2";

/* ── Miniatura con fallback a ícono (mismo criterio que el Paso 3). ── */
function Thumb({ name, kind }: { name: string; kind: "image" | "video" | "doc" }) {
  const [errored, setErrored] = useState(false);
  const showImg = kind === "image" && !errored;
  return (
    <div
      className="relative h-14 w-14 shrink-0 overflow-hidden rounded-fx-md border border-fx-border bg-fx-surface-2"
      title={name}
    >
      {showImg ? (
        <img
          src={agentFileURL(name)}
          alt={name}
          className="h-full w-full object-cover"
          onError={() => setErrored(true)}
          ref={el => { if (el?.complete && el.naturalWidth === 0 && !errored) setErrored(true); }}
        />
      ) : (
        <div className="flex h-full w-full items-center justify-center">
          {kind === "video"
            ? <Video className="h-5 w-5 text-fx-text-2" aria-hidden="true" />
            : <FileText className="h-5 w-5 text-fx-text-3" aria-hidden="true" />}
        </div>
      )}
      {kind === "video" && showImg && (
        /* contenido de imagen: velo sobre la miniatura del video */
        <span className="absolute inset-0 flex items-center justify-center bg-black/40 text-white">
          <Video className="h-4 w-4" aria-hidden="true" />
        </span>
      )}
    </div>
  );
}

/* ── Confirmación de una identidad (perito / titular) con su foto. ── */
function IdentityConfirm({
  label, role, icon: Icon, file,
}: { label: string; role: string; icon: React.ElementType; file?: CapturedFile }) {
  const [errored, setErrored] = useState(false);
  const showImg = !!file && !errored;
  return (
    <div className="flex items-center gap-3 rounded-fx-lg border border-fx-border bg-fx-surface-2 px-3 py-2.5">
      <div className="relative h-11 w-11 shrink-0">
        {showImg ? (
          <img
            src={agentFileURL(file!.name)}
            alt={label}
            className="h-full w-full rounded-full border border-fx-border object-cover"
            onError={() => setErrored(true)}
            ref={el => { if (el?.complete && el.naturalWidth === 0 && !errored) setErrored(true); }}
          />
        ) : (
          <span className="flex h-full w-full items-center justify-center rounded-full border-2 border-dashed border-fx-border-strong">
            <Icon className="h-4 w-4 text-fx-text-3" aria-hidden="true" />
          </span>
        )}
        {file && (
          <span className="absolute -bottom-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-fx-success text-fx-surface-1 ring-2 ring-fx-surface-2">
            <Check className="h-2.5 w-2.5" strokeWidth={3} aria-hidden="true" />
            <span className="sr-only">Foto cargada</span>
          </span>
        )}
      </div>
      <div className="min-w-0">
        <p className="m-0 text-fx-body-sm font-semibold leading-tight text-fx-text">{label}</p>
        <p className="m-0 text-xs text-fx-text-3">
          {file ? role : "Sin foto · opcional"}
        </p>
      </div>
    </div>
  );
}

/* ── Grupo de evidencia — título + conteo + fila de miniaturas. ── */
function EvidenceGroup({
  title, icon: Icon, items, kind,
}: { title: string; icon: React.ElementType; items: CapturedFile[]; kind: "image" | "video" | "doc" }) {
  if (items.length === 0) return null;
  return (
    <div>
      <p className="m-0 mb-1.5 flex items-center gap-1.5 text-xs font-medium text-fx-text-2">
        <Icon className="h-3.5 w-3.5 text-fx-text-3" aria-hidden="true" />
        {title}
        <span className="text-fx-text-3">· {items.length}</span>
      </p>
      <div className="flex flex-wrap gap-2">
        {items.map(f => <Thumb key={f.name} name={f.name} kind={kind} />)}
      </div>
    </div>
  );
}

export function GenerateStep({ currentCase, files, loading, serverMissing = [], onBack, onGenerate, onGoToField }: Props) {
  // Solo se promete cifrado si el backend lo tiene activo (`encrypt_zip`).
  const { encryptZip } = usePublicConfig();
  const missing: MissingRequirement[] = [...getMissingRequirements(currentCase)];
  serverMissing.forEach(k => {
    const m = describeMissing(k);
    if (m && !missing.some(x => x.label === m.label)) missing.push(m);
  });
  const blocked = missing.length > 0;

  const fiscalFile      = files.find(f => isFuncionario(f.name));
  const denuncianteFile = files.find(f => isDenunciante(f.name));
  const captures        = files.filter(f => isCapture(f.name));
  const videos          = files.filter(f => isVideo(f.name));
  const attachments     = files.filter(f => !isIdentity(f.name) && !isVideo(f.name) && !isCapture(f.name));

  const dev = currentCase.device;
  const deviceName = dev ? [dev.manufacturer, dev.model].filter(Boolean).join(" ") : null;
  const deviceOS = dev
    ? dev.platform === "ios"
      ? `iOS ${dev.os_version ?? ""}`.trim()
      : `Android ${dev.android_version ?? ""}`.trim()
    : null;

  const folders = [
    { name: "capturas", count: captures.length },
    { name: "videos", count: videos.length },
    { name: "identificaciones", count: (fiscalFile ? 1 : 0) + (denuncianteFile ? 1 : 0) },
    { name: "adjuntos", count: attachments.length },
  ].filter(f => f.count > 0);

  const guarantees = [
    encryptZip
      ? { icon: Lock, text: "ZIP cifrado con AES-256 y contraseña única" }
      : { icon: Archive, text: "ZIP de evidencia con hash SHA-256 verificable" },
    { icon: Fingerprint, text: "Hash SHA-256 calculado por cada archivo" },
    { icon: FileText, text: "Informe pericial en Word con la tabla de valores hash" },
  ];

  return (
    <div className="mx-auto max-w-2xl space-y-5">
      <StepHeader
        title="Revisá y generá el informe"
        description="Confirmá que esté toda la evidencia. Al generar, el caso queda cerrado para edición."
      />

      {/* ── Obligatorios que faltan: cada uno lleva al campo ── */}
      {blocked && (
        <section
          aria-labelledby="generate-missing-title"
          className="rounded-fx-lg border border-fx-warning bg-fx-warning-soft p-4 sm:p-5 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
        >
          <h3 id="generate-missing-title" className="m-0 flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">
            <AlertTriangle className="h-4 w-4 shrink-0 text-fx-warning" aria-hidden="true" />
            Faltan {missing.length} {missing.length === 1 ? "dato obligatorio" : "datos obligatorios"} para generar el informe
          </h3>
          <ul className="m-0 mt-3 grid list-none grid-cols-1 gap-1.5 p-0 sm:grid-cols-2">
            {missing.map(m => (
              <li key={m.key}>
                <button
                  type="button"
                  onClick={() => onGoToField(m.step, m.fieldId)}
                  className="group flex w-full min-h-11 cursor-pointer items-center gap-2 rounded-fx-md border-0 bg-transparent px-2.5 py-2 text-left text-fx-body-sm font-medium text-fx-text underline-offset-2 hover:bg-fx-surface-1 hover:underline transition-colors duration-fx-fast ease-fx fx-focus-ring"
                >
                  <ArrowRight className="h-3.5 w-3.5 shrink-0 text-fx-warning" aria-hidden="true" />
                  <span className="min-w-0 flex-1">{m.label}</span>
                  <span className="shrink-0 text-xs font-normal text-fx-text-2">Paso {m.step}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* ── Datos del caso ── */}
      <div className="rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 sm:p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="m-0 text-fx-h3 text-fx-text break-words">{currentCase.nro_referencia}</p>
            {currentCase.caratula && (
              <p className="m-0 mt-1 break-words text-fx-body-sm text-fx-text-2">{currentCase.caratula}</p>
            )}
            <p className="m-0 mt-0.5 text-xs text-fx-text-3">
              {currentCase.nombre_denunciante}
              {currentCase.dni_denunciante && <> · DNI {currentCase.dni_denunciante}</>}
            </p>
          </div>
          {deviceName && <Tag value={`${deviceName}${deviceOS ? ` · ${deviceOS}` : ""}`} />}
        </div>
        {currentCase.observaciones && (
          <p className="m-0 mt-3 border-t border-fx-border pt-3 text-xs leading-relaxed text-fx-text-2">
            {currentCase.observaciones}
          </p>
        )}
      </div>

      {/* ── Identificación ── */}
      <section aria-labelledby="generate-identity-title">
        <h3 id="generate-identity-title" className={`${BLOCK_TITLE} mb-2`}>Identificación · opcional</h3>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <IdentityConfirm label="Perito" role="Quien realiza la inspección" icon={Shield} file={fiscalFile} />
          <IdentityConfirm label="Titular del dispositivo" role="Titular" icon={User} file={denuncianteFile} />
        </div>
      </section>

      {/* ── Evidencia ── */}
      <section aria-labelledby="generate-evidence-title">
        <div className="mb-2 flex items-center justify-between gap-3">
          <h3 id="generate-evidence-title" className={BLOCK_TITLE}>Evidencia</h3>
          <Tag value={`${files.length} ${files.length === 1 ? "archivo" : "archivos"}`} />
        </div>
        {captures.length + videos.length + attachments.length === 0 ? (
          <div className="rounded-fx-lg border border-dashed border-fx-border-strong bg-fx-surface-2 px-4 py-6 text-center text-fx-body-sm text-fx-text-2">
            No se capturó evidencia del dispositivo. Podés volver atrás para agregarla.
          </div>
        ) : (
          <div className="space-y-3 rounded-fx-lg border border-fx-border bg-fx-surface-1 p-3.5">
            <EvidenceGroup title="Capturas de pantalla" icon={ImageIcon} items={captures} kind="image" />
            <EvidenceGroup title="Grabaciones" icon={Video} items={videos} kind="video" />
            <EvidenceGroup title="Adjuntos" icon={Paperclip} items={attachments} kind="doc" />
          </div>
        )}
      </section>

      {/* ── El paquete ── */}
      <section aria-labelledby="generate-package-title" className="rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 sm:p-5">
        <h3 id="generate-package-title" className={BLOCK_TITLE}>El paquete</h3>
        <ul className="m-0 mt-2.5 list-none space-y-2 p-0">
          {guarantees.map(({ icon: Icon, text }) => (
            <li key={text} className="flex items-start gap-2 text-fx-body-sm text-fx-text-2">
              <Icon className="mt-0.5 h-4 w-4 shrink-0 text-fx-text-2" aria-hidden="true" />
              {text}
            </li>
          ))}
        </ul>
        {folders.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1.5 border-t border-fx-border pt-3">
            {folders.map(f => (
              <Tag
                key={f.name}
                icon={<FolderClosed className="h-3 w-3" aria-hidden="true" />}
                value={`${f.name}/ ${f.count}`}
              />
            ))}
          </div>
        )}
      </section>

      {/* ── Acciones ── */}
      <StepActions>
        <Button
          type="button"
          severity="secondary"
          icon={<ArrowLeft className="h-4 w-4" aria-hidden="true" />}
          label="Atrás"
          onClick={onBack}
          className="w-full sm:w-auto min-h-11"
        />
        <Button
          type="button"
          size="large"
          label={loading ? "Generando informe pericial…" : "Generar informe pericial"}
          icon={<FileCheck2 className="h-5 w-5" aria-hidden="true" />}
          loading={loading}
          disabled={loading || blocked}
          aria-describedby={blocked ? "generate-missing-title" : undefined}
          onClick={onGenerate}
          className="w-full sm:flex-1 min-h-11"
        />
      </StepActions>
      <span className="sr-only" role="status" aria-live="polite">
        {loading ? "Generando informe pericial, esperá…" : ""}
      </span>

      <p className="m-0 flex items-center justify-center gap-1.5 text-xs text-fx-text-3">
        <ShieldCheck className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        Una vez generado, el caso queda cerrado para edición
      </p>
    </div>
  );
}
