"use client";

import { useState, useRef, useEffect, useMemo } from "react";
import { Button } from "primereact/button";
import { Checkbox } from "primereact/checkbox";
import { Tag } from "primereact/tag";
import {
  Camera, Video, VideoOff, ImageIcon, UploadCloud,
  RotateCcw, Mic, MicOff, FolderOpen,
  AlertTriangle, Paperclip, Trash2, Smartphone, WifiOff, Loader2, Wifi,
  Shield, User, Check, ChevronRight, Tag as TagIcon, Info, Minus, ArrowRight,
} from "lucide-react";
import type { CaptureRole, CaptureRoleValue } from "@/lib/api";
import { CAPTURE_ROLES_FIELD_ID, isRoleEligible } from "@/lib/pericial";
import type { VideoVariant } from "@/lib/agent";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { WebcamCaptureModal } from "./WebcamCaptureModal";
import { CameraRecordModal } from "./CameraRecordModal";
import { DeviceFileExplorer, type PulledFileRef } from "./DeviceFileExplorer";
import { Lightbox } from "./Lightbox";
import { VideoCard } from "./VideoCard";
import { IdentityCard } from "./IdentityCard";
import { IOSModePicker, type IOSRecordMode } from "./IOSModePicker";
import { PhoneShell } from "./PhoneFrame";
import { FxBanner } from "./feedback/FxBanner";
import { StepHeader } from "./wizard/StepHeader";
import { StepActions } from "./wizard/StepActions";
import { MEDIA_SURFACE } from "./capture/media";
import {
  fileType, localKindOf, nextAdjuntoName, kindMeta,
  type CapturedFile, type GKind, type GItem,
} from "./capture/gallery";
import type { LocalBlobInfo } from "@/hooks/useFileManager";
import { StageEmpty, StageScreen } from "./capture/StageScreen";
import { AttachmentChip, EvidenceTrayTile, type TrayUploadState } from "./capture/EvidenceTray";
import { UploadLiveRegion, UploadProgressPanel } from "./capture/UploadProgressPanel";
import type { UploadMessage } from "@/lib/upload-messages";
import type { FileUploadState, UploadProgress } from "@/types";
import { CaptureRoleMenu } from "./capture/CaptureRoleMenu";
import { AirplayConnectGuide, OnDeviceStopGuide } from "./capture/CaptureGuides";

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
  /** Vista (blob URL, tamaño, tipo) de los archivos generados en el navegador, por nombre. */
  localBlobs?: Record<string, LocalBlobInfo>;
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
  // Envío de evidencia en curso (subida-archivos-grandes): mientras dura se
  // deshabilitan captura, adjuntar, explorador, quitar y marcas (DT13).
  uploading?: boolean;
  uploadProgress?: UploadProgress | null;
  uploadStates?: Record<string, FileUploadState>;
  uploadNotice?: UploadMessage | null;
  onCancelUpload?: () => void;
  onDismissUploadNotice?: () => void;
}

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
  localBlobs = NO_LOCAL_BLOBS,
  onDeviceFilesAdded,
  videoVariants = {},
  pendingVariantFiles = new Set(),
  airplayReceiverName,
  airplayShotActive, airplayShotConnected, airplayShotReceiverName, airplayShotMarksCount = 0,
  onStartAirplayShot, onMarkAirplayShot, onStopAirplayShot,
  androidWithMic = false, onToggleAndroidWithMic,
  uploading = false, uploadProgress = null, uploadStates = NO_UPLOAD_STATES, uploadNotice = null,
  onCancelUpload, onDismissUploadNotice,
}: Props) {
  const isIOS = platform === "ios";
  const [lightbox, setLightbox]     = useState<string | null>(null);
  const [webcamTarget, setWebcam]   = useState<"funcionario" | "denunciante" | null>(null);
  const [cameraRecordOpen, setCameraRecordOpen] = useState(false);
  const [explorerOpen, setExplorerOpen] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [moreOpen, setMoreOpen]     = useState(false);
  const fileInputRef                = useRef<HTMLInputElement>(null);
  // Nombres de adjunto ya generados en esta sesión del paso: cubre dos tandas en el
  // mismo tick, antes de que `files` se actualice.
  const usedNamesRef                = useRef<Set<string>>(new Set());

  // Última persona de la webcam: la etiqueta del diálogo no salta durante la
  // animación de salida (ajuste de estado durante el render, patrón de React).
  const [lastWebcamTarget, setLastWebcamTarget] = useState<"funcionario" | "denunciante">("funcionario");
  if (webcamTarget && webcamTarget !== lastWebcamTarget) setLastWebcamTarget(webcamTarget);

  const byType = (t: ReturnType<typeof fileType>) => files.filter(f => fileType(f.name) === t);
  const fotoFunc    = byType("funcionario").at(-1);
  const fotoDen     = byType("denunciante").at(-1);
  const hasFuncionario = !!fotoFunc;
  const hasDenunciante = !!fotoDen;

  // ── Galería unificada, en orden de captura. Una sola fuente: `files` (lo que se
  //    sube). Los archivos generados en el navegador toman su vista de `localBlobs`. ──
  const galleryItems: GItem[] = [];
  files.forEach(f => {
    const t = fileType(f.name);
    if (t === "funcionario" || t === "denunciante") return;
    const lb = localBlobs[f.name];
    if (lb) {
      galleryItems.push({
        kind: localKindOf(lb.type, f.name), key: f.name, name: f.name,
        url: lb.url, sizeBytes: lb.size, originalName: lb.originalName, remoteFile: f,
      });
    } else {
      galleryItems.push({ kind: t as GKind, key: f.name, name: f.name, sourcePath: f.sourcePath, remoteFile: f });
    }
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
    if (uploading) return;
    onRemoveFile?.(item.name);
  }

  /** Estado de envío de un ítem: subido (en el caso), subiendo/error (esta tanda) o pendiente. */
  function uploadStateOf(item: GItem): TrayUploadState {
    if (item.remoteFile?.uploaded) return "uploaded";
    return uploadStates[item.name] ?? "pending";
  }

  function handleWebcamCapture(blob: Blob, _raw: string) {
    const d = new Date();
    const ts = `${d.getFullYear()}${String(d.getMonth()+1).padStart(2,"0")}${String(d.getDate()).padStart(2,"0")}_${String(d.getHours()).padStart(2,"0")}${String(d.getMinutes()).padStart(2,"0")}${String(d.getSeconds()).padStart(2,"0")}`;
    const filename = `foto_${webcamTarget}_${ts}.jpg`;
    if (webcamTarget === "funcionario") onPhotoPerito(blob, filename);
    else if (webcamTarget === "denunciante") onPhotoTitular(blob, filename);
    setWebcam(null);
  }

  function handleCameraRecording(blob: Blob, filename: string) {
    onAttachLocalFile?.(blob, filename);
    setCameraRecordOpen(false);
  }

  function processFiles(fileList: FileList) {
    const now  = new Date();
    const date = `${now.getFullYear()}${String(now.getMonth()+1).padStart(2,"0")}${String(now.getDate()).padStart(2,"0")}`;
    const time = `${String(now.getHours()).padStart(2,"0")}${String(now.getMinutes()).padStart(2,"0")}${String(now.getSeconds()).padStart(2,"0")}`;
    const taken = new Set([...files.map(f => f.name), ...usedNamesRef.current]);
    Array.from(fileList).forEach(file => {
      const ext      = file.name.split(".").pop() ?? "bin";
      const filename = nextAdjuntoName(date, time, ext, taken);
      taken.add(filename);
      usedNamesRef.current.add(filename);
      onAttachLocalFile?.(file, filename);
    });
  }

  const recordBtnDisabled = !!loading.startRecord || !!loading.stopRecord || !!deviceOffline || uploading;
  const recordBusy = !!loading.startRecord || !!loading.stopRecord;

  return (
    <>
      {/* ── Diálogos (siempre montados; el Dialog de Prime monta el contenido al abrir) ── */}
      <Lightbox src={lightbox} onClose={() => setLightbox(null)} />
      <WebcamCaptureModal
        open={webcamTarget !== null}
        label={lastWebcamTarget === "funcionario" ? "Foto del perito" : "Foto del titular"}
        icon={lastWebcamTarget === "funcionario" ? Shield : User}
        onCapture={handleWebcamCapture}
        onClose={() => setWebcam(null)}
      />
      <CameraRecordModal
        open={cameraRecordOpen}
        onCapture={handleCameraRecording}
        onClose={() => setCameraRecordOpen(false)}
      />
      {deviceSerial && (
        <DeviceFileExplorer
          open={explorerOpen}
          serial={deviceSerial}
          onFilesAdded={files => onDeviceFilesAdded?.(files)}
          onClose={() => setExplorerOpen(false)}
        />
      )}
      <IOSModePicker
        open={!!iosModePicker}
        onSelect={mode => onSelectIosMode?.(mode)}
        onCancel={() => onCancelIosMode?.()}
      />

      {/* ── Input de archivos oculto ── */}
      <input ref={fileInputRef} type="file" multiple accept="image/*,video/*,audio/*" className="hidden"
        disabled={uploading}
        onChange={e => { if (!uploading && e.target.files?.length) processFiles(e.target.files); e.target.value = ""; }} />

      <StepHeader
        title="Captura de evidencia"
        description="Capturá la pantalla, grabá o adjuntá archivos del celular. Cada archivo se guarda con su hash."
        className="mb-6"
      />

      {/* ══ Identificación — fila full-width ═══════════════════════ */}
      <section aria-labelledby="capture-identity-title" className="fx-card mb-6 p-4 sm:p-5">
        <h3 id="capture-identity-title" className={LABEL}>
          <Camera className="h-3.5 w-3.5" aria-hidden="true" /> Identificación · opcional
        </h3>
        <p className="m-0 mb-3 mt-1 text-xs text-fx-text-3">
          Fotos opcionales del perito y del titular. Se guardan en el ZIP con su hash; no se incluyen en el informe.
        </p>
        <ul className="m-0 list-none divide-y divide-fx-border rounded-fx-lg border border-fx-border bg-fx-surface-2 px-3.5 py-0">
          <li>
            <IdentityCard label="Perito" role="Quien realiza la inspección" icon={Shield}
              done={hasFuncionario}
              blobURL={fotoFunc ? localBlobs[fotoFunc.name]?.url : undefined}
              agentFilename={fotoFunc?.name}
              onCapture={() => setWebcam("funcionario")}
              loading={!!loading.photo}
              disabled={uploading} />
          </li>
          <li>
            <IdentityCard label="Titular del dispositivo" role="Titular" icon={User}
              name={titularNombre || undefined}
              dni={titularDni || undefined}
              done={hasDenunciante}
              blobURL={fotoDen ? localBlobs[fotoDen.name]?.url : undefined}
              agentFilename={fotoDen?.name}
              onCapture={() => setWebcam("denunciante")}
              loading={!!loading.photo}
              disabled={uploading} />
          </li>
        </ul>
      </section>

      {/* ══ Dos columnas desde lg: evidencia (teléfono, sticky) a la izquierda y el trabajo
           (capturar, bandeja, adjuntar) a la derecha. `lg:flex-row-reverse` invierte
           visualmente sin tocar el orden de tabulación. Debajo de lg se apilan: trabajo
           arriba y evidencia abajo (orden del DOM). Sin `items-start` para que la columna
           de evidencia estire su alto y el sticky del teléfono tenga recorrido. ═══════ */}
      <div className="flex flex-col gap-6 lg:flex-row-reverse">

        {/* ─────────────── COLUMNA DE TRABAJO ─────────────── */}
        <div className="flex w-full flex-col gap-3 lg:w-[320px] lg:shrink-0">

          {/* Estado del dispositivo */}
          <div
            role="status"
            className={cn(
              "flex items-center gap-2 rounded-fx-lg border px-3 py-2.5 text-xs",
              deviceOffline ? "border-fx-danger bg-fx-danger-soft" : "border-fx-border bg-fx-surface-2",
            )}
          >
            {deviceOffline
              ? <WifiOff className="h-3.5 w-3.5 shrink-0 text-fx-danger" aria-hidden="true" />
              : <Wifi className="h-3.5 w-3.5 shrink-0 text-fx-success" aria-hidden="true" />}
            <span className="flex-1 font-semibold text-fx-text">
              {isIOS ? "iPhone" : `Android ${androidVersion}`}
              <span className="font-normal text-fx-text-2">
                {" · "}{deviceOffline ? "desconectado" : "conectado"}
              </span>
            </span>
            <Smartphone className="h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
          </div>

          {/* ── Equipo desconectado ── */}
          {deviceOffline && !disconnectedDuringRecord && (
            <FxBanner tone="error" icon={<WifiOff className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
              <p className="m-0 font-bold">{isIOS ? "iPhone desconectado" : "Dispositivo desconectado"}</p>
              <p className="m-0 mt-0.5 text-xs font-normal">
                Reconectá el cable USB para continuar. La pantalla se actualizará sola.
              </p>
              <p className="m-0 mt-2 flex items-center gap-1.5 text-xs">
                <Loader2 className="h-3 w-3 shrink-0 animate-spin" aria-hidden="true" /> Esperando reconexión…
              </p>
            </FxBanner>
          )}

          {/* ── Cable desconectado durante la grabación ── */}
          {disconnectedDuringRecord && (
            <FxBanner tone="warn" icon={<AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
              <p className="m-0 font-bold">Cable desconectado</p>
              <p className="m-0 mt-0.5 text-xs font-normal">
                Los archivos capturados están guardados y se incluirán en la evidencia.
              </p>
              <div className="mt-2.5 flex gap-2">
                <Button type="button" size="small" severity="secondary"
                  icon={<RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
                  label="Regrabar" onClick={onRetryRecording} className="flex-1 min-h-11 sm:min-h-0" />
                <Button type="button" size="small"
                  icon={<ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />} iconPos="right"
                  label="Continuar" onClick={onDismissDisconnect} className="flex-1 min-h-11 sm:min-h-0" />
              </div>
            </FxBanner>
          )}

          {/* ── Tarjeta: Capturar del celular ── */}
          <section aria-labelledby="capture-actions-title" className="fx-card space-y-3 p-4">
            <h3 id="capture-actions-title" className={LABEL}>
              <ImageIcon className="h-3.5 w-3.5" aria-hidden="true" /> Capturar del celular
            </h3>

            {/* Acciones principales — par 2-up del mismo peso */}
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={onScreenshot}
                disabled={!!loading.screenshot || !!deviceOffline || uploading}
                aria-busy={!!loading.screenshot || undefined}
                className={cn(TILE, TILE_IDLE)}
              >
                {loading.screenshot
                  ? <Loader2 className="h-5 w-5 animate-spin" aria-hidden="true" />
                  : <ImageIcon className="h-5 w-5 text-fx-text-2" aria-hidden="true" />}
                Pantalla
              </button>

              <button
                type="button"
                onClick={onToggleRecord}
                disabled={recordBtnDisabled}
                aria-busy={recordBusy || undefined}
                className={cn(
                  TILE,
                  isRecording
                    ? "border-fx-danger-fill bg-fx-danger-fill text-fx-on-danger enabled:hover:bg-fx-danger-fill-hover"
                    : TILE_IDLE,
                )}
              >
                {recordBusy
                  ? <Loader2 className="h-5 w-5 animate-spin" aria-hidden="true" />
                  : isRecording
                    ? <VideoOff className="h-5 w-5" aria-hidden="true" />
                    : <Video className="h-5 w-5 text-fx-danger" aria-hidden="true" />}
                {loading.stopRecord ? "Deteniendo…" : loading.startRecord ? "Iniciando…" : isRecording ? "Detener" : "Grabar"}
              </button>
            </div>

            {/* Guía AirPlay de respaldo del screenshot — el backend llega acá solo si DVT y
                USB fallaron (ej. sin Developer Mode y sin qvh instalado) */}
            {isIOS && !!loading.screenshot && !!airplayReceiverName && (
              <AirplayConnectGuide airplayReceiverName={airplayReceiverName} />
            )}

            {/* Espejar para capturas (AirPlay, marcas múltiples) — flujo separado del botón
                normal de arriba: se conecta una vez, se navega libremente en el teléfono y se
                puede marcar el momento exacto de cada captura las veces que haga falta. */}
            {isIOS && !airplayShotActive && (
              <Button type="button" severity="secondary" size="small"
                icon={<Wifi className="h-3.5 w-3.5" aria-hidden="true" />}
                label="Espejar para capturas"
                onClick={onStartAirplayShot}
                loading={!!loading.shotStart}
                disabled={!!deviceOffline || isRecording || uploading}
                className="w-full min-h-11 sm:min-h-0" />
            )}
            {isIOS && airplayShotActive && !airplayShotConnected && (
              <div className={cn("space-y-2", ENTER)}>
                <AirplayConnectGuide airplayReceiverName={airplayShotReceiverName} />
                <Button type="button" text severity="secondary" size="small" label="Cancelar"
                  onClick={onStopAirplayShot} disabled={!!loading.shotStop || uploading} className="w-full min-h-11 sm:min-h-0" />
              </div>
            )}
            {isIOS && airplayShotActive && airplayShotConnected && (
              <div className={cn("space-y-2", ENTER)}>
                <div role="status" className="flex items-center gap-2 rounded-fx-lg border border-fx-success bg-fx-success-soft px-3 py-2.5 text-xs font-semibold text-fx-success">
                  <Wifi className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  Espejando en vivo — navegá y marcá cuando quieras
                </div>
                <Button type="button" size="small"
                  icon={<Camera className="h-3.5 w-3.5" aria-hidden="true" />}
                  label={`Marcar captura${airplayShotMarksCount > 0 ? ` (${airplayShotMarksCount})` : ""}`}
                  onClick={onMarkAirplayShot} loading={!!loading.shotMark} disabled={uploading} className="w-full min-h-11 sm:min-h-0" />
                <Button type="button" severity="secondary" size="small"
                  icon={<WifiOff className="h-3.5 w-3.5" aria-hidden="true" />}
                  label="Finalizar espejado"
                  onClick={onStopAirplayShot} loading={!!loading.shotStop} disabled={uploading} className="w-full min-h-11 sm:min-h-0" />
              </div>
            )}

            {/* Mezclar micrófono de la PC (Android) — mezcla el micrófono de la PC en la
                grabación normal, análogo al modo with_mic de iOS. Se oculta mientras graba. */}
            {!isIOS && !isRecording && (
              <div className="flex items-start gap-2.5 py-1">
                <Checkbox
                  inputId="capture-android-mic"
                  checked={androidWithMic}
                  onChange={e => onToggleAndroidWithMic?.(!!e.checked)}
                  disabled={uploading}
                  icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />}
                  aria-describedby="capture-android-mic-hint"
                  className="mt-0.5"
                />
                <div className="min-w-0">
                  <label htmlFor="capture-android-mic" className="cursor-pointer text-fx-body-sm text-fx-text">
                    Mezclar micrófono de la PC
                  </label>
                  <p id="capture-android-mic-hint" className="m-0 mt-0.5 text-xs text-fx-text-3">
                    Si el audio no se graba solo (ej. notas de voz de WhatsApp)
                  </p>
                </div>
              </div>
            )}

            {/* Métodos de respaldo — cámara externa y explorador de archivos son fallbacks
                puntuales, detrás de un disclosure para no competir con las acciones principales. */}
            <div className="border-t border-fx-border pt-2.5">
              <button
                type="button"
                className={cn(
                  "flex min-h-9 w-full items-center gap-1.5 rounded-fx-md px-1 text-xs font-medium text-fx-text-2",
                  "transition-colors duration-fx-fast ease-fx hover:text-fx-text",
                  FOCUS_RING,
                )}
                onClick={() => setMoreOpen(o => !o)}
                aria-expanded={moreOpen}
                aria-controls="capture-more-options"
              >
                <ChevronRight
                  className={cn("h-3.5 w-3.5 transition-transform duration-fx-fast ease-fx motion-reduce:transition-none", moreOpen && "rotate-90")}
                  aria-hidden="true"
                />
                Más formas de capturar
              </button>

              {moreOpen && (
                <div id="capture-more-options" className={cn("space-y-2 pt-2", ENTER)}>
                  <Button type="button" outlined severity="secondary" size="small"
                    icon={<Camera className="h-3.5 w-3.5" aria-hidden="true" />}
                    label="Filmar con cámara externa"
                    onClick={() => setCameraRecordOpen(true)}
                    disabled={!!deviceOffline || uploading}
                    aria-haspopup="dialog"
                    className="w-full min-h-11 sm:min-h-0" />
                  {!isIOS && (
                    <Button type="button" outlined severity="secondary" size="small"
                      icon={<FolderOpen className="h-3.5 w-3.5" aria-hidden="true" />}
                      label="Explorar archivos del celular"
                      onClick={() => setExplorerOpen(true)}
                      disabled={!!deviceOffline || !deviceSerial || uploading}
                      aria-haspopup="dialog"
                      className="w-full min-h-11 sm:min-h-0" />
                  )}
                </div>
              )}
            </div>

            {/* Estado de grabación + guía */}
            {isRecording && (
              <div className={cn("space-y-2", ENTER)}>
                <div role="status" className="flex items-center gap-2 rounded-fx-lg border border-fx-danger bg-fx-danger-soft px-3 py-2.5">
                  <span className="h-2 w-2 shrink-0 rounded-full bg-fx-danger motion-safe:animate-pulse" aria-hidden="true" />
                  <span className="text-xs font-bold text-fx-danger">REC</span>
                  <span className="flex-1 text-fx-body-sm font-semibold text-fx-text">
                    {iosRecordMode === "on_device" ? "Grabando en el iPhone"
                      : iosRecordMode === "airplay" ? "Espejando por AirPlay"
                      : "Grabando pantalla"}
                  </span>
                  <span className="flex items-center gap-1 text-xs text-fx-text-2 [&_svg]:text-fx-text-3">
                    {isIOS
                      ? iosRecordMode === "on_device" ? <><Smartphone className="h-3 w-3" aria-hidden="true" /> nativa</>
                        : iosRecordMode === "airplay" ? <><Wifi className="h-3 w-3" aria-hidden="true" /> 30fps</>
                        : iosRecordMode === "with_mic" ? <><Mic className="h-3 w-3" aria-hidden="true" /> mic PC</>
                        : <><MicOff className="h-3 w-3" aria-hidden="true" /> sin audio</>
                      : androidWithMic
                        ? <><Mic className="h-3 w-3" aria-hidden="true" /> mic PC</>
                        : androidVersion >= 11
                          ? <><Mic className="h-3 w-3" aria-hidden="true" /> con audio</>
                          : <><MicOff className="h-3 w-3" aria-hidden="true" /> sin audio</>}
                  </span>
                </div>

                {iosRecordMode === "airplay" && <AirplayConnectGuide airplayReceiverName={airplayReceiverName} />}
                {iosRecordMode === "on_device" && <OnDeviceStopGuide />}
              </div>
            )}
          </section>

          {/* ── Bandeja de capturas + adjuntos ── */}
          {(screenItems.length > 0 || attachItems.length > 0) && (
            <div className="fx-card space-y-3 p-3.5">
              {screenItems.length > 0 && (
                <div>
                  <h4 className={cn(LABEL, "mb-2")}>
                    <ImageIcon className="h-3.5 w-3.5" aria-hidden="true" /> Capturas
                  </h4>
                  <ul className="m-0 flex list-none gap-2.5 overflow-x-auto px-1 pb-1 pt-2 [scroll-snap-type:x_proximity]">
                    {screenItems.map((item, i) => (
                      <EvidenceTrayTile
                        key={item.key}
                        item={item}
                        active={item.key === selected?.key}
                        index={i}
                        total={screenItems.length}
                        role={roleByName.get(item.name)}
                        uploadState={uploadStateOf(item)}
                        removeDisabled={uploading}
                        onSelect={() => setSelectedKey(item.key)}
                        onRemove={() => removeItem(item)}
                      />
                    ))}
                  </ul>
                </div>
              )}

              {attachItems.length > 0 && (
                <div className={screenItems.length > 0 ? "border-t border-fx-border pt-3" : undefined}>
                  <h4 className={cn(LABEL, "mb-2")}>
                    <Paperclip className="h-3.5 w-3.5" aria-hidden="true" /> Adjuntos
                  </h4>
                  <ul className="m-0 flex list-none flex-wrap gap-2 p-0">
                    {attachItems.map(item => {
                      const sizeMB = item.sizeBytes != null
                        ? (item.sizeBytes / (1024 * 1024)).toFixed(1)
                        : null;
                      return (
                        <AttachmentChip
                          key={item.key}
                          item={item}
                          active={item.key === selected?.key}
                          sizeMB={sizeMB}
                          uploadState={uploadStateOf(item)}
                          removeDisabled={uploading}
                          onSelect={() => setSelectedKey(item.key)}
                          onRemove={() => removeItem(item)}
                        />
                      );
                    })}
                  </ul>
                </div>
              )}
            </div>
          )}

          {/* ── Zona para adjuntar (click o arrastrar) ── */}
          <div
            className="shrink-0"
            onDragOver={e => { e.preventDefault(); if (!uploading) setIsDragging(true); }}
            onDragLeave={() => setIsDragging(false)}
            onDrop={e => {
              e.preventDefault();
              setIsDragging(false);
              if (uploading) return;
              if (e.dataTransfer.files.length) processFiles(e.dataTransfer.files);
            }}
          >
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              disabled={uploading}
              className={cn(
                "group block w-full rounded-fx-xl border-2 border-dashed p-5 text-center transition-colors duration-fx-fast ease-fx",
                "disabled:cursor-not-allowed disabled:opacity-50",
                FOCUS_RING,
                isDragging ? "border-fx-accent bg-fx-accent-soft" : "border-fx-border-strong bg-fx-surface-2 enabled:hover:bg-fx-surface-3",
              )}
            >
              <span className="flex flex-col items-center gap-1.5">
                <span className="flex h-10 w-10 items-center justify-center rounded-fx-lg border border-fx-border bg-fx-surface-1 text-fx-accent-text">
                  <UploadCloud className="h-5 w-5" aria-hidden="true" />
                </span>
                <span className="text-fx-body-sm font-semibold text-fx-text">
                  {isDragging ? "Soltá los archivos" : "Arrastrá o hacé clic para adjuntar"}
                </span>
                <span className="text-xs text-fx-text-3">Fotos, videos, audios y documentos</span>
              </span>
            </button>
          </div>
        </div>

        {/* ─────────────── COLUMNA DE EVIDENCIA ─────────────── */}
        <div className="flex min-w-0 flex-1 flex-col gap-3">

          {/* Encabezado */}
          <div className="flex shrink-0 flex-wrap items-center justify-between gap-2">
            <h3 className={LABEL}>
              <ImageIcon className="h-3.5 w-3.5" aria-hidden="true" /> Evidencia
            </h3>
            {totalItems > 0 && (
              <div className="flex items-center gap-1.5">
                {screenItems.length > 0 && (
                  <Tag severity="secondary" value={`${screenItems.length} ${screenItems.length === 1 ? "captura" : "capturas"}`} />
                )}
                {attachItems.length > 0 && (
                  <Tag severity="secondary" value={`${attachItems.length} ${attachItems.length === 1 ? "adjunto" : "adjuntos"}`} />
                )}
              </div>
            )}
          </div>

          {/* ── Marcas de identificación del equipo (rol de captura para el informe).
                 GenerateStep enfoca este bloque desde el checklist: id y tabIndex no cambian. ── */}
          <div
            id={CAPTURE_ROLES_FIELD_ID}
            tabIndex={-1}
            className="fx-card fx-focus-ring space-y-2 px-3.5 py-3"
            aria-labelledby="capture-roles-title"
          >
            <h4 id="capture-roles-title" className={LABEL}>
              <TagIcon className="h-3.5 w-3.5" aria-hidden="true" /> Capturas de identificación del equipo
            </h4>
            <div className="flex flex-wrap gap-1.5" aria-live="polite">
              {imeiCount > 0 ? (
                <Tag severity="success" icon={<Check className="h-3 w-3" strokeWidth={3} aria-hidden="true" />}
                  value={`IMEI y modelo: ${imeiCount} ${imeiCount === 1 ? "captura" : "capturas"}`} />
              ) : (
                <Tag severity="warning" icon={<AlertTriangle className="h-3 w-3" aria-hidden="true" />}
                  value="IMEI y modelo: falta" />
              )}
              {nameCount > 0 ? (
                <Tag severity="success" icon={<Check className="h-3 w-3" strokeWidth={3} aria-hidden="true" />}
                  value={`Nombre del dispositivo: ${nameCount}`} />
              ) : (
                <Tag severity="secondary" icon={<Minus className="h-3 w-3" aria-hidden="true" />}
                  value="Nombre del dispositivo: sin marcar (opcional)" />
              )}
            </div>
            <p className="m-0 flex items-start gap-1.5 text-xs text-fx-text-3">
              <Info className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              <span>
                Marcá *#06# o abrí Ajustes › Acerca del teléfono y capturá la pantalla. Después elegí la captura y
                usá «Marcar como…».
              </span>
            </p>
          </div>

          {/* ── Escenario: el ítem seleccionado en marco de teléfono. Sticky desde lg: queda
                 fijo mientras se scrollea la columna de trabajo. ── */}
          <div className="fx-card flex shrink-0 flex-col items-center gap-3 p-4 sm:p-5 lg:sticky lg:top-6 lg:z-20">
            {selected?.kind === "video" ? (
              <div className="w-full">
                <VideoCard
                  file={selected.remoteFile!}
                  variants={videoVariants[selected.name] ?? []}
                  isPending={pendingVariantFiles.has(selected.name)}
                  onRemove={() => removeItem(selected)}
                  removeDisabled={uploading}
                />
              </div>
            ) : (
              <>
                <div key={selected?.key ?? "empty"} className={cn("w-full", ENTER)}>
                  <PhoneShell platform={platform} className="max-w-[220px]">
                    <div className={cn(MEDIA_SURFACE, "absolute inset-0")}>
                      {selected
                        ? <StageScreen item={selected} onExpand={src => setLightbox(src)} />
                        : <StageEmpty />}
                    </div>
                  </PhoneShell>
                </div>

                {selected && (() => {
                  const m = kindMeta(selected.kind);
                  return (
                    <div className="mx-auto flex w-full max-w-[420px] flex-wrap items-center gap-2">
                      <Tag severity="secondary" icon={<m.Icon className="h-3 w-3" aria-hidden="true" />} value={m.label} />
                      {selected.kind === "screenshot" && isRoleEligible(selected.name) && onSetCaptureRole && (
                        <CaptureRoleMenu
                          filename={selected.name}
                          role={roleByName.get(selected.name)}
                          onChange={role => onSetCaptureRole(selected.name, role)}
                          disabled={uploading}
                        />
                      )}
                      <span
                        translate="no"
                        className="min-w-0 flex-1 basis-32 truncate font-mono text-xs text-fx-text-2"
                        title={selected.originalName ? `${selected.originalName} (se sube como ${selected.name})` : selected.name}
                      >
                        {/* Adjunto de la PC (D4 B): el nombre original manda; el generado, que es
                            el que se sube y figura en el informe, va debajo. */}
                        {selected.originalName ?? selected.name}
                        {selected.originalName && (
                          <span className="block truncate text-[11px] text-fx-text-3">Se sube como {selected.name}</span>
                        )}
                        {selected.sourcePath && (
                          <span className="block truncate text-[11px] text-fx-text-3">{selected.sourcePath}</span>
                        )}
                      </span>
                      <Button type="button" text severity="danger" size="small"
                        icon={<Trash2 className="h-3.5 w-3.5" aria-hidden="true" />}
                        aria-label={`Eliminar ${selected.originalName ?? selected.name}`}
                        onClick={() => removeItem(selected)}
                        disabled={uploading} />
                    </div>
                  );
                })()}
              </>
            )}
          </div>
        </div>
      </div>

      {/* ── Envío en curso: progreso real + cancelar; aviso de cancelación ── */}
      <UploadLiveRegion progress={uploading ? uploadProgress : null} />
      {(uploadNotice || (uploading && uploadProgress)) && (
        <div className="mt-6 space-y-3">
          {uploadNotice && (
            <FxBanner tone="info" onClose={onDismissUploadNotice}>{uploadNotice.text}</FxBanner>
          )}
          {uploading && uploadProgress && (
            <UploadProgressPanel progress={uploadProgress} onCancel={() => onCancelUpload?.()} />
          )}
        </div>
      )}

      {/* ── Botonera del paso ── */}
      <StepActions className="mt-6 border-t border-fx-border pt-5">
        <Button
          type="button"
          size="large"
          icon={<UploadCloud className="h-4 w-4" aria-hidden="true" />}
          label={loading.upload ? "Enviando…" : "Enviar evidencia y continuar"}
          loading={!!loading.upload}
          onClick={onUploadAndContinue}
          className="w-full min-h-11 sm:ml-auto sm:w-auto sm:flex-none"
        />
      </StepActions>
    </>
  );
}

/* Default estable de `localBlobs` (un `{}` literal en el destructuring sería nuevo en cada render). */
const NO_LOCAL_BLOBS: Record<string, LocalBlobInfo> = {};
const NO_UPLOAD_STATES: Record<string, FileUploadState> = {};

/* Subtítulo de bloque del paso (misma convención que la parte 2). */
const LABEL = "m-0 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2";

/* Entrada sobria (solo con motion-safe). */
const ENTER = "motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]";

/* Tiles Pantalla / Grabar (botones propios, no Button de Prime). */
const TILE = cn(
  "flex min-h-[4.5rem] flex-col items-center justify-center gap-1.5 rounded-fx-lg border px-2 py-3.5",
  "text-fx-body-sm font-semibold transition-colors duration-fx-fast ease-fx",
  "disabled:cursor-not-allowed disabled:opacity-50",
  FOCUS_RING,
);
const TILE_IDLE = "border-fx-border-strong bg-fx-surface-2 text-fx-text enabled:hover:bg-fx-surface-3";
