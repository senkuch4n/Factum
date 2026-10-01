"use client";

import { useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { X, Check, RotateCcw, AlertTriangle, RefreshCw, Monitor, Settings } from "lucide-react";

type Phase = "live" | "captured" | "error";
type ErrKind = "NotAllowed" | "NotFound" | "InUse" | "Other";

interface Props {
  label: string;
  icon: React.ElementType;
  onCapture: (blob: Blob, filename: string) => void;
  onClose: () => void;
}

const ERROR_GUIDES: Record<ErrKind, { title: string; steps: string[]; extra?: string }> = {
  NotAllowed: {
    title: "Permiso de cámara denegado",
    steps: [
      'Hacé clic en el ícono de candado 🔒 en la barra de direcciones del navegador',
      'Buscá "Cámara" y cambiá a "Permitir"',
      "Recargá la página con F5 e intentá de nuevo",
    ],
    extra: "Si el navegador no muestra el popup de permiso, andá a Ajustes del navegador → Privacidad → Permisos de cámara",
  },
  NotFound: {
    title: "No se encontró ninguna cámara",
    steps: [
      "Verificá que la webcam esté enchufada correctamente",
      "Si es USB, desenchufala y volvé a enchufar",
      "Abrí el Administrador de dispositivos y verificá que no tenga errores",
      "Probá con otro puerto USB",
    ],
    extra: "Si la PC tiene cámara integrada, verificá que no esté deshabilitada en la BIOS",
  },
  InUse: {
    title: "La cámara está siendo usada por otra aplicación",
    steps: [
      "Cerrá Zoom, Teams, Skype u otras apps que usen la cámara",
      "Si la usaste en otra pestaña del navegador, cerrá esa pestaña",
      "Intentá de nuevo",
    ],
  },
  Other: {
    title: "Error al acceder a la cámara",
    steps: [
      "Actualizá el navegador a la última versión",
      "Verificá que el sitio esté en HTTPS o localhost",
      "Probá en otro navegador (Chrome o Edge)",
      "Reiniciá el navegador",
    ],
  },
};

export function WebcamCaptureModal({ label, icon: Icon, onCapture, onClose }: Props) {
  const videoRef  = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const streamRef = useRef<MediaStream | null>(null);

  const [phase,        setPhase]        = useState<Phase>("live");
  const [errKind,      setErrKind]      = useState<ErrKind | null>(null);
  const [capturedURL,  setCapturedURL]  = useState<string | null>(null);
  const [capturedBlob, setCapturedBlob] = useState<Blob | null>(null);
  const [videoReady,   setVideoReady]   = useState(false);

  function startStream() {
    setPhase("live");
    setErrKind(null);
    setVideoReady(false);
    navigator.mediaDevices
      .getUserMedia({ video: { width: { ideal: 1280 }, height: { ideal: 720 }, facingMode: "user" } })
      .then(stream => {
        streamRef.current = stream;
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
          videoRef.current.onloadedmetadata = () => setVideoReady(true);
        }
      })
      .catch(err => {
        const name = (err as DOMException).name;
        if (name === "NotAllowedError" || name === "PermissionDeniedError") setErrKind("NotAllowed");
        else if (name === "NotFoundError" || name === "DevicesNotFoundError") setErrKind("NotFound");
        else if (name === "NotReadableError" || name === "TrackStartError") setErrKind("InUse");
        else setErrKind("Other");
        setPhase("error");
      });
  }

  function stopStream() {
    streamRef.current?.getTracks().forEach(t => t.stop());
    streamRef.current = null;
  }

  useEffect(() => {
    startStream();
    return () => {
      stopStream();
      if (capturedURL) URL.revokeObjectURL(capturedURL);
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function capture() {
    const video  = videoRef.current;
    const canvas = canvasRef.current;
    if (!video || !canvas || !videoReady) return;
    canvas.width  = video.videoWidth  || 1280;
    canvas.height = video.videoHeight || 720;
    canvas.getContext("2d")?.drawImage(video, 0, 0);
    canvas.toBlob(blob => {
      if (!blob) return;
      stopStream();
      const url = URL.createObjectURL(blob);
      setCapturedBlob(blob);
      setCapturedURL(url);
      setPhase("captured");
    }, "image/jpeg", 0.93);
  }

  function retake() {
    if (capturedURL) URL.revokeObjectURL(capturedURL);
    setCapturedURL(null);
    setCapturedBlob(null);
    startStream();
  }

  function confirm() {
    if (!capturedBlob) return;
    const d  = new Date();
    const ts = `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, "0")}${String(d.getDate()).padStart(2, "0")}_${String(d.getHours()).padStart(2, "0")}${String(d.getMinutes()).padStart(2, "0")}${String(d.getSeconds()).padStart(2, "0")}`;
    onCapture(capturedBlob, `foto_${label.replace(/\s+/g, "_")}_${ts}.jpg`);
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
        {/* ── Close button (always visible, top-right) ── */}
        <div className="absolute top-3 right-3 z-20">
          <button
            onClick={onClose}
            aria-label="Cerrar"
            className="w-9 h-9 rounded-full flex items-center justify-center backdrop-blur-sm transition-colors"
            style={{ background: "rgba(0,0,0,0.55)", border: "1px solid rgba(255,255,255,0.15)" }}
          >
            <X className="w-4 h-4 text-white" />
          </button>
        </div>

        {/* ── Label chip (top-left) ── */}
        <div className="absolute top-3 left-3 z-20 flex items-center gap-1.5 px-2.5 py-1.5 rounded-full backdrop-blur-sm"
          style={{ background: "rgba(0,0,0,0.5)", border: "1px solid rgba(255,255,255,0.12)" }}
        >
          <Icon className="w-3.5 h-3.5 text-white" aria-hidden="true" />
          <span className="text-white text-xs font-semibold">{label}</span>
        </div>

        <AnimatePresence mode="wait">

          {/* ─── LIVE ─── */}
          {phase === "live" && (
            <motion.div
              key="live"
              className="relative"
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            >
              {/* Video */}
              <video
                ref={videoRef}
                autoPlay playsInline muted
                className="w-full block"
                style={{ maxHeight: "70vh", objectFit: "cover", background: "#111" }}
              />

              {/* Grid overlay */}
              <div className="absolute inset-0 pointer-events-none" style={{
                backgroundImage: "linear-gradient(rgba(255,255,255,0.035) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.035) 1px, transparent 1px)",
                backgroundSize: "33.3% 33.3%",
              }} />

              {/* Crosshair */}
              <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
                <div className="w-8 h-px bg-white/20" />
                <div className="absolute w-px h-8 bg-white/20" />
              </div>

              {/* Corner brackets */}
              {[
                "top-3 left-3",
                "top-3 right-3",
                "bottom-16 left-3",
                "bottom-16 right-3",
              ].map((pos, i) => (
                <svg key={i} width="20" height="20" viewBox="0 0 20 20"
                  className={`absolute ${pos} pointer-events-none`}
                  style={{
                    transform: `rotate(${[0, 90, 270, 180][i]}deg)`,
                    opacity: 0.6,
                  }}
                >
                  <path d="M0 12 L0 0 L12 0" fill="none" stroke="white" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              ))}

              {/* Loading */}
              {!videoReady && (
                <div className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-black/70">
                  <div className="w-9 h-9 rounded-full border-2 border-white/15 border-t-white animate-spin" />
                  <p className="text-white/60 text-xs">Iniciando cámara...</p>
                </div>
              )}

              {/* ── Controls bar overlaid at bottom ── */}
              <div
                className="absolute bottom-0 inset-x-0 flex items-center justify-between px-6 py-5"
                style={{ background: "linear-gradient(to top, rgba(0,0,0,0.75) 0%, transparent 100%)" }}
              >
                {/* Cancel */}
                <button
                  onClick={onClose}
                  className="text-white/70 text-sm font-medium hover:text-white transition-colors py-2 px-3"
                >
                  Cancelar
                </button>

                {/* Shutter */}
                <motion.button
                  onClick={capture}
                  disabled={!videoReady}
                  whileTap={{ scale: 0.88 }}
                  whileHover={{ scale: 1.06 }}
                  className="relative"
                  style={{ opacity: videoReady ? 1 : 0.4, cursor: videoReady ? "pointer" : "not-allowed" }}
                >
                  {/* Outer ring */}
                  <div className="w-16 h-16 rounded-full flex items-center justify-center"
                    style={{ border: "3px solid rgba(255,255,255,0.8)" }}
                  >
                    {/* Inner white circle */}
                    <div className="w-12 h-12 rounded-full bg-white" />
                  </div>
                </motion.button>

                {/* Spacer to balance layout */}
                <div className="w-20" />
              </div>
            </motion.div>
          )}

          {/* ─── CAPTURED ─── */}
          {phase === "captured" && capturedURL && (
            <motion.div
              key="captured"
              className="relative"
              initial={{ opacity: 0, scale: 1.04 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0 }}
            >
              <img
                src={capturedURL}
                alt="Captura"
                className="w-full block"
                style={{ maxHeight: "70vh", objectFit: "cover" }}
              />

              {/* "Previsualización" chip */}
              <div className="absolute top-14 left-3 flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold backdrop-blur-sm"
                style={{ background: "rgba(16,185,129,0.25)", border: "1px solid rgba(16,185,129,0.5)", color: "#6ee7b7" }}
              >
                <Check className="w-3 h-3" /> Previsualización
              </div>

              {/* Action bar overlaid at bottom */}
              <div
                className="absolute bottom-0 inset-x-0 flex items-center justify-between gap-3 px-5 py-5"
                style={{ background: "linear-gradient(to top, rgba(0,0,0,0.85) 0%, rgba(0,0,0,0.4) 80%, transparent 100%)" }}
              >
                <motion.button
                  onClick={retake}
                  whileTap={{ scale: 0.96 }}
                  className="flex-1 flex items-center justify-center gap-2 py-2.5 rounded-md text-sm font-semibold text-white"
                  style={{ background: "rgba(255,255,255,0.12)", border: "1px solid rgba(255,255,255,0.2)" }}
                >
                  <RotateCcw className="w-4 h-4" /> Repetir
                </motion.button>
                <motion.button
                  onClick={confirm}
                  whileTap={{ scale: 0.96 }}
                  className="flex-1 flex items-center justify-center gap-2 py-2.5 rounded-md text-sm font-bold text-white"
                  style={{ background: "var(--green)", boxShadow: "0 1px 2px rgba(0,0,0,0.12)" }}
                >
                  <Check className="w-4 h-4" /> Usar esta foto
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
              {/* Spacer for close button */}
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

              <button className="btn-primary w-full" onClick={startStream}>
                <RefreshCw className="w-4 h-4" /> Intentar de nuevo
              </button>

              <button className="btn-ghost w-full text-sm" onClick={onClose}>
                Omitir foto
              </button>
            </motion.div>
          )}

        </AnimatePresence>

        {/* Hidden canvas */}
        <canvas ref={canvasRef} className="hidden" />
      </motion.div>
    </div>
  );
}
