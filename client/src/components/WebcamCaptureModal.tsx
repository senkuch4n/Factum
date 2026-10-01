"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import { Check, RotateCcw, RefreshCw, Loader2 } from "lucide-react";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { MEDIA_ACTIONS, MediaErrorGuide } from "./capture/MediaErrorGuide";
import { FxMediaDialog } from "./overlay/FxMediaDialog";

type Phase = "live" | "captured" | "error";
type ErrKind = "NotAllowed" | "NotFound" | "InUse" | "Other";

interface Props {
  open: boolean;
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

/** Esquinas del visor (decorativas). */
const CORNERS = [
  "top-3 left-3 rotate-0",
  "top-3 right-3 rotate-90",
  "bottom-3 left-3 -rotate-90",
  "bottom-3 right-3 rotate-180",
];

/**
 * Foto de identificación con la webcam. El componente público es solo el
 * diálogo; el `Body` (stream, captura, previsualización) se monta al abrir y
 * se desmonta al terminar la salida, así el cleanup apaga la cámara.
 * Escape y la X cierran en todas las fases; la máscara no (DP4 A).
 */
export function WebcamCaptureModal({ open, label, icon: Icon, onCapture, onClose }: Props) {
  return (
    <FxMediaDialog
      visible={open}
      onHide={onClose}
      title={label}
      icon={<Icon className="h-4 w-4" aria-hidden="true" />}
    >
      <WebcamBody key={label} label={label} onCapture={onCapture} onClose={onClose} />
    </FxMediaDialog>
  );
}

function WebcamBody({ label, onCapture, onClose }: Omit<Props, "open" | "icon">) {
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
    <>
      {phase === "live" && (
        <>
          <div className="relative bg-fx-bg">
            {/* contenido de imagen */}
            <video ref={videoRef} autoPlay playsInline muted className="block max-h-[60vh] w-full object-cover" />
            {CORNERS.map(pos => (
              <svg key={pos} width="20" height="20" viewBox="0 0 20 20" aria-hidden="true"
                className={cn("pointer-events-none absolute text-fx-text opacity-60", pos)}>
                <path d="M0 12 L0 0 L12 0" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            ))}
            {!videoReady && (
              <div role="status" className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-fx-overlay">
                <Loader2 className="h-8 w-8 animate-spin text-fx-text" aria-hidden="true" />
                <p className="m-0 text-xs text-fx-text">Iniciando cámara…</p>
              </div>
            )}
          </div>
          <div className={MEDIA_ACTIONS}>
            <div className="flex-1">
              <Button type="button" text severity="secondary" label="Cancelar" onClick={onClose} className="min-h-11" />
            </div>
            <button
              type="button"
              onClick={capture}
              disabled={!videoReady}
              aria-label="Tomar foto"
              className={cn(
                "relative flex h-16 w-16 items-center justify-center rounded-full border-[3px] border-fx-accent",
                "transition-transform duration-fx-fast ease-fx motion-safe:enabled:active:scale-95",
                "disabled:cursor-not-allowed disabled:opacity-40",
                FOCUS_RING,
              )}
            >
              <span className="h-12 w-12 rounded-full bg-fx-text" aria-hidden="true" />
            </button>
            {/* Espaciador: centra el obturador */}
            <span className="flex-1" aria-hidden="true" />
          </div>
        </>
      )}

      {phase === "captured" && capturedURL && (
        <>
          <div className="relative bg-fx-bg motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
            {/* contenido de imagen */}
            <img src={capturedURL} alt={`Previsualización: ${label}`} className="block max-h-[60vh] w-full object-cover" />
            <span className="absolute left-3 top-3 inline-flex items-center gap-1.5 rounded-full border border-fx-success bg-fx-success-soft px-2.5 py-1 text-xs font-semibold text-fx-success">
              <Check className="h-3 w-3" aria-hidden="true" /> Previsualización
            </span>
          </div>
          <div className={MEDIA_ACTIONS}>
            <Button type="button" severity="secondary" icon={<RotateCcw className="h-4 w-4" aria-hidden="true" />}
              label="Repetir" onClick={retake} className="flex-1 min-h-11" />
            <Button type="button" icon={<Check className="h-4 w-4" aria-hidden="true" />}
              label="Usar esta foto" onClick={confirm} className="flex-1 min-h-11" />
          </div>
        </>
      )}

      {phase === "error" && guide && (
        <>
          <MediaErrorGuide guide={guide} />
          <div className={MEDIA_ACTIONS}>
            <Button type="button" text severity="secondary" label="Omitir foto" onClick={onClose} className="min-h-11" />
            <Button type="button" icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
              label="Intentar de nuevo" onClick={startStream} className="min-h-11" />
          </div>
        </>
      )}

      {/* Canvas oculto para sacar el cuadro */}
      <canvas ref={canvasRef} className="hidden" />
    </>
  );
}
