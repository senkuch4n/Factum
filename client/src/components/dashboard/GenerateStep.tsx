"use client";

import { useState } from "react";
import { motion } from "framer-motion";
import {
  Loader2, Sparkles, RotateCcw, ShieldCheck, Shield, User,
  FileText, Video, ImageIcon, Paperclip, Check, Lock, Fingerprint, FolderClosed,
  AlertTriangle, ArrowRight, Archive,
} from "lucide-react";
import type { Case, CapturedFile } from "@/types";
import { agentFileURL } from "@/lib/agent";
import { usePublicConfig } from "@/hooks/usePublicConfig";
import { describeMissing, getMissingRequirements, type MissingRequirement } from "@/lib/pericial";

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

/* ── Miniatura con fallback a ícono (mismo criterio que el Paso 3). ── */
function Thumb({ name, kind }: { name: string; kind: "image" | "video" | "doc" }) {
  const [errored, setErrored] = useState(false);
  const showImg = kind === "image" && !errored;
  return (
    <div
      className="relative h-14 w-14 flex-shrink-0 overflow-hidden rounded-lg"
      style={{ border: "1px solid var(--border)", background: "var(--bg-elevated)" }}
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
            ? <Video className="h-5 w-5" style={{ color: "#8b5cf6" }} />
            : <FileText className="h-5 w-5" style={{ color: "var(--text-muted)" }} />}
        </div>
      )}
      {kind === "video" && showImg && (
        <span className="absolute inset-0 flex items-center justify-center bg-black/30">
          <Video className="h-4 w-4 text-white" />
        </span>
      )}
    </div>
  );
}

/* ── Confirmación de una identidad (fiscal / denunciante) con su foto. ── */
function IdentityConfirm({
  label, role, icon: Icon, file,
}: { label: string; role: string; icon: React.ElementType; file?: CapturedFile }) {
  const [errored, setErrored] = useState(false);
  const showImg = !!file && !errored;
  return (
    <div
      className="flex items-center gap-3 rounded-xl px-3 py-2.5"
      style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
    >
      <div className="relative h-11 w-11 flex-shrink-0">
        {showImg ? (
          <img
            src={agentFileURL(file!.name)}
            alt={label}
            className="h-full w-full rounded-full object-cover"
            style={{ border: "1px solid var(--border)" }}
            onError={() => setErrored(true)}
            ref={el => { if (el?.complete && el.naturalWidth === 0 && !errored) setErrored(true); }}
          />
        ) : (
          <span
            className="flex h-full w-full items-center justify-center rounded-full border-2 border-dashed"
            style={{ borderColor: file ? "var(--border-md)" : "var(--border)" }}
          >
            <Icon className="h-4 w-4" style={{ color: "var(--text-muted)" }} />
          </span>
        )}
        {file && (
          <span className="absolute -bottom-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-emerald-500 ring-2 ring-[var(--bg-elevated)]">
            <Check className="h-2.5 w-2.5 text-white" strokeWidth={3} />
          </span>
        )}
      </div>
      <div className="min-w-0">
        <p className="text-sm font-semibold leading-tight" style={{ color: "var(--text-primary)" }}>{label}</p>
        <p className="text-xs" style={{ color: "var(--text-muted)" }}>
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
      <p className="mb-1.5 flex items-center gap-1.5 text-xs font-medium" style={{ color: "var(--text-secondary)" }}>
        <Icon className="h-3.5 w-3.5" style={{ color: "var(--text-muted)" }} />
        {title}
        <span style={{ color: "var(--text-muted)" }}>· {items.length}</span>
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
      <div>
        <h2 className="step-title">Revisá y generá el informe</h2>
        <p className="mt-2 text-sm" style={{ color: "var(--text-secondary)" }}>
          Confirmá que esté toda la evidencia. Al generar, el caso queda cerrado para edición.
        </p>
      </div>

      {/* ── Obligatorios que faltan: cada uno lleva al campo ── */}
      {blocked && (
        <section
          aria-labelledby="generate-missing-title"
          className="rounded-2xl p-4 sm:p-5"
          style={{ background: "rgba(217,119,6,0.06)", border: "1px solid rgba(217,119,6,0.28)" }}
        >
          <h3 id="generate-missing-title" className="m-0 flex items-center gap-2 text-sm font-semibold" style={{ color: "var(--text-primary)" }}>
            <AlertTriangle className="h-4 w-4 flex-shrink-0" style={{ color: "var(--amber)" }} aria-hidden="true" />
            Faltan {missing.length} {missing.length === 1 ? "dato obligatorio" : "datos obligatorios"} para generar el informe
          </h3>
          <ul className="mt-3 grid grid-cols-1 gap-1.5 sm:grid-cols-2">
            {missing.map(m => (
              <li key={m.key}>
                <button
                  type="button"
                  onClick={() => onGoToField(m.step, m.fieldId)}
                  className="group flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-[13px] font-medium underline-offset-2 transition-colors hover:bg-[var(--bg-hover)] hover:underline fx-focus-ring"
                  style={{ color: "var(--text-secondary)" }}
                >
                  <ArrowRight className="h-3.5 w-3.5 flex-shrink-0" style={{ color: "var(--amber)" }} aria-hidden="true" />
                  <span className="min-w-0 flex-1">{m.label}</span>
                  <span className="flex-shrink-0 text-[11px] font-normal" style={{ color: "var(--text-muted)" }}>
                    Paso {m.step}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* ── Datos del caso ── */}
      <div
        className="rounded-2xl p-4 sm:p-5"
        style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}
      >
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="text-lg font-semibold leading-tight" style={{ color: "var(--text-primary)" }}>
              {currentCase.nro_referencia}
            </p>
            {currentCase.caratula && (
              <p className="mt-1 break-words text-sm" style={{ color: "var(--text-secondary)" }}>{currentCase.caratula}</p>
            )}
            <p className={currentCase.caratula ? "mt-0.5 text-xs" : "mt-1 text-sm"} style={{ color: "var(--text-muted)" }}>
              {currentCase.nombre_denunciante}
              {currentCase.dni_denunciante && <> · DNI {currentCase.dni_denunciante}</>}
            </p>
          </div>
          {deviceName && (
            <span
              className="flex-shrink-0 rounded-lg px-2.5 py-1 text-xs font-medium"
              style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)", color: "var(--text-secondary)" }}
            >
              {deviceName}{deviceOS ? ` · ${deviceOS}` : ""}
            </span>
          )}
        </div>
        {currentCase.observaciones && (
          <p className="mt-3 border-t pt-3 text-xs leading-relaxed" style={{ borderColor: "var(--border)", color: "var(--text-muted)" }}>
            {currentCase.observaciones}
          </p>
        )}
      </div>

      {/* ── Identificación ── */}
      <div>
        <p className="section-label mb-2">Identificación · opcional</p>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <IdentityConfirm label="Perito" role="Quien realiza la inspección" icon={Shield} file={fiscalFile} />
          <IdentityConfirm label="Titular del dispositivo" role="Titular" icon={User} file={denuncianteFile} />
        </div>
      </div>

      {/* ── Evidencia ── */}
      <div>
        <div className="mb-2 flex items-center justify-between">
          <p className="section-label">Evidencia</p>
          <span
            className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
            style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)", color: "var(--text-muted)" }}
          >
            {files.length} {files.length === 1 ? "archivo" : "archivos"}
          </span>
        </div>
        {captures.length + videos.length + attachments.length === 0 ? (
          <div
            className="rounded-xl px-4 py-6 text-center text-sm"
            style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)", color: "var(--text-muted)" }}
          >
            No se capturó evidencia del dispositivo. Podés volver atrás para agregarla.
          </div>
        ) : (
          <div
            className="space-y-3 rounded-xl p-3.5"
            style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}
          >
            <EvidenceGroup title="Capturas de pantalla" icon={ImageIcon} items={captures} kind="image" />
            <EvidenceGroup title="Grabaciones" icon={Video} items={videos} kind="video" />
            <EvidenceGroup title="Adjuntos" icon={Paperclip} items={attachments} kind="doc" />
          </div>
        )}
      </div>

      {/* ── El paquete ── */}
      <div
        className="rounded-2xl p-4 sm:p-5"
        style={{ background: "rgba(13,148,136,0.05)", border: "1px solid rgba(13,148,136,0.16)" }}
      >
        <p className="section-label" style={{ color: "var(--blue-lg)" }}>El paquete</p>
        <div className="mt-2.5 space-y-2">
          {guarantees.map(({ icon: Icon, text }) => (
            <p key={text} className="flex items-start gap-2 text-sm" style={{ color: "var(--text-secondary)" }}>
              <Icon className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
              {text}
            </p>
          ))}
        </div>
        {folders.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1.5 border-t pt-3" style={{ borderColor: "rgba(13,148,136,0.16)" }}>
            {folders.map(f => (
              <span
                key={f.name}
                className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-[11px] font-medium"
                style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", color: "var(--text-secondary)" }}
              >
                <FolderClosed className="h-3 w-3" style={{ color: "var(--blue-lg)" }} />
                {f.name}/ <span style={{ color: "var(--text-muted)" }}>{f.count}</span>
              </span>
            ))}
          </div>
        )}
      </div>

      {/* ── Acciones ── */}
      <div className="flex gap-3">
        <motion.button className="btn-secondary" onClick={onBack} whileTap={{ scale: 0.98 }}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" /> Atrás
        </motion.button>
        <motion.button
          className="btn-primary btn-xl flex flex-1 items-center justify-center gap-2"
          onClick={onGenerate}
          disabled={loading || blocked}
          aria-describedby={blocked ? "generate-missing-title" : undefined}
          whileHover={{ scale: loading || blocked ? 1 : 1.02 }}
          whileTap={{ scale: loading || blocked ? 1 : 0.98 }}
        >
          {loading
            ? <><Loader2 className="h-5 w-5 animate-spin" aria-hidden="true" /> Generando informe pericial…</>
            : <><Sparkles className="h-5 w-5" aria-hidden="true" /> Generar informe pericial</>}
        </motion.button>
        <span className="sr-only" role="status" aria-live="polite">
          {loading ? "Generando informe pericial, esperá…" : ""}
        </span>
      </div>

      <p
        className="flex items-center justify-center gap-1.5 text-[11px]"
        style={{ color: "var(--text-muted)" }}
      >
        <ShieldCheck className="h-3 w-3 flex-shrink-0" aria-hidden="true" />
        Una vez generado, el caso queda cerrado para edición
      </p>
    </div>
  );
}
