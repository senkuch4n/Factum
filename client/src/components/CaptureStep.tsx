"use client";

import { useState, useRef, useEffect, useMemo } from "react";
import { Menu } from "primereact/menu";
import type { MenuItem } from "primereact/menuitem";
import { motion, AnimatePresence, useReducedMotion } from "framer-motion";
import {
  Camera, Video, VideoOff, ImageIcon, UploadCloud,
  RotateCcw, Maximize2, ExternalLink, Mic, MicOff, FolderOpen, FileText,
  AlertTriangle, Paperclip, Volume2, Trash2, Smartphone, WifiOff, Loader2, Wifi,
  Shield, User, Check, ChevronRight, Tag, Info,
} from "lucide-react";
import type { CaptureRole, CaptureRoleValue } from "@/lib/api";
import { CAPTURE_ROLES_FIELD_ID, CAPTURE_ROLE_LABELS, isRoleEligible } from "@/lib/pericial";
import type { VideoVariant } from "@/lib/agent";
import { agentFileURL } from "@/lib/agent";
import { cn } from "@/lib/utils";
import { WebcamCaptureModal } from "./WebcamCaptureModal";
import { CameraRecordModal } from "./CameraRecordModal";
import { DeviceFileExplorer, type PulledFileRef } from "./DeviceFileExplorer";
import { Lightbox } from "./Lightbox";
import { VideoCard } from "./VideoCard";
import { IdentityCard } from "./IdentityCard";
import { IOSModePicker, type IOSRecordMode } from "./IOSModePicker";

interface CapturedFile { name: string; uploaded: boolean; sourcePath?: string; captureRole?: CaptureRoleValue; }
interface LocalFile { file: File; url: string; filename: string; kind: "image" | "video" | "audio"; }

interface Props {
  isRecording: boolean;
  androidVersion: number;
  platform?: "android" | "ios";
  deviceSerial?: string;
  files: CapturedFile[];
  onScreenshot: () => void;
  onToggleRecord: () => void;
  iosModePicker?: boolean;
  iosRecordMode?: IOSRecordMode | null;
  onSelectIosMode?: (mode: IOSRecordMode) => void;
  onCancelIosMode?: () => void;
  // Fotos de identificación (opcionales). Los nombres de archivo y el tipo del agente
  // siguen siendo `foto_funcionario_*` / `foto_denunciante_*` (contrato con Tatana).
  onPhotoPerito: (blob: Blob, filename: string) => void;
  onPhotoTitular: (blob: Blob, filename: string) => void;
  // Datos del titular del dispositivo (ya cargados en el caso) para mostrarlos junto a su foto.
  titularNombre?: string;
  titularDni?: string;
  /** Marcas ya persistidas en el caso (`capture_roles`); se unen con las locales. */
  captureRoles?: CaptureRole[];
  onSetCaptureRole?: (filename: string, role: CaptureRoleValue | null) => void;
  onUploadAndContinue: () => void;
  onRemoveFile?: (filename: string) => void;
  loading: Record<string, boolean>;
  deviceOffline?: boolean;
  disconnectedDuringRecord?: boolean;
  onRetryRecording?: () => void;
  onDismissDisconnect?: () => void;
  onAttachLocalFile?: (blob: Blob, filename: string) => void;
  // Archivos traídos del explorador de archivos del dispositivo — ya existen en agent-data/
  // (no son blobs del navegador), se integran igual que un screenshot/recording. Cada uno
  // trae su ruta de origen en el dispositivo, para que quede registrada en el informe.
  onDeviceFilesAdded?: (files: PulledFileRef[]) => void;
  videoVariants?: Record<string, VideoVariant[]>;
  pendingVariantFiles?: Set<string>;
  airplayReceiverName?: string | null;
  // Sesión de "espejar para capturas" (iOS + AirPlay, marcas múltiples) — flujo separado
  // del botón normal "Capturar pantalla".
  airplayShotActive?: boolean;
  airplayShotConnected?: boolean;
  airplayShotReceiverName?: string | null;
  airplayShotMarksCount?: number;
  onStartAirplayShot?: () => void;
  onMarkAirplayShot?: () => void;
  onStopAirplayShot?: () => void;
  // "Con mic de PC" para Android — mezcla el micrófono de la PC en la grabación cuando el
  // audio digital no se puede capturar (ej. notas de voz de WhatsApp, protegidas por el SO).
  androidWithMic?: boolean;
  onToggleAndroidWithMic?: (value: boolean) => void;
}

function isVideo(name: string) { return /\.(mp4|mkv|mov)$/i.test(name); }
function fileType(name: string): "funcionario" | "denunciante" | "screenshot" | "video" | "other" {
  if (name.includes("funcionario"))  return "funcionario";
  if (name.includes("denunciante"))  return "denunciante";
  if (name.includes("screenshot") || name.includes("captura")) return "screenshot";
  if (isVideo(name))                 return "video";
  return "other";
}
function localFileKind(file: File): "image" | "video" | "audio" {
  if (file.type.startsWith("image/")) return "image";
  if (file.type.startsWith("video/")) return "video";
  return "audio";
}

/* ── Ítem unificado de la galería — funde screenshots/videos/otros (que vienen del agente)
   y los adjuntos locales en una sola línea de tiempo, para que la vista grande + filmstrip
   de abajo puedan tratarlos de forma pareja. ── */
type GKind = "screenshot" | "video" | "other" | "local-image" | "local-video" | "local-audio";
interface GItem {
  kind: GKind;
  key: string;
  name: string;
  url?: string;
  sourcePath?: string;
  remoteFile?: CapturedFile;
  localIdx?: number;
}


/* ── AirPlay connection guide (grabación "airplay" y fallback de screenshot) ── */
function AirplayConnectGuide({ airplayReceiverName }: { airplayReceiverName?: string | null }) {
  return (
    <motion.div className="rounded-xl overflow-hidden"
      initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.12 }}
      style={{ border: "1px solid rgba(245,158,11,0.25)", background: "rgba(245,158,11,0.05)" }}>
      <div className="px-3 pt-3 pb-2.5 space-y-2.5">
        <p className="text-[10px] font-bold uppercase tracking-wider" style={{ color: "#f59e0b" }}>
          Conectar el iPhone por AirPlay:
        </p>
        <div className="grid grid-cols-3 gap-2">
          {[
            { step: "1", label: "Deslizá esquina superior derecha del iPhone" },
            { step: "2", label: 'Tocá "Espejo de pantalla"' },
            { step: "3", label: `Seleccioná "${airplayReceiverName ?? "…"}"` },
          ].map(({ step, label }) => (
            <div key={step} className="flex flex-col items-center gap-1.5 text-center">
              <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 text-xs font-bold"
                style={{ background: "rgba(245,158,11,0.12)", border: "1px solid rgba(245,158,11,0.25)", color: "#f59e0b" }}>
                {step}
              </div>
              <p className="text-[10px] leading-tight" style={{ color: "var(--text-muted)" }}>{label}</p>
            </div>
          ))}
        </div>
        {airplayReceiverName && (
          <div className="flex items-center gap-2 rounded-lg px-2.5 py-2"
            style={{ background: "rgba(245,158,11,0.08)", border: "1px solid rgba(245,158,11,0.2)" }}>
            <Wifi className="w-3 h-3 flex-shrink-0" style={{ color: "#f59e0b" }} />
            <p className="text-[11px]" style={{ color: "var(--text-secondary)" }}>
              Nombre del receptor:{" "}
              <span className="font-bold font-mono" style={{ color: "#f59e0b" }}>{airplayReceiverName}</span>
            </p>
          </div>
        )}
        {!airplayReceiverName && (
          <div className="flex items-center gap-2">
            <Loader2 className="w-3 h-3 animate-spin flex-shrink-0" style={{ color: "#f59e0b" }} />
            <p className="text-[11px]" style={{ color: "var(--text-muted)" }}>Iniciando receptor AirPlay…</p>
          </div>
        )}
      </div>
    </motion.div>
  );
}

/* ── Main component ────────────────────────────────────────────── */
export function CaptureStep({
  isRecording, androidVersion, platform = "android", deviceSerial, files,
  onScreenshot, onToggleRecord,
  iosModePicker, iosRecordMode, onSelectIosMode, onCancelIosMode,
  onPhotoPerito, onPhotoTitular,
  titularNombre, titularDni,
  captureRoles = [], onSetCaptureRole,
  onUploadAndContinue, onRemoveFile, loading,
  deviceOffline,
  disconnectedDuringRecord,
  onRetryRecording,
  onDismissDisconnect,
  onAttachLocalFile,
  onDeviceFilesAdded,
  videoVariants = {},
  pendingVariantFiles = new Set(),
  airplayReceiverName,
  airplayShotActive, airplayShotConnected, airplayShotReceiverName, airplayShotMarksCount = 0,
  onStartAirplayShot, onMarkAirplayShot, onStopAirplayShot,
  androidWithMic = false, onToggleAndroidWithMic,
}: Props) {
  const isIOS = platform === "ios";
  const reduceMotion = useReducedMotion();
  const [lightbox, setLightbox]     = useState<string | null>(null);
  const [webcamTarget, setWebcam]   = useState<"funcionario" | "denunciante" | null>(null);
  const [cameraRecordOpen, setCameraRecordOpen] = useState(false);
  const [explorerOpen, setExplorerOpen] = useState(false);
  const [blobURLs, setBlobURLs]     = useState<Record<string, string>>({});
  const [localFiles, setLocalFiles] = useState<LocalFile[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [moreOpen, setMoreOpen]     = useState(false);
  const fileInputRef                = useRef<HTMLInputElement>(null);

  const byType = (t: ReturnType<typeof fileType>) => files.filter(f => fileType(f.name) === t);
  const fotoFunc    = byType("funcionario").at(-1);
  const fotoDen     = byType("denunciante").at(-1);
  const hasFuncionario = !!fotoFunc;
  const hasDenunciante = !!fotoDen;

  // ── Galería unificada, en orden de captura ──────────────────────
  const galleryItems: GItem[] = [];
  files.forEach(f => {
    const t = fileType(f.name);
    if (t === "funcionario" || t === "denunciante") return;
    galleryItems.push({ kind: t as GKind, key: f.name, name: f.name, sourcePath: f.sourcePath, remoteFile: f });
  });
  localFiles.forEach((lf, idx) => {
    const kind: GKind = lf.kind === "image" ? "local-image" : lf.kind === "video" ? "local-video" : "local-audio";
    galleryItems.push({ kind, key: `local-${lf.filename}`, name: lf.file.name, url: lf.url, localIdx: idx });
  });
  const totalItems = galleryItems.length;

  // ── Marcas de captura: las persistidas en el caso + las locales (que mandan) ──
  const roleByName = useMemo(() => {
    const m = new Map<string, CaptureRoleValue>();
    captureRoles.forEach(r => m.set(r.filename, r.role));
    files.forEach(f => {
      if (!isRoleEligible(f.name)) return;
      if (f.captureRole) m.set(f.name, f.captureRole);
      else m.delete(f.name);
    });
    return m;
  }, [captureRoles, files]);
  const roleCount = (r: CaptureRoleValue) => [...roleByName.values()].filter(v => v === r).length;
  const imeiCount = roleCount("imei_modelo");
  const nameCount = roleCount("nombre_dispositivo");
  const selected = galleryItems.find(i => i.key === selectedKey) ?? galleryItems[galleryItems.length - 1] ?? null;

  // Capturas del propio teléfono (van al "escenario" con marco de celular) vs. adjuntos
  // traídos del equipo o de la cámara externa (van como chips).
  const screenItems = galleryItems.filter(i => i.kind === "screenshot" || i.kind === "video");
  const attachItems = galleryItems.filter(i => i.kind === "local-image" || i.kind === "local-video" || i.kind === "local-audio" || i.kind === "other");

  // Cada vez que se agrega una captura nueva, la mostramos automáticamente en grande —
  // el feedback inmediato ("mirá lo que acabás de capturar") es el punto central del rediseño.
  const prevCountRef = useRef(0);
  useEffect(() => {
    if (totalItems > prevCountRef.current) {
      setSelectedKey(galleryItems[galleryItems.length - 1]?.key ?? null);
    }
    prevCountRef.current = totalItems;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [totalItems]);

  function removeItem(item: GItem) {
    if (item.localIdx !== undefined) removeLocalFile(item.localIdx);
    else onRemoveFile?.(item.name);
  }

  function handleWebcamCapture(blob: Blob, _raw: string) {
    const d = new Date();
    const ts = `${d.getFullYear()}${String(d.getMonth()+1).padStart(2,"0")}${String(d.getDate()).padStart(2,"0")}_${String(d.getHours()).padStart(2,"0")}${String(d.getMinutes()).padStart(2,"0")}${String(d.getSeconds()).padStart(2,"0")}`;
    const filename = `foto_${webcamTarget}_${ts}.jpg`;
    setBlobURLs(prev => ({ ...prev, [filename]: URL.createObjectURL(blob) }));
    if (webcamTarget === "funcionario") onPhotoPerito(blob, filename);
    else if (webcamTarget === "denunciante") onPhotoTitular(blob, filename);
    setWebcam(null);
  }

  function handleCameraRecording(blob: Blob, filename: string) {
    setLocalFiles(prev => [...prev, { file: new File([blob], filename, { type: blob.type }), url: URL.createObjectURL(blob), filename, kind: "video" }]);
    onAttachLocalFile?.(blob, filename);
    setCameraRecordOpen(false);
  }

  function processFiles(fileList: FileList) {
    const now  = new Date();
    const date = `${now.getFullYear()}${String(now.getMonth()+1).padStart(2,"0")}${String(now.getDate()).padStart(2,"0")}`;
    const time = `${String(now.getHours()).padStart(2,"0")}${String(now.getMinutes()).padStart(2,"0")}${String(now.getSeconds()).padStart(2,"0")}`;
    Array.from(fileList).forEach((file, i) => {
      const ext      = file.name.split(".").pop() ?? "bin";
      const filename = `adjunto_${date}_${time}_${i+1}.${ext}`;
      setLocalFiles(prev => [...prev, { file, url: URL.createObjectURL(file), filename, kind: localFileKind(file) }]);
      onAttachLocalFile?.(file, filename);
    });
  }

  function removeLocalFile(idx: number) {
    setLocalFiles(prev => {
      URL.revokeObjectURL(prev[idx].url);
      return prev.filter((_, i) => i !== idx);
    });
  }

  const recordBtnDisabled = !!loading.startRecord || !!loading.stopRecord || !!deviceOffline;

  return (
    <>
      {/* ── Overlay modals ── */}
      <AnimatePresence>
        {lightbox && <Lightbox src={lightbox} onClose={() => setLightbox(null)} />}
        {webcamTarget && (
          <WebcamCaptureModal
            key={webcamTarget}
            label={webcamTarget === "funcionario" ? "Foto del perito" : "Foto del titular"}
            icon={webcamTarget === "funcionario" ? Shield : User}
            onCapture={handleWebcamCapture}
            onClose={() => setWebcam(null)}
          />
        )}
        {cameraRecordOpen && (
          <CameraRecordModal
            onCapture={handleCameraRecording}
            onClose={() => setCameraRecordOpen(false)}
          />
        )}
        {explorerOpen && deviceSerial && (
          <DeviceFileExplorer
            serial={deviceSerial}
            onFilesAdded={files => onDeviceFilesAdded?.(files)}
            onClose={() => setExplorerOpen(false)}
          />
        )}
      </AnimatePresence>

      {/* ── iOS recording mode picker ── */}
      <AnimatePresence>
        {iosModePicker && (
          <IOSModePicker
            onSelect={mode => onSelectIosMode?.(mode)}
            onCancel={() => onCancelIosMode?.()}
          />
        )}
      </AnimatePresence>

      {/* ── Hidden file input ── */}
      <input ref={fileInputRef} type="file" multiple accept="image/*,video/*,audio/*" className="hidden"
        onChange={e => { if (e.target.files?.length) processFiles(e.target.files); e.target.value = ""; }} />

      {/* ══ Identificación — fila full-width ═══════════════════════ */}
      <div className="mb-6 rounded-2xl p-4 sm:p-5" style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}>
        <p className="section-label mb-1 flex items-center gap-1.5">
          <Camera className="w-3 h-3" aria-hidden="true" /> Identificación · opcional
        </p>
        <p className="mb-3 text-[11px]" style={{ color: "var(--text-muted)" }}>
          Fotos opcionales del perito y del titular. Se guardan en el ZIP con su hash; no se incluyen en el informe.
        </p>
        <div className="divide-y divide-[var(--border)] rounded-xl border border-[var(--border)] bg-[var(--bg-elevated)] px-3.5">
          <IdentityCard label="Perito" role="Quien realiza la inspección" icon={Shield}
            done={hasFuncionario}
            blobURL={fotoFunc ? blobURLs[fotoFunc.name] : undefined}
            agentFilename={fotoFunc?.name}
            onCapture={() => setWebcam("funcionario")}
            loading={!!loading.photo} />
          <IdentityCard label="Titular del dispositivo" role="Titular" icon={User}
            name={titularNombre || undefined}
            dni={titularDni || undefined}
            done={hasDenunciante}
            blobURL={fotoDen ? blobURLs[fotoDen.name] : undefined}
            agentFilename={fotoDen?.name}
            onCapture={() => setWebcam("denunciante")}
            loading={!!loading.photo} />
        </div>
      </div>

      {/* ══ Two-column layout — evidencia (teléfono, pinned) a la izquierda; el trabajo
           (capturar, bandeja, adjuntar, enviar) a la derecha y scrollea. `flex-row-reverse`
           invierte visualmente sin tocar el orden de tabulación del DOM. Sin `items-start`
           para que la columna izquierda estire su alto y el sticky del teléfono tenga
           recorrido. ═══════════════════════════════════════════════════════════════════ */}
      <div className="flex flex-row-reverse gap-6">

        {/* ─────────────── COLUMNA DE TRABAJO (se muestra a la derecha) ─────────────── */}
        <div className="flex w-[320px] flex-shrink-0 flex-col gap-3">

          {/* Estado del dispositivo */}
          <div className="flex items-center gap-2 rounded-xl px-3 py-2.5"
            style={{ background: "var(--bg-elevated)", border: `1px solid ${deviceOffline ? "rgba(239,68,68,0.35)" : "var(--border)"}` }}>
            <span className={cn("h-2 w-2 flex-shrink-0 rounded-full", deviceOffline ? "bg-red-500" : "bg-emerald-500")} />
            <span className="flex-1 text-xs font-semibold" style={{ color: "var(--text-primary)" }}>
              {isIOS ? "iPhone" : `Android ${androidVersion}`}
              <span className="font-normal" style={{ color: "var(--text-muted)" }}>
                {" · "}{deviceOffline ? "desconectado" : "conectado"}
              </span>
            </span>
            <Smartphone className="h-3.5 w-3.5 flex-shrink-0" style={{ color: "var(--text-muted)" }} />
          </div>

          {/* ── Device offline banner ── */}
          <AnimatePresence>
            {deviceOffline && !disconnectedDuringRecord && (
              <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }}
                exit={{ opacity: 0, height: 0 }} transition={{ duration: 0.25, ease: [0.22,1,0.36,1] }}
                className="overflow-hidden">
                <div className="rounded-xl p-3.5"
                  style={{ background: "rgba(239,68,68,0.06)", border: "1px solid rgba(239,68,68,0.28)" }}>
                  <div className="flex items-start gap-2.5">
                    <div className="relative flex-shrink-0 mt-0.5">
                      <div className="w-7 h-7 rounded-lg flex items-center justify-center"
                        style={{ background: "rgba(239,68,68,0.12)", border: "1px solid rgba(239,68,68,0.22)" }}>
                        <WifiOff className="w-3.5 h-3.5 text-red-500" />
                      </div>
                      {!reduceMotion && (
                        <motion.div className="absolute -inset-1 rounded-md pointer-events-none"
                          style={{ border: "1px solid rgba(239,68,68,0.45)" }}
                          animate={{ scale: [1, 1.35, 1], opacity: [0.6, 0, 0.6] }}
                          transition={{ duration: 2.2, repeat: Infinity }} />
                      )}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-[13px] font-bold text-red-600 dark:text-red-400">
                        {isIOS ? "iPhone desconectado" : "Dispositivo desconectado"}
                      </p>
                      <p className="text-[11px] mt-0.5 text-red-600/80 dark:text-red-400/80 leading-snug">
                        Reconectá el cable USB para continuar. La pantalla se actualizará sola.
                      </p>
                      <div className="flex items-center gap-1.5 mt-2">
                        <motion.div className="w-1.5 h-1.5 rounded-full bg-red-500 flex-shrink-0"
                          animate={reduceMotion ? undefined : { opacity: [1, 0.3, 1] }}
                          transition={{ duration: 1.4, repeat: Infinity }} />
                        <span className="text-[10px] font-medium" style={{ color: "rgba(239,68,68,0.7)" }}>
                          Esperando reconexión…
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              </motion.div>
            )}
          </AnimatePresence>

          {/* ── Disconnect during record banner ── */}
          <AnimatePresence>
            {disconnectedDuringRecord && (
              <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }}
                exit={{ opacity: 0, height: 0 }} transition={{ duration: 0.25 }} className="overflow-hidden">
                <div className="rounded-xl p-3.5 space-y-2.5"
                  style={{ background: "rgba(245,158,11,0.07)", border: "1px solid rgba(245,158,11,0.3)" }}>
                  <div className="flex items-start gap-2.5">
                    <div className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0 mt-0.5"
                      style={{ background: "rgba(245,158,11,0.15)", border: "1px solid rgba(245,158,11,0.25)" }}>
                      <AlertTriangle className="w-3.5 h-3.5 text-amber-500" />
                    </div>
                    <div>
                      <p className="text-[13px] font-bold text-amber-700 dark:text-amber-400">Cable desconectado</p>
                      <p className="text-[11px] mt-0.5 text-amber-600 dark:text-amber-500 leading-snug">
                        Los archivos capturados están guardados y se incluirán en la evidencia.
                      </p>
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <motion.button className="btn-secondary btn-sm flex-1 flex items-center justify-center gap-1.5"
                      onClick={onRetryRecording} whileTap={{ scale: 0.97 }}>
                      <RotateCcw className="w-3.5 h-3.5" /> Regrabar
                    </motion.button>
                    <motion.button className="btn-sm flex-1 flex items-center justify-center gap-1.5 font-semibold text-white"
                      style={{ background: "linear-gradient(135deg, #d97706, #b45309)", boxShadow: "0 2px 10px rgba(217,119,6,0.3)" }}
                      onClick={onDismissDisconnect} whileTap={{ scale: 0.97 }}>
                      Continuar →
                    </motion.button>
                  </div>
                </div>
              </motion.div>
            )}
          </AnimatePresence>

          {/* ── Card: Capturar del celular ── */}
          <div
            className="rounded-2xl p-4 space-y-3"
            style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}
          >
            <p className="section-label flex items-center gap-1.5">
              <ImageIcon className="w-3 h-3" /> Capturar del celular
            </p>

            {/* Acciones principales — par 2-up del mismo peso */}
            <div className="grid grid-cols-2 gap-2">
              <motion.button
                onClick={onScreenshot}
                disabled={!!loading.screenshot || !!deviceOffline}
                whileTap={{ scale: (loading.screenshot || deviceOffline) ? 1 : 0.97 }}
                className="flex flex-col items-center justify-center gap-1.5 rounded-xl px-2 py-3.5 text-[13px] font-semibold transition disabled:opacity-45"
                style={{ background: "var(--bg-elevated)", border: "1px solid var(--border-md)", color: "var(--text-primary)" }}
              >
                {loading.screenshot
                  ? <span className="h-5 w-5 animate-spin rounded-full border-2 border-current/25 border-t-current" />
                  : <ImageIcon className="h-5 w-5" style={{ color: "var(--blue)" }} />}
                Pantalla
              </motion.button>

              <motion.button
                onClick={onToggleRecord}
                disabled={recordBtnDisabled}
                whileTap={{ scale: recordBtnDisabled ? 1 : 0.97 }}
                className={cn(
                  "flex flex-col items-center justify-center gap-1.5 rounded-xl px-2 py-3.5 text-[13px] font-semibold transition disabled:opacity-45",
                  isRecording && "text-white",
                )}
                style={isRecording
                  ? { background: "#dc2626", border: "1px solid #dc2626" }
                  : { background: "var(--bg-elevated)", border: "1px solid var(--border-md)", color: "var(--text-primary)" }}
              >
                {loading.startRecord || loading.stopRecord
                  ? <span className="h-5 w-5 animate-spin rounded-full border-2 border-current/25 border-t-current" />
                  : isRecording
                    ? <VideoOff className="h-5 w-5" />
                    : <Video className="h-5 w-5" style={{ color: "#dc2626" }} />}
                {loading.stopRecord ? "Deteniendo…" : loading.startRecord ? "Iniciando…" : isRecording ? "Detener" : "Grabar"}
              </motion.button>
            </div>

            {/* Screenshot AirPlay fallback guide — el backend llega acá solo si DVT y USB
                fallaron (ej. sin Developer Mode y sin qvh instalado) */}
            <AnimatePresence>
              {isIOS && !!loading.screenshot && !!airplayReceiverName && (
                <AirplayConnectGuide airplayReceiverName={airplayReceiverName} />
              )}
            </AnimatePresence>

            {/* Espejar para capturas (AirPlay, marcas múltiples) — flujo separado del botón
                normal de arriba: se conecta una vez, se navega libremente en el teléfono y se
                puede marcar el momento exacto de cada captura las veces que haga falta. */}
            {isIOS && (
              <AnimatePresence mode="wait">
                {!airplayShotActive && (
                  <motion.button key="shot-start"
                    className="btn-secondary btn-sm w-full flex items-center justify-center gap-1.5"
                    onClick={onStartAirplayShot}
                    disabled={!!loading.shotStart || !!deviceOffline || isRecording}
                    whileTap={{ scale: 0.97 }}
                    style={{ opacity: (deviceOffline || isRecording) ? 0.45 : 1 }}>
                    {loading.shotStart
                      ? <div className="w-3.5 h-3.5 border-2 border-current/20 border-t-current rounded-full animate-spin" />
                      : <Wifi className="w-3.5 h-3.5" />}
                    Espejar para capturas
                  </motion.button>
                )}

                {airplayShotActive && !airplayShotConnected && (
                  <motion.div key="shot-connecting" className="space-y-2"
                    initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }}
                    exit={{ opacity: 0, height: 0 }}>
                    <AirplayConnectGuide airplayReceiverName={airplayShotReceiverName} />
                    <motion.button className="btn-ghost btn-sm w-full flex items-center justify-center gap-1.5"
                      onClick={onStopAirplayShot}
                      disabled={!!loading.shotStop}
                      whileTap={{ scale: 0.97 }}>
                      Cancelar
                    </motion.button>
                  </motion.div>
                )}

                {airplayShotActive && airplayShotConnected && (
                  <motion.div key="shot-connected" className="space-y-2"
                    initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }}
                    exit={{ opacity: 0, height: 0 }}>
                    <div className="flex items-center gap-2 rounded-xl px-3 py-2.5 border border-emerald-500/20 bg-emerald-500/[0.07]">
                      <span className="dot-live dot-live-green" />
                      <span className="text-emerald-600 dark:text-emerald-400 text-xs font-semibold flex-1">
                        Espejando en vivo — navegá y marcá cuando quieras
                      </span>
                    </div>
                    <motion.button className="btn-primary btn-sm w-full flex items-center justify-center gap-1.5"
                      onClick={onMarkAirplayShot}
                      disabled={!!loading.shotMark}
                      whileTap={{ scale: 0.97 }}>
                      {loading.shotMark
                        ? <div className="w-3.5 h-3.5 border-2 border-current/20 border-t-current rounded-full animate-spin" />
                        : <Camera className="w-3.5 h-3.5" />}
                      Marcar captura{airplayShotMarksCount > 0 ? ` (${airplayShotMarksCount})` : ""}
                    </motion.button>
                    <motion.button className="btn-secondary btn-sm w-full flex items-center justify-center gap-1.5"
                      onClick={onStopAirplayShot}
                      disabled={!!loading.shotStop}
                      whileTap={{ scale: 0.97 }}>
                      {loading.shotStop
                        ? <div className="w-3.5 h-3.5 border-2 border-current/20 border-t-current rounded-full animate-spin" />
                        : <WifiOff className="w-3.5 h-3.5" />}
                      Finalizar espejado
                    </motion.button>
                  </motion.div>
                )}
              </AnimatePresence>
            )}

            {/* Con mic de PC (Android) — mezcla el micrófono de la PC en la grabación normal,
                análogo al modo with_mic de iOS. Se oculta mientras graba (no se puede cambiar
                a mitad de una grabación en curso). */}
            {!isIOS && !isRecording && (
              <label className="flex cursor-pointer items-start gap-2.5 py-1">
                <input
                  type="checkbox"
                  checked={androidWithMic}
                  onChange={e => onToggleAndroidWithMic?.(e.target.checked)}
                  className="mt-0.5 h-4 w-4 flex-shrink-0 rounded"
                  style={{ accentColor: "#f59e0b" }}
                />
                <span className="flex-1 text-xs leading-snug" style={{ color: "var(--text-secondary)" }}>
                  Mezclar micrófono de la PC
                  <span className="mt-0.5 block text-[11px]" style={{ color: "var(--text-muted)" }}>
                    Si el audio no se graba solo (ej. notas de voz de WhatsApp)
                  </span>
                </span>
              </label>
            )}

            {/* Métodos de respaldo — cámara externa y explorador de archivos son fallbacks
                puntuales (audio protegido por el SO, evidencia específica en el almacenamiento),
                no el flujo principal. Detrás de un disclosure para no competir con las acciones
                principales, que cubren el 90% de los casos. */}
            <div className="border-t pt-2.5" style={{ borderColor: "var(--border)" }}>
              <button
                type="button"
                className="flex w-full items-center gap-1.5 text-xs font-medium transition-colors"
                onClick={() => setMoreOpen(o => !o)}
                style={{ color: "var(--text-muted)" }}
                aria-expanded={moreOpen}
                aria-controls="capture-more-options"
              >
                <ChevronRight className={cn("h-3.5 w-3.5 transition-transform", moreOpen && "rotate-90")} aria-hidden="true" />
                Más formas de capturar
              </button>

              <AnimatePresence initial={false}>
                {moreOpen && (
                  <motion.div
                    id="capture-more-options"
                    className="space-y-2 overflow-hidden pt-2"
                    initial={{ opacity: 0, height: 0 }}
                    animate={{ opacity: 1, height: "auto" }}
                    exit={{ opacity: 0, height: 0 }}
                    transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
                >
                  {/* Filmar con cámara externa — fallback manual cuando el audio digital no se
                      puede capturar (ej. notas de voz de WhatsApp en Android, protegidas por el
                      sistema) */}
                  <motion.button
                    className="btn-ghost btn-sm w-full flex items-center justify-center gap-1.5"
                    onClick={() => setCameraRecordOpen(true)}
                    disabled={!!deviceOffline}
                    whileTap={{ scale: 0.97 }}
                    style={{ opacity: deviceOffline ? 0.45 : 1 }}>
                    <Camera className="w-3.5 h-3.5" />
                    Filmar con cámara externa
                  </motion.button>

                  {/* Explorador de archivos del dispositivo (Android) — navegar el
                      almacenamiento para agregar evidencia puntual (fotos, videos, documentos)
                      sin depender solo de screenshots/grabaciones */}
                  {!isIOS && (
                    <motion.button
                      className="btn-ghost btn-sm w-full flex items-center justify-center gap-1.5"
                      onClick={() => setExplorerOpen(true)}
                      disabled={!!deviceOffline || !deviceSerial}
                      whileTap={{ scale: 0.97 }}
                      style={{ opacity: (deviceOffline || !deviceSerial) ? 0.45 : 1 }}>
                      <FolderOpen className="w-3.5 h-3.5" />
                      Explorar archivos del celular
                    </motion.button>
                    )}
                  </motion.div>
                )}
              </AnimatePresence>
            </div>

            {/* Recording status + guide */}
            <AnimatePresence>
              {isRecording && (
                <motion.div className="space-y-2"
                  initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }}
                  exit={{ opacity: 0, height: 0 }}>

                  {/* Live indicator */}
                  <div className="flex items-center gap-2 rounded-xl px-3 py-2.5 border border-red-500/20 bg-red-500/[0.07]">
                    <span className="dot-live dot-live-red" />
                    <span className="text-red-600 dark:text-red-400 text-[13px] font-semibold flex-1">
                      {iosRecordMode === "on_device" ? "Grabando en el iPhone"
                        : iosRecordMode === "airplay" ? "Espejando por AirPlay"
                        : "Grabando pantalla"}
                    </span>
                    <span className="flex items-center gap-1 text-[10px]" style={{ color: "var(--text-muted)" }}>
                      {isIOS
                        ? iosRecordMode === "on_device" ? <><Smartphone className="w-3 h-3" /> nativa</>
                          : iosRecordMode === "airplay" ? <><Wifi className="w-3 h-3 text-amber-400" /> 30fps</>
                          : iosRecordMode === "with_mic" ? <><Mic className="w-3 h-3 text-red-500" /> mic PC</>
                          : <><MicOff className="w-3 h-3" /> sin audio</>
                        : androidWithMic
                          ? <><Mic className="w-3 h-3 text-red-500" /> mic PC</>
                          : androidVersion >= 11
                            ? <><Mic className="w-3 h-3 text-red-500" /> con audio</>
                            : <><MicOff className="w-3 h-3" /> sin audio</>}
                    </span>
                  </div>

                  {/* AirPlay connection guide */}
                  {iosRecordMode === "airplay" && (
                    <AirplayConnectGuide airplayReceiverName={airplayReceiverName} />
                  )}

                  {/* on_device step guide */}
                  {iosRecordMode === "on_device" && (
                    <motion.div className="rounded-xl overflow-hidden"
                      initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.12 }}
                      style={{ border: "1px solid rgba(16,185,129,0.22)", background: "rgba(16,185,129,0.04)" }}>
                      <div className="px-3 pt-3 pb-2.5">
                        <p className="text-[10px] font-bold uppercase tracking-wider mb-2.5" style={{ color: "#10b981" }}>
                          Para detener la grabación:
                        </p>
                        <div className="grid grid-cols-3 gap-2 mb-2.5">
                          {[
                            {
                              icon: (<svg viewBox="0 0 20 20" className="w-3.5 h-3.5" fill="none">
                                <rect x="3" y="1" width="14" height="18" rx="3" stroke="#10b981" strokeWidth="1.4"/>
                                <path d="M7 5 L10 2 L13 5" stroke="#10b981" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"/>
                                <line x1="10" y1="2" x2="10" y2="8" stroke="#10b981" strokeWidth="1.4" strokeLinecap="round"/>
                              </svg>),
                              label: "Deslizá esquina superior derecha",
                            },
                            {
                              icon: (<svg viewBox="0 0 20 20" className="w-3.5 h-3.5" fill="none">
                                <circle cx="10" cy="10" r="8" stroke="#10b981" strokeWidth="1.4"/>
                                <circle cx="10" cy="10" r="4.5" fill="#ef4444"/>
                              </svg>),
                              label: "Tocá el botón de grabación",
                            },
                            {
                              icon: (<svg viewBox="0 0 20 20" className="w-3.5 h-3.5" fill="none">
                                <circle cx="10" cy="10" r="8" stroke="#ef4444" strokeWidth="1.4"/>
                                <rect x="6.5" y="6.5" width="7" height="7" rx="1.5" fill="#ef4444"/>
                              </svg>),
                              label: "Confirmá Detener en el iPhone",
                            },
                          ].map(({ icon, label }, i) => (
                            <div key={i} className="flex flex-col items-center gap-1.5 text-center">
                              <div className="w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0"
                                style={{ background: "rgba(16,185,129,0.1)", border: "1px solid rgba(16,185,129,0.2)" }}>
                                {icon}
                              </div>
                              <p className="text-[10px] leading-tight" style={{ color: "var(--text-muted)" }}>{label}</p>
                            </div>
                          ))}
                        </div>
                        <div className="flex items-center gap-1.5 pt-2" style={{ borderTop: "1px solid rgba(16,185,129,0.15)" }}>
                          <div className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse motion-reduce:animate-none flex-shrink-0" />
                          <p className="text-[11px] font-medium" style={{ color: "#10b981" }}>
                            Luego tocá <strong>"Detener"</strong> aquí para extraer el video
                          </p>
                        </div>
                      </div>
                    </motion.div>
                  )}
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          {/* ── Bandeja de capturas + adjuntos: viven en esta columna (que scrollea) para
                 quedar siempre alcanzables; en la izquierda el sticky del teléfono los tapaba. ── */}
          {(screenItems.length > 0 || attachItems.length > 0) && (
            <div className="rounded-2xl p-3.5 space-y-3" style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}>
              {screenItems.length > 0 && (
                <div>
                  <p className="section-label mb-2 flex items-center gap-1.5 text-[10px]">
                    <ImageIcon className="h-3 w-3" /> Capturas
                  </p>
                  <div className="flex gap-2.5 overflow-x-auto pb-1 [scroll-snap-type:x_proximity]">
                    {screenItems.map((item, i) => (
                      <EvidenceTrayTile
                        key={item.key}
                        item={item}
                        active={item.key === selected?.key}
                        index={i}
                        total={screenItems.length}
                        role={roleByName.get(item.name)}
                        onSelect={() => setSelectedKey(item.key)}
                        onRemove={() => removeItem(item)}
                      />
                    ))}
                  </div>
                </div>
              )}

              {attachItems.length > 0 && (
                <div className={screenItems.length > 0 ? "border-t pt-3" : ""} style={{ borderColor: "var(--border)" }}>
                  <p className="section-label mb-2 flex items-center gap-1.5 text-[10px]">
                    <Paperclip className="h-3 w-3" /> Adjuntos
                  </p>
                  <div className="flex flex-wrap gap-2">
                    {attachItems.map(item => {
                      const sizeMB = item.localIdx != null && localFiles[item.localIdx]
                        ? (localFiles[item.localIdx].file.size / (1024 * 1024)).toFixed(1)
                        : null;
                      return (
                        <AttachmentChip
                          key={item.key}
                          item={item}
                          active={item.key === selected?.key}
                          sizeMB={sizeMB}
                          onSelect={() => setSelectedKey(item.key)}
                          onRemove={() => removeItem(item)}
                        />
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* ── Dropzone: adjuntar desde el equipo (vive en esta columna para quedar
                 siempre visible; el sticky del teléfono tapaba la de la izquierda). ── */}
          <div
            className="flex-shrink-0"
            onDragOver={e => { e.preventDefault(); setIsDragging(true); }}
            onDragLeave={() => setIsDragging(false)}
            onDrop={e => { e.preventDefault(); setIsDragging(false); if (e.dataTransfer.files.length) processFiles(e.dataTransfer.files); }}
          >
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              className="group relative block w-full overflow-hidden rounded-2xl border-2 border-dashed p-5 text-center transition-colors"
              style={{
                borderColor: isDragging ? "var(--blue)" : "var(--border-md)",
                background: isDragging ? "rgba(13,148,136,0.05)" : "var(--bg-elevated)",
              }}
            >
              <span className="pointer-events-none absolute inset-0 opacity-50 [mask-image:radial-gradient(ellipse_at_center,black,transparent_72%)]">
                <GridPattern />
              </span>
              <span className="relative flex flex-col items-center gap-1.5">
                <span
                  className={cn(
                    "flex h-10 w-10 items-center justify-center rounded-xl transition-transform duration-200 group-hover:-translate-y-1",
                    isDragging && "-translate-y-1",
                  )}
                  style={{ background: "rgba(13,148,136,0.1)", border: "1px solid rgba(13,148,136,0.2)" }}
                >
                  <UploadCloud className="h-5 w-5" style={{ color: "var(--blue)" }} />
                </span>
                <span className="text-[13px] font-semibold" style={{ color: "var(--text-primary)" }}>
                  {isDragging ? "Soltá los archivos" : "Arrastrá o hacé clic para adjuntar"}
                </span>
                <span className="text-[11px]" style={{ color: "var(--text-muted)" }}>
                  Fotos, videos, audios y documentos
                </span>
              </span>
            </button>
          </div>

          {/* ── Continue CTA ── */}
          <motion.button
            className="btn-primary btn-xl w-full flex items-center justify-center gap-2"
            onClick={onUploadAndContinue}
            disabled={!!loading.upload}
            whileHover={{ scale: 1.01 }}
            whileTap={{ scale: 0.99 }}>
            {loading.upload
              ? <><div className="w-4 h-4 border-2 border-white/20 border-t-white rounded-full animate-spin" /> Enviando…</>
              : <><UploadCloud className="w-4 h-4" /> Enviar evidencia y continuar</>}
          </motion.button>
        </div>

        {/* ─────────────── COLUMNA DE EVIDENCIA — teléfono + bandeja (se muestra a la izquierda) ─────────────── */}
        <div className="flex min-w-0 flex-1 flex-col gap-3">

          {/* Header */}
          <div className="flex flex-shrink-0 items-center justify-between">
            <p className="section-label flex items-center gap-1.5">
              <ImageIcon className="h-3 w-3" /> Evidencia
            </p>
            {totalItems > 0 && (
              <div className="flex items-center gap-1.5 text-[11px] font-semibold">
                {screenItems.length > 0 && (
                  <span className="rounded-full px-2 py-0.5" style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)", color: "var(--text-muted)" }}>
                    {screenItems.length} {screenItems.length === 1 ? "captura" : "capturas"}
                  </span>
                )}
                {attachItems.length > 0 && (
                  <span className="rounded-full px-2 py-0.5" style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)", color: "var(--text-muted)" }}>
                    {attachItems.length} {attachItems.length === 1 ? "adjunto" : "adjuntos"}
                  </span>
                )}
              </div>
            )}
          </div>

          {/* ── Marcas de identificación del equipo (rol de captura para el informe) ── */}
          <div
            id={CAPTURE_ROLES_FIELD_ID}
            tabIndex={-1}
            className="space-y-2 rounded-2xl px-3.5 py-3 outline-none focus-visible:ring-2 focus-visible:ring-[var(--blue-lg)]"
            style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}
            aria-labelledby="capture-roles-title"
          >
            <p id="capture-roles-title" className="section-label flex items-center gap-1.5">
              <Tag className="h-3 w-3" aria-hidden="true" /> Capturas de identificación del equipo
            </p>
            <div className="flex flex-wrap gap-1.5 text-[11px] font-semibold" aria-live="polite">
              <RoleChip
                ok={imeiCount > 0}
                text={imeiCount > 0
                  ? `IMEI y modelo: ${imeiCount} ${imeiCount === 1 ? "captura" : "capturas"}`
                  : "IMEI y modelo: falta"}
                tone={imeiCount > 0 ? "ok" : "warn"}
              />
              <RoleChip
                ok={nameCount > 0}
                text={nameCount > 0
                  ? `Nombre del dispositivo: ${nameCount}`
                  : "Nombre del dispositivo: sin marcar (opcional)"}
                tone={nameCount > 0 ? "ok" : "muted"}
              />
            </div>
            <p className="flex items-start gap-1.5 text-[11px] leading-snug" style={{ color: "var(--text-muted)" }}>
              <Info className="mt-px h-3 w-3 flex-shrink-0" aria-hidden="true" />
              <span>
                Marcá *#06# o abrí Ajustes › Acerca del teléfono y capturá la pantalla. Después elegí la captura y
                usá «Marcar como…».
              </span>
            </p>
          </div>

          {/* ── Escenario: el ítem seleccionado en marco de teléfono. Sticky: queda fijo
                 mientras se scrollea la columna de trabajo de la derecha. ── */}
          <div className="flex flex-shrink-0 flex-col items-center gap-3 rounded-2xl p-4 sm:p-5 lg:sticky lg:top-6 lg:z-20"
            style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}>
            {selected?.kind === "video" ? (
              <div className="w-full">
                <VideoCard
                  file={selected.remoteFile!}
                  variants={videoVariants[selected.name] ?? []}
                  isPending={pendingVariantFiles.has(selected.name)}
                  onRemove={() => removeItem(selected)}
                />
              </div>
            ) : (
              <>
                <motion.div
                  key={selected?.key ?? "empty"}
                  initial={{ opacity: 0, y: 6 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
                  className="w-full"
                >
                  <PhoneFrame>
                    {selected ? (
                      <PhoneScreen item={selected} onExpand={src => setLightbox(src)} />
                    ) : (
                      <div className="flex h-full w-full flex-col items-center justify-center gap-2 px-6 text-center">
                        <ImageIcon className="h-7 w-7 text-white/25" />
                        <p className="text-xs font-medium text-white/60">Sin capturas todavía</p>
                        <p className="text-[11px] leading-relaxed text-white/35">
                          Usá <span className="text-white/55">Capturar pantalla</span> o <span className="text-white/55">Grabar</span> para empezar
                        </p>
                      </div>
                    )}
                  </PhoneFrame>
                </motion.div>

                {selected && (() => {
                  const m = kindMeta(selected.kind);
                  return (
                    <div className="mx-auto flex w-full max-w-[420px] items-center gap-2">
                      <span className="flex flex-shrink-0 items-center gap-1 rounded px-1.5 py-0.5 text-[10px] font-bold"
                        style={{ background: "var(--bg-elevated)", color: m.color, border: "1px solid var(--border)" }}>
                        <m.Icon className="h-3 w-3" aria-hidden="true" /> {m.label}
                      </span>
                      {selected.kind === "screenshot" && isRoleEligible(selected.name) && onSetCaptureRole && (
                        <CaptureRoleMenu
                          filename={selected.name}
                          role={roleByName.get(selected.name)}
                          onChange={role => onSetCaptureRole(selected.name, role)}
                        />
                      )}
                      <span className="min-w-0 flex-1 truncate font-mono text-xs" style={{ color: "var(--text-secondary)" }} title={selected.name}>
                        {selected.name}
                        {selected.sourcePath && (
                          <span className="block truncate text-[10px]" style={{ color: "var(--text-muted)" }}>{selected.sourcePath}</span>
                        )}
                      </span>
                      <button type="button" onClick={() => removeItem(selected)} aria-label={`Eliminar ${selected.name}`}
                        className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg transition hover:bg-red-500/10">
                        <Trash2 className="h-3.5 w-3.5 text-red-400" />
                      </button>
                    </div>
                  );
                })()}
              </>
            )}
          </div>
        </div>
      </div>
    </>
  );
}

/* ── Imagen con fallback — si el archivo no carga (agente mock, red, archivo movido),
   mostramos un ícono en vez del ícono roto del navegador. Con el hero nuevo una imagen
   caída ocupa mucho más lugar que en la grilla chica de antes, así que vale la pena. ── */
function SafeImg({ src, alt, className }: { src?: string; alt: string; className?: string }) {
  const [errored, setErrored] = useState(false);
  if (!src || errored) {
    return (
      <div className={cn("flex items-center justify-center", className)} style={{ background: "var(--bg-hover)" }}>
        <ImageIcon className="w-6 h-6" style={{ color: "var(--text-muted)" }} />
      </div>
    );
  }
  return <img src={src} alt={alt} className={className} onError={() => setErrored(true)} />;
}

/* ── Metadatos por tipo de ítem — etiqueta, color e ícono para badges y chips. ── */
function kindMeta(kind: GKind): { label: string; color: string; Icon: React.ElementType } {
  switch (kind) {
    case "screenshot":  return { label: "CAPTURA",    color: "#2dd4bf", Icon: ImageIcon };
    case "video":       return { label: "GRABACIÓN",  color: "#8b5cf6", Icon: Video };
    case "local-image": return { label: "IMAGEN",     color: "#2dd4bf", Icon: ImageIcon };
    case "local-video": return { label: "VIDEO",      color: "#8b5cf6", Icon: Video };
    case "local-audio": return { label: "AUDIO",      color: "#a855f7", Icon: Volume2 };
    default:            return { label: "ARCHIVO",    color: "var(--text-muted)", Icon: FileText };
  }
}

const srcOf = (item: GItem) => item.url ?? agentFileURL(item.name);

/* ── Marco de teléfono — ancla visual del escenario. Neutral y con tokens para que
   funcione en claro/oscuro; los botones laterales son decorativos. ── */
function PhoneFrame({ children }: { children: React.ReactNode }) {
  return (
    <div className="relative mx-auto w-full max-w-[220px]" style={{ aspectRatio: "236 / 470" }}>
      <span aria-hidden className="absolute -left-[3px] top-[16%] h-[6%] w-[3px] rounded-l-sm" style={{ background: "var(--border-md)" }} />
      <span aria-hidden className="absolute -left-[3px] top-[26%] h-[11%] w-[3px] rounded-l-sm" style={{ background: "var(--border-md)" }} />
      <span aria-hidden className="absolute -right-[3px] top-[22%] h-[15%] w-[3px] rounded-r-sm" style={{ background: "var(--border-md)" }} />
      <div
        className="relative h-full w-full rounded-[2.6rem] p-[9px] shadow-xl"
        style={{ background: "linear-gradient(150deg, var(--bg-elevated), var(--bg-hover))", border: "1px solid var(--border-md)" }}
      >
        <div className="relative h-full w-full overflow-hidden rounded-[2.1rem] bg-black">
          {children}
          <span aria-hidden className="absolute left-1/2 top-2 z-30 h-[20px] w-[74px] -translate-x-1/2 rounded-full bg-black ring-1 ring-white/10" />
        </div>
      </div>
    </div>
  );
}

/* ── Contenido de la pantalla del teléfono según el tipo de ítem. Los videos de
   grabación del agente NO pasan por acá — usan <VideoCard> con sus variantes. ── */
function PhoneScreen({ item, onExpand }: { item: GItem; onExpand: (src: string) => void }) {
  const src = srcOf(item);

  if (item.kind === "screenshot" || item.kind === "local-image") {
    return (
      <>
        <SafeImg src={src} alt={item.name} className="h-full w-full object-contain" />
        <button
          type="button"
          onClick={() => onExpand(src)}
          aria-label="Ver en grande"
          className="absolute bottom-3 right-3 z-20 flex h-9 w-9 items-center justify-center rounded-full bg-black/55 text-white backdrop-blur-sm transition hover:bg-black/75"
        >
          <Maximize2 className="h-4 w-4" />
        </button>
      </>
    );
  }
  if (item.kind === "local-video") {
    return <video src={src} controls playsInline preload="metadata" className="h-full w-full bg-black object-contain" />;
  }
  if (item.kind === "local-audio") {
    return (
      <div className="flex h-full w-full flex-col items-center justify-center gap-4 px-6">
        <div className="flex h-16 w-16 items-center justify-center rounded-full" style={{ background: "rgba(168,85,247,0.18)" }}>
          <Volume2 className="h-7 w-7" style={{ color: "#a855f7" }} />
        </div>
        <audio src={src} controls className="w-full" style={{ accentColor: "#a855f7" }} />
      </div>
    );
  }
  return (
    <div className="flex h-full w-full flex-col items-center justify-center gap-3 px-6 text-center">
      <FileText className="h-10 w-10 text-white/40" />
      <a
        href={src}
        target="_blank"
        rel="noreferrer"
        className="inline-flex items-center gap-1.5 rounded-lg bg-white/10 px-3 py-2 text-xs font-semibold text-white transition hover:bg-white/20"
      >
        <ExternalLink className="h-3.5 w-3.5" /> Abrir archivo
      </a>
    </div>
  );
}

/* ── Fondo de puntos para el dropzone (inspirado en Aceternity File Upload). ── */
function GridPattern() {
  return (
    <span
      className="block h-full w-full"
      style={{
        backgroundImage: "radial-gradient(var(--border) 1px, transparent 1px)",
        backgroundSize: "16px 16px",
      }}
    />
  );
}

/* ── Miniatura de la bandeja de capturas — <button> real para el orden de tabulación;
   el borrar es un botón hermano (HTML no permite <button> dentro de <button>). El
   chequeo en la esquina refuerza la selección para quien no distingue colores. ── */
function EvidenceTrayTile({
  item, active, index, total, role, onSelect, onRemove,
}: {
  item: GItem; active: boolean; index: number; total: number; role?: CaptureRoleValue;
  onSelect: () => void; onRemove: () => void;
}) {
  const meta = kindMeta(item.kind);
  const isImg = item.kind === "screenshot" || item.kind === "local-image";

  return (
    <div className="group relative flex-shrink-0 [scroll-snap-align:start]">
      <motion.button
        type="button"
        onClick={onSelect}
        aria-pressed={active}
        aria-label={`Ver ${meta.label.toLowerCase()} ${index + 1} de ${total} — ${item.name}${role ? ` — marcada como ${CAPTURE_ROLE_LABELS[role]}` : ""}`}
        className="block h-[76px] w-[76px] overflow-hidden rounded-xl transition"
        style={{ border: `2px solid ${active ? "var(--blue-lg)" : "var(--border)"}`, background: "var(--bg-elevated)" }}
        initial={{ opacity: 0, scale: 0.85 }}
        animate={{ opacity: 1, scale: 1 }}
        whileTap={{ scale: 0.95 }}
      >
        {isImg
          ? <SafeImg src={srcOf(item)} alt={item.name} className="h-full w-full object-cover" />
          : <span className="flex h-full w-full items-center justify-center"><meta.Icon className="h-5 w-5" style={{ color: "var(--text-muted)" }} /></span>}
      </motion.button>

      {role && (
        <span
          aria-hidden="true"
          title={CAPTURE_ROLE_LABELS[role]}
          className="pointer-events-none absolute inset-x-1 bottom-1 truncate rounded px-1 py-px text-center text-[9px] font-bold leading-tight text-white"
          style={{ background: "rgba(15,23,42,0.82)" }}
        >
          {role === "imei_modelo" ? "IMEI" : "Nombre"}
        </span>
      )}

      {active && (
        <span
          className="pointer-events-none absolute -bottom-1 -left-1 flex h-4 w-4 items-center justify-center rounded-full"
          style={{ background: "var(--blue-lg)", border: "1.5px solid var(--bg-surface)" }}
        >
          <Check className="h-2.5 w-2.5 text-white" strokeWidth={3} />
        </span>
      )}

      <button
        type="button"
        onClick={onRemove}
        aria-label={`Eliminar ${item.name}`}
        className={cn(
          "absolute -right-1.5 -top-1.5 flex h-6 w-6 items-center justify-center rounded-full transition-opacity focus-visible:opacity-100",
          active ? "opacity-100" : "opacity-0 group-hover:opacity-100",
        )}
        style={{ background: "rgba(220,38,38,0.95)", border: "1.5px solid var(--bg-surface)" }}
      >
        <Trash2 className="h-3 w-3 text-white" />
      </button>
    </div>
  );
}

/* ── Chip de adjunto — click en el nombre lo lleva al escenario; borrar al lado. ── */
function AttachmentChip({
  item, active, sizeMB, onSelect, onRemove,
}: { item: GItem; active: boolean; sizeMB: string | null; onSelect: () => void; onRemove: () => void }) {
  const meta = kindMeta(item.kind);
  return (
    <span
      className="inline-flex items-center gap-2 rounded-lg py-1.5 pl-2.5 pr-1.5 text-xs transition"
      style={{ border: `1px solid ${active ? "var(--blue-lg)" : "var(--border)"}`, background: "var(--bg-elevated)" }}
    >
      <button type="button" onClick={onSelect} className="flex min-w-0 items-center gap-2" title={item.name}>
        <meta.Icon className="h-3.5 w-3.5 flex-shrink-0" style={{ color: meta.color }} />
        <span className="max-w-[150px] truncate font-medium" style={{ color: "var(--text-secondary)" }}>{item.name}</span>
        {sizeMB != null && <span className="flex-shrink-0" style={{ color: "var(--text-muted)" }}>{sizeMB} MB</span>}
      </button>
      <button
        type="button"
        onClick={onRemove}
        aria-label={`Eliminar ${item.name}`}
        className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded transition hover:bg-red-500/10"
      >
        <Trash2 className="h-3 w-3 text-red-400" />
      </button>
    </span>
  );
}

/* ── Chip de estado de las marcas: texto + ícono, nunca solo color. ── */
function RoleChip({ text, tone, ok }: { text: string; tone: "ok" | "warn" | "muted"; ok: boolean }) {
  const style =
    tone === "ok"   ? { background: "rgba(16,185,129,0.08)", border: "1px solid rgba(16,185,129,0.28)", color: "var(--text-primary)" } :
    tone === "warn" ? { background: "rgba(217,119,6,0.08)",  border: "1px solid rgba(217,119,6,0.3)",   color: "var(--text-primary)" } :
                      { background: "var(--bg-elevated)",    border: "1px solid var(--border)",         color: "var(--text-muted)" };
  return (
    <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5" style={style}>
      {ok
        ? <Check className="h-3 w-3 flex-shrink-0" style={{ color: "#10b981" }} strokeWidth={3} aria-hidden="true" />
        : tone === "warn"
          ? <AlertTriangle className="h-3 w-3 flex-shrink-0" style={{ color: "var(--amber)" }} aria-hidden="true" />
          : null}
      {text}
    </span>
  );
}

/* ── "Marcar como…": Menu popup de PrimeReact (teclado, Escape y foco de vuelta los
   resuelve Prime). Una sola etiqueta por captura. ── */
function CaptureRoleMenu({
  filename, role, onChange,
}: { filename: string; role?: CaptureRoleValue; onChange: (role: CaptureRoleValue | null) => void }) {
  const menuRef = useRef<Menu>(null);
  const [open, setOpen] = useState(false);
  const menuId = "capture-role-menu";

  const items: MenuItem[] = [
    ...(["imei_modelo", "nombre_dispositivo"] as const).map(r => ({
      label: CAPTURE_ROLE_LABELS[r],
      icon: role === r
        ? <Check className="h-4 w-4" aria-hidden="true" />
        : <span className="inline-block h-4 w-4" aria-hidden="true" />,
      command: () => onChange(r),
    })),
    { separator: true },
    {
      label: "Sin marca",
      icon: !role
        ? <Check className="h-4 w-4" aria-hidden="true" />
        : <span className="inline-block h-4 w-4" aria-hidden="true" />,
      command: () => onChange(null),
    },
  ];

  return (
    <>
      <button
        type="button"
        onClick={e => menuRef.current?.toggle(e)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? `${menuId}_list` : undefined}
        aria-label={`Marcar como… ${filename}${role ? ` (actual: ${CAPTURE_ROLE_LABELS[role]})` : ""}`}
        className="flex flex-shrink-0 items-center gap-1 rounded-lg px-2 py-1 text-[11px] font-semibold transition-colors hover:bg-[var(--bg-hover)] fx-focus-ring"
        style={{ border: "1px solid var(--border-md)", color: "var(--text-secondary)" }}
      >
        <Tag className="h-3 w-3" aria-hidden="true" />
        {role ? CAPTURE_ROLE_LABELS[role] : "Marcar como…"}
      </button>
      <Menu
        ref={menuRef}
        id={menuId}
        model={items}
        popup
        pt={{ menu: { "aria-label": "Marcar captura como" } }}
        onShow={() => setOpen(true)}
        onHide={() => setOpen(false)}
      />
    </>
  );
}
