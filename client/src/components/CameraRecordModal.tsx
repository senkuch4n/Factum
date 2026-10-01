"use client";

import { useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  X, Check, RotateCcw, AlertTriangle, RefreshCw, Monitor, Settings,
  Camera, Mic, Info, Circle, Square,
} from "lucide-react";

type Phase = "live" | "recording" | "preview" | "error";
type ErrKind = "NotAllowed" | "NotFound" | "InUse" | "Other";

interface Props {
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

export function CameraRecordModal({ onCapture, onClose }: Props) {
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

  // Escape cierra el modal — mismo comportamiento que ya tenía el click afuera
  // (línea onClick del overlay), así que no suma un riesgo nuevo de perder una
  // grabación en curso, solo lo hace alcanzable también por teclado.
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

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

  const guide = errKind ? ERROR_GUIDES[errKind] : null;

  return (
    <div
      className="webcam-overlay"
      onClick={e => { if (e.target === e.currentTarget) onClose(); }}
    >
      <motion.div
        className="w-full max-w-lg rounded-lg overflow-hidden"
        style={{ background: "#000", boxShadow: "0 32px 80px rgba(0,0,0,0.7)" }}
        initial={{ opacity: 0, scale: 0.93, y: 20 }}
        animate={{ opacity: 1, scale: 1,    y: 0  }}
        exit={{   opacity: 0, scale: 0.93, y: 20  }}
        transition={{ type: "spring", stiffness: 280, damping: 24 }}
      >
        {/* ── Close button ── */}
        {phase !== "recording" && (
          <div className="absolute top-3 right-3 z-20">
            <button
              onClick={onClose}
              className="w-9 h-9 rounded-full flex items-center justify-center backdrop-blur-sm transition-colors"
              style={{ background: "rgba(0,0,0,0.55)", border: "1px solid rgba(255,255,255,0.15)" }}
            >
              <X className="w-4 h-4 text-white" />
            </button>
          </div>
        )}

        {/* ── Label chip ── */}
        <div className="absolute top-3 left-3 z-20 flex items-center gap-1.5 px-2.5 py-1.5 rounded-full backdrop-blur-sm"
          style={{ background: "rgba(0,0,0,0.5)", border: "1px solid rgba(255,255,255,0.12)" }}
        >
          <Camera className="w-3.5 h-3.5 text-white" />
          <span className="text-white text-xs font-semibold">Filmar con cámara externa</span>
        </div>

        <AnimatePresence mode="wait">

          {/* ─── LIVE / RECORDING ─── */}
          {(phase === "live" || phase === "recording") && (
            <motion.div
              key="live"
              className="relative"
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            >
              <video
                ref={videoRef}
                autoPlay playsInline muted
                className="w-full block"
                style={{ maxHeight: "70vh", objectFit: "cover", background: "#111" }}
              />

              {/* Tip de uso (arriba, debajo del chip) */}
              <div className="absolute top-12 left-3 right-3 z-10 flex items-start gap-1.5 px-2.5 py-2 rounded-md backdrop-blur-sm"
                style={{ background: "rgba(245,158,11,0.18)", border: "1px solid rgba(245,158,11,0.35)" }}
              >
                <Info className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" style={{ color: "#f2b544" }} />
                <p className="text-[11px] leading-snug text-white/90">
                  Apoyá o apuntá la cámara al celular, reproducí el audio que necesitás capturar, y grabá.
                </p>
              </div>

              {/* Selectores de dispositivo (solo si hay más de una opción) */}
              {phase === "live" && (videoDevices.length > 1 || audioDevices.length > 1) && (
                <div className="absolute top-24 left-3 right-3 z-10 flex gap-2">
                  {videoDevices.length > 1 && (
                    <label className="flex-1 flex items-center gap-1.5 px-2 py-1.5 rounded-md text-[11px] backdrop-blur-sm"
                      style={{ background: "rgba(0,0,0,0.55)", border: "1px solid rgba(255,255,255,0.15)" }}
                    >
                      <Camera className="w-3 h-3 text-white/70 flex-shrink-0" />
                      <select
                        className="flex-1 bg-transparent text-white outline-none min-w-0 focus-visible:ring-2 focus-visible:ring-white/70 rounded"
                        value={videoDeviceId}
                        onChange={e => changeVideoDevice(e.target.value)}
                      >
                        {videoDevices.map(d => (
                          <option key={d.deviceId} value={d.deviceId} style={{ color: "#000" }}>
                            {d.label || "Cámara"}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                  {audioDevices.length > 1 && (
                    <label className="flex-1 flex items-center gap-1.5 px-2 py-1.5 rounded-md text-[11px] backdrop-blur-sm"
                      style={{ background: "rgba(0,0,0,0.55)", border: "1px solid rgba(255,255,255,0.15)" }}
                    >
                      <Mic className="w-3 h-3 text-white/70 flex-shrink-0" />
                      <select
                        className="flex-1 bg-transparent text-white outline-none min-w-0 focus-visible:ring-2 focus-visible:ring-white/70 rounded"
                        value={audioDeviceId}
                        onChange={e => changeAudioDevice(e.target.value)}
                      >
                        {audioDevices.map(d => (
                          <option key={d.deviceId} value={d.deviceId} style={{ color: "#000" }}>
                            {d.label || "Micrófono"}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                </div>
              )}

              {/* REC badge + cronómetro */}
              {phase === "recording" && (
                <div className="absolute top-12 right-3 z-10 flex items-center gap-1.5 px-2.5 py-1 rounded-full"
                  style={{ background: "rgba(220,38,38,0.9)" }}
                >
                  <motion.span
                    className="w-2 h-2 rounded-full bg-white"
                    animate={{ opacity: [1, 0.3, 1] }}
                    transition={{ duration: 1.1, repeat: Infinity }}
                  />
                  <span className="text-white text-[11px] font-bold tabular-nums">REC {fmtTime(elapsed)}</span>
                </div>
              )}

              {/* Loading */}
              {!videoReady && (
                <div className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-black/70">
                  <div className="w-9 h-9 rounded-full border-2 border-white/15 border-t-white animate-spin" />
                  <p className="text-white/60 text-xs">Iniciando cámara...</p>
                </div>
              )}

              {/* Controles */}
              <div
                className="absolute bottom-0 inset-x-0 flex items-center justify-between px-6 py-5"
                style={{ background: "linear-gradient(to top, rgba(0,0,0,0.75) 0%, transparent 100%)" }}
              >
                {phase === "live" ? (
                  <>
                    <button
                      onClick={onClose}
                      className="text-white/70 text-sm font-medium hover:text-white transition-colors py-2 px-3"
                    >
                      Cancelar
                    </button>
                    <motion.button
                      onClick={startRecording}
                      disabled={!videoReady}
                      whileTap={{ scale: 0.88 }}
                      whileHover={{ scale: 1.06 }}
                      className="relative"
                      style={{ opacity: videoReady ? 1 : 0.4, cursor: videoReady ? "pointer" : "not-allowed" }}
                    >
                      <div className="w-16 h-16 rounded-full flex items-center justify-center"
                        style={{ border: "3px solid rgba(255,255,255,0.8)" }}
                      >
                        <Circle className="w-11 h-11 fill-red-600 text-red-600" />
                      </div>
                    </motion.button>
                    <div className="w-20" />
                  </>
                ) : (
                  <>
                    <div className="flex-1" />
                    <motion.button
                      onClick={stopRecording}
                      whileTap={{ scale: 0.88 }}
                      whileHover={{ scale: 1.06 }}
                      className="relative"
                    >
                      <div className="w-16 h-16 rounded-full flex items-center justify-center"
                        style={{ border: "3px solid rgba(255,255,255,0.8)" }}
                      >
                        <Square className="w-9 h-9 fill-white text-white" />
                      </div>
                    </motion.button>
                    <div className="flex-1" />
                  </>
                )}
              </div>
            </motion.div>
          )}

          {/* ─── PREVIEW ─── */}
          {phase === "preview" && recordedURL && (
            <motion.div
              key="preview"
              className="relative"
              initial={{ opacity: 0, scale: 1.04 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0 }}
            >
              <video
                src={recordedURL}
                controls
                className="w-full block"
                style={{ maxHeight: "70vh", background: "#111" }}
              />

              <div className="absolute top-14 left-3 flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold backdrop-blur-sm"
                style={{ background: "rgba(16,185,129,0.25)", border: "1px solid rgba(16,185,129,0.5)", color: "#6ee7b7" }}
              >
                <Check className="w-3 h-3" /> Grabación lista — {fmtTime(elapsed)}
              </div>

              <div className="flex items-center justify-between gap-3 px-5 py-4" style={{ background: "var(--bg-surface)" }}>
                <motion.button
                  onClick={retake}
                  whileTap={{ scale: 0.96 }}
                  className="flex-1 flex items-center justify-center gap-2 py-2.5 rounded-md text-sm font-semibold"
                  style={{ background: "var(--btn-secondary-bg)", border: "1px solid var(--border-md)", color: "var(--text-secondary)" }}
                >
                  <RotateCcw className="w-4 h-4" /> Repetir
                </motion.button>
                <motion.button
                  onClick={confirm}
                  whileTap={{ scale: 0.96 }}
                  className="flex-1 flex items-center justify-center gap-2 py-2.5 rounded-md text-sm font-bold text-white"
                  style={{ background: "var(--green)", boxShadow: "0 1px 2px rgba(0,0,0,0.12)" }}
                >
                  <Check className="w-4 h-4" /> Usar este video
                </motion.button>
              </div>
            </motion.div>
          )}

          {/* ─── ERROR ─── */}
          {phase === "error" && guide && (
            <motion.div
              key="error"
              className="p-5 space-y-4"
              style={{ background: "var(--bg-surface)", minHeight: 240 }}
              initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
            >
              <div className="h-6" />

              <div className="flex items-start gap-3 p-3.5 rounded-md border border-red-500/20 bg-red-500/[0.07]">
                <AlertTriangle className="w-4 h-4 text-red-400 flex-shrink-0 mt-0.5" />
                <p className="font-semibold text-red-500 dark:text-red-300 text-sm">{guide.title}</p>
              </div>

              <div>
                <p className="section-label mb-2.5 flex items-center gap-1.5">
                  <Settings className="w-3 h-3" /> Cómo solucionarlo
                </p>
                <ol className="space-y-2">
                  {guide.steps.map((step, i) => (
                    <motion.li
                      key={i}
                      className="flex gap-3 text-sm"
                      style={{ color: "var(--text-secondary)" }}
                      initial={{ opacity: 0, x: -8 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{ delay: i * 0.07 }}
                    >
                      <span
                        className="w-5 h-5 rounded-full flex items-center justify-center text-xs font-bold flex-shrink-0 mt-0.5"
                        style={{ background: "rgba(13,148,136,0.12)", color: "var(--blue)", border: "1px solid rgba(13,148,136,0.2)" }}
                      >
                        {i + 1}
                      </span>
                      {step}
                    </motion.li>
                  ))}
                </ol>

                {guide.extra && (
                  <div className="mt-3 p-3 rounded-md text-xs flex gap-2" style={{ background: "var(--bg-elevated)", color: "var(--text-muted)" }}>
                    <Monitor className="w-3.5 h-3.5 flex-shrink-0 mt-0.5 text-teal-500" />
                    {guide.extra}
                  </div>
                )}
              </div>

              <button className="btn-primary w-full" onClick={init}>
                <RefreshCw className="w-4 h-4" /> Intentar de nuevo
              </button>

              <button className="btn-ghost w-full text-sm" onClick={onClose}>
                Cancelar
              </button>
            </motion.div>
          )}

        </AnimatePresence>
      </motion.div>
    </div>
  );
}
