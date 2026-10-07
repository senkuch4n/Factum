"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import { Dropdown } from "primereact/dropdown";
import { Check, RotateCcw, RefreshCw, Camera, Mic, Info, Loader2 } from "lucide-react";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { MEDIA_ACTIONS, MediaErrorGuide } from "./capture/MediaErrorGuide";
import { FxMediaDialog } from "./overlay/FxMediaDialog";

type Phase = "live" | "recording" | "preview" | "error";
type ErrKind = "NotAllowed" | "NotFound" | "InUse" | "Other";

interface Props {
  open: boolean;
  onCapture: (blob: Blob, filename: string) => void;
  onClose: () => void;
}

const MAX_SECONDS = 600; // 10 min — corte automático para evitar archivos gigantes por olvido

const ERROR_GUIDES: Record<ErrKind, { title: string; steps: string[]; extra?: string }> = {
  NotAllowed: {
    title: "Permiso de cámara/micrófono denegado",
    steps: [
      'Hacé clic en el ícono de candado 🔒 en la barra de direcciones del navegador',
      'Buscá "Cámara" y "Micrófono" y cambiá ambos a "Permitir"',
      "Recargá la página con F5 e intentá de nuevo",
    ],
    extra: "Si el navegador no muestra el popup de permiso, andá a Ajustes del navegador → Privacidad → Permisos de cámara/micrófono",
  },
  NotFound: {
    title: "No se encontró cámara o micrófono",
    steps: [
      "Verificá que la cámara/webcam esté enchufada correctamente",
      "Si es USB, desenchufala y volvé a enchufar",
      "Probá con otro puerto USB",
    ],
  },
  InUse: {
    title: "La cámara o el micrófono están siendo usados por otra aplicación",
    steps: [
      "Cerrá Zoom, Teams, Skype u otras apps que usen la cámara o el micrófono",
      "Si los usaste en otra pestaña del navegador, cerrá esa pestaña",
      "Intentá de nuevo",
    ],
  },
  Other: {
    title: "Error al acceder a la cámara/micrófono",
    steps: [
      "Actualizá el navegador a la última versión",
      "Verificá que el sitio esté en HTTPS o localhost",
      "Probá en otro navegador (Chrome o Edge)",
    ],
  },
};

function pickMimeType(): string | undefined {
  const candidates = ["video/webm;codecs=vp9,opus", "video/webm;codecs=vp8,opus", "video/webm"];
  return candidates.find(c => typeof MediaRecorder !== "undefined" && MediaRecorder.isTypeSupported(c));
}

function fmtTime(sec: number) {
  const m = Math.floor(sec / 60).toString().padStart(2, "0");
  const s = (sec % 60).toString().padStart(2, "0");
  return `${m}:${s}`;
}

const RECORD_BUTTON = cn(
  "relative flex h-16 w-16 items-center justify-center rounded-full border-[3px] border-fx-accent",
  "transition-transform duration-fx-fast ease-fx motion-safe:enabled:active:scale-95",
  "disabled:cursor-not-allowed disabled:opacity-40",
  FOCUS_RING,
);

const SUCCESS_CHIP =
  "absolute left-3 top-3 inline-flex items-center gap-1.5 rounded-full border border-fx-success bg-fx-success-soft px-2.5 py-1 text-xs font-semibold text-fx-success";

/**
 * Filmar con una cámara externa (respaldo cuando el audio digital no se puede
 * capturar). El componente público es solo el diálogo; el `Body` (stream,
 * MediaRecorder, cronómetro) se monta al abrir y se desmonta al terminar la
 * salida. Mientras graba no hay X ni Escape (DP4 A), para no perder la
 * grabación por accidente; la máscara nunca cierra.
 */
export function CameraRecordModal({ open, onCapture, onClose }: Props) {
  const [recording, setRecording] = useState(false);
  return (
    <FxMediaDialog
      visible={open}
      onHide={onClose}
      title="Filmar con cámara externa"
      icon={<Camera className="h-4 w-4" aria-hidden="true" />}
      closable={!recording}
    >
      <CameraRecordBody
        onCapture={onCapture}
        onClose={onClose}
        onPhaseChange={p => setRecording(p === "recording")}
      />
    </FxMediaDialog>
  );
}

function CameraRecordBody({
  onCapture, onClose, onPhaseChange,
}: Omit<Props, "open"> & { onPhaseChange: (phase: Phase) => void }) {
  const videoRef    = useRef<HTMLVideoElement>(null);
  const streamRef   = useRef<MediaStream | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef   = useRef<BlobPart[]>([]);
  const timerRef    = useRef<ReturnType<typeof setInterval> | null>(null);

  const [phase, setPhase]           = useState<Phase>("live");
  const [errKind, setErrKind]       = useState<ErrKind | null>(null);
  const [videoReady, setVideoReady] = useState(false);
  const [elapsed, setElapsed]       = useState(0);

  const [videoDevices, setVideoDevices] = useState<MediaDeviceInfo[]>([]);
  const [audioDevices, setAudioDevices] = useState<MediaDeviceInfo[]>([]);
  const [videoDeviceId, setVideoDeviceId] = useState<string>("");
  const [audioDeviceId, setAudioDeviceId] = useState<string>("");

  const [recordedURL, setRecordedURL]   = useState<string | null>(null);
  const [recordedBlob, setRecordedBlob] = useState<Blob | null>(null);
  const mimeTypeRef = useRef<string | undefined>(undefined);

  function stopStream() {
    streamRef.current?.getTracks().forEach(t => t.stop());
    streamRef.current = null;
  }

  function classifyError(err: unknown): ErrKind {
    const name = (err as DOMException)?.name;
    if (name === "NotAllowedError" || name === "PermissionDeniedError") return "NotAllowed";
    if (name === "NotFoundError" || name === "DevicesNotFoundError") return "NotFound";
    if (name === "NotReadableError" || name === "TrackStartError") return "InUse";
    return "Other";
  }

  async function startStream(vId?: string, aId?: string) {
    setVideoReady(false);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: vId ? { deviceId: { exact: vId } } : { width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: aId ? { deviceId: { exact: aId } } : true,
      });
      streamRef.current = stream;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        videoRef.current.onloadedmetadata = () => setVideoReady(true);
      }
      setPhase("live");
      setErrKind(null);
    } catch (err) {
      setErrKind(classifyError(err));
      setPhase("error");
    }
  }

  async function init() {
    setPhase("live");
    setErrKind(null);
    setVideoReady(false);
    try {
      // Pedimos acceso genérico primero — hasta no tener permiso, enumerateDevices()
      // devuelve los dispositivos sin nombre (label vacío).
      const tempStream = await navigator.mediaDevices.getUserMedia({ video: true, audio: true });
      const devices = await navigator.mediaDevices.enumerateDevices();
      const vids = devices.filter(d => d.kind === "videoinput");
      const auds = devices.filter(d => d.kind === "audioinput");
      setVideoDevices(vids);
      setAudioDevices(auds);
      const vId = vids[0]?.deviceId ?? "";
      const aId = auds[0]?.deviceId ?? "";
      setVideoDeviceId(vId);
      setAudioDeviceId(aId);
      tempStream.getTracks().forEach(t => t.stop());
      await startStream(vId, aId);
    } catch (err) {
      setErrKind(classifyError(err));
      setPhase("error");
    }
  }

  useEffect(() => {
    init();
    return () => {
      stopStream();
      if (timerRef.current) clearInterval(timerRef.current);
      if (recordedURL) URL.revokeObjectURL(recordedURL);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function changeVideoDevice(id: string) {
    setVideoDeviceId(id);
    stopStream();
    startStream(id, audioDeviceId);
  }

  function changeAudioDevice(id: string) {
    setAudioDeviceId(id);
    stopStream();
    startStream(videoDeviceId, id);
  }

  function startRecording() {
    const stream = streamRef.current;
    if (!stream || !videoReady) return;
    const mimeType = pickMimeType();
    mimeTypeRef.current = mimeType;
    chunksRef.current = [];
    const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
    recorder.ondataavailable = e => { if (e.data.size > 0) chunksRef.current.push(e.data); };
    recorder.onstop = () => {
      const blob = new Blob(chunksRef.current, { type: mimeType || "video/webm" });
      const url  = URL.createObjectURL(blob);
      setRecordedBlob(blob);
      setRecordedURL(url);
      setPhase("preview");
    };
    recorder.start(1000);
    recorderRef.current = recorder;
    setElapsed(0);
    timerRef.current = setInterval(() => {
      setElapsed(e => {
        if (e + 1 >= MAX_SECONDS) { stopRecording(); return e; }
        return e + 1;
      });
    }, 1000);
    setPhase("recording");
  }

  function stopRecording() {
    recorderRef.current?.stop();
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null; }
    stopStream();
  }

  function retake() {
    if (recordedURL) URL.revokeObjectURL(recordedURL);
    setRecordedURL(null);
    setRecordedBlob(null);
    startStream(videoDeviceId, audioDeviceId);
  }

  function confirm() {
    if (!recordedBlob) return;
    const d  = new Date();
    const ts = `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, "0")}${String(d.getDate()).padStart(2, "0")}_${String(d.getHours()).padStart(2, "0")}${String(d.getMinutes()).padStart(2, "0")}${String(d.getSeconds()).padStart(2, "0")}`;
    onCapture(recordedBlob, `grabacion_camara_${ts}.webm`);
  }

  useEffect(() => { onPhaseChange(phase); }, [phase]); // eslint-disable-line react-hooks/exhaustive-deps

  const guide = errKind ? ERROR_GUIDES[errKind] : null;
  const showSelectors = phase === "live" && (videoDevices.length > 1 || audioDevices.length > 1);

  return (
    <>
      {(phase === "live" || phase === "recording") && (
        <>
          <p className="m-3 flex items-start gap-2 rounded-fx-md border border-fx-info bg-fx-info-soft px-3 py-2 text-xs text-fx-text">
            <Info className="mt-px h-3.5 w-3.5 shrink-0 text-fx-info" aria-hidden="true" />
            Apoyá o apuntá la cámara al celular, reproducí el audio que necesitás capturar, y grabá.
          </p>

          <div className="relative bg-fx-bg">
            {/* contenido de imagen */}
            <video ref={videoRef} autoPlay playsInline muted className="block max-h-[55vh] w-full object-cover" />

            {phase === "recording" && (
              <div
                role="timer"
                aria-label="Tiempo de grabación"
                className="absolute right-3 top-3 inline-flex items-center gap-1.5 rounded-full bg-fx-danger-fill px-2.5 py-1 text-xs font-bold tabular-nums text-fx-on-danger"
              >
                <span className="h-2 w-2 rounded-full bg-fx-on-danger motion-safe:animate-pulse" aria-hidden="true" />
                REC {fmtTime(elapsed)}
              </div>
            )}

            {!videoReady && (
              <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-fx-overlay">
                <Loader2 className="h-8 w-8 animate-spin text-fx-text" aria-hidden="true" />
                <p className="m-0 text-xs text-fx-text">Iniciando cámara…</p>
              </div>
            )}
          </div>

          {showSelectors && (
            <div className="grid gap-2 px-3 pb-3 pt-3 sm:grid-cols-2">
              {videoDevices.length > 1 && (
                <div className="min-w-0">
                  <label htmlFor="camrec-video" className="mb-1 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
                    <Camera className="h-3.5 w-3.5" aria-hidden="true" /> Cámara
                  </label>
                  <Dropdown
                    inputId="camrec-video"
                    value={videoDeviceId}
                    onChange={e => changeVideoDevice(e.value)}
                    options={videoDevices.map(d => ({ label: d.label || "Cámara", value: d.deviceId }))}
                    panelClassName="dark"
                    className="w-full"
                  />
                </div>
              )}
              {audioDevices.length > 1 && (
                <div className="min-w-0">
                  <label htmlFor="camrec-audio" className="mb-1 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
                    <Mic className="h-3.5 w-3.5" aria-hidden="true" /> Micrófono
                  </label>
                  <Dropdown
                    inputId="camrec-audio"
                    value={audioDeviceId}
                    onChange={e => changeAudioDevice(e.value)}
                    options={audioDevices.map(d => ({ label: d.label || "Micrófono", value: d.deviceId }))}
                    panelClassName="dark"
                    className="w-full"
                  />
                </div>
              )}
            </div>
          )}

          <div className={MEDIA_ACTIONS}>
            {phase === "live" ? (
              <>
                <div className="flex-1">
                  <Button type="button" text severity="secondary" label="Cancelar" onClick={onClose} className="min-h-11" />
                </div>
                <button type="button" onClick={startRecording} disabled={!videoReady} aria-label="Iniciar grabación" className={RECORD_BUTTON}>
                  <span className="h-11 w-11 rounded-full bg-fx-danger-fill" aria-hidden="true" />
                </button>
                {/* Espaciador: centra el botón de grabar */}
                <span className="flex-1" aria-hidden="true" />
              </>
            ) : (
              <button type="button" onClick={stopRecording} aria-label="Detener grabación" className={cn(RECORD_BUTTON, "mx-auto")}>
                <span className="h-7 w-7 rounded-fx-sm bg-fx-text" aria-hidden="true" />
              </button>
            )}
          </div>
        </>
      )}

      {phase === "preview" && recordedURL && (
        <>
          <div className="relative bg-fx-bg motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
            {/* contenido de imagen */}
            <video src={recordedURL} controls className="block max-h-[55vh] w-full" />
            <span className={SUCCESS_CHIP}>
              <Check className="h-3 w-3" aria-hidden="true" /> Grabación lista — {fmtTime(elapsed)}
            </span>
          </div>
          <div className={MEDIA_ACTIONS}>
            <Button type="button" severity="secondary" icon={<RotateCcw className="h-4 w-4" aria-hidden="true" />}
              label="Repetir" onClick={retake} className="flex-1 min-h-11" />
            <Button type="button" icon={<Check className="h-4 w-4" aria-hidden="true" />}
              label="Usar este video" onClick={confirm} className="flex-1 min-h-11" />
          </div>
        </>
      )}

      {phase === "error" && guide && (
        <>
          <MediaErrorGuide guide={guide} />
          <div className={MEDIA_ACTIONS}>
            <Button type="button" text severity="secondary" label="Cancelar" onClick={onClose} className="min-h-11" />
            <Button type="button" icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
              label="Intentar de nuevo" onClick={init} className="min-h-11" />
          </div>
        </>
      )}
    </>
  );
}
