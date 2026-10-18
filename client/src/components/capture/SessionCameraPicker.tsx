"use client";

import { useEffect, useId, useState } from "react";
import { Button } from "primereact/button";
import { Checkbox } from "primereact/checkbox";
import { Dropdown } from "primereact/dropdown";
import { Check, Loader2, RefreshCw, Video, VideoOff } from "lucide-react";
import { agent, type CameraDevice } from "@/lib/agent";
import { cameraFailText } from "@/lib/recording-session";
import type { SessionCameraPref } from "@/lib/session-camera-pref";
import type { CameraPreviewProblem } from "@/hooks/useRecordingSession";
import { useMediaQuery } from "@/hooks/useMediaQuery";
import { cn } from "@/lib/utils";

/** Refresco de la vista previa (F2 §9.3). */
const PREVIEW_REFRESH_MS = 1500;
/**
 * Sin tocar el bloque, el refresco automático se pausa antes de los 120 s de inactividad de
 * Tatana (`CameraPreviewIdleSeconds`): así la previa se corta sola y la luz de la cámara se
 * apaga (QA §16.2 paso 3). Grabando, no se pausa (la imagen sale de la grabación).
 */
const PREVIEW_AUTO_PAUSE_MS = 110_000;

export interface SessionCameraPickerProps {
  id: string;
  cameras: CameraDevice[] | null;
  camerasError: string | null;
  selected: SessionCameraPref | null;
  withMic: boolean;
  previewOn: boolean;
  previewProblem: CameraPreviewProblem | null;
  /** La cámara de la sesión está grabando: desplegable y micrófono bloqueados. */
  recording: boolean;
  /** La sesión permite sumar la cámara (`recording`/`rotating`/`waiting_device`). */
  sessionReady: boolean;
  busy: boolean;
  disabled?: boolean;
  onSelect: (cam: SessionCameraPref) => void;
  onWithMic: (on: boolean) => void;
  onStart: () => void;
  onRefreshList: () => Promise<void>;
  onRefreshPreview: () => Promise<void>;
}

/**
 * Cámara del procedimiento (grabacion-sesion-completa-f2 §9.3): elegir la cámara de la PC,
 * encuadrar con una vista previa chica y empezar a grabarla junto con la pantalla.
 * Sin animaciones propias: la imagen se reemplaza en el lugar (espacio reservado con
 * `aspect-video`, sin saltos de layout).
 */
export function SessionCameraPicker({
  id, cameras, camerasError, selected, withMic, previewOn, previewProblem, recording, sessionReady, busy,
  disabled = false, onSelect, onWithMic, onStart, onRefreshList, onRefreshPreview,
}: SessionCameraPickerProps) {
  const selectId = useId();
  const micId = useId();
  const privacyId = useId();
  const previewHelpId = useId();
  const reducedMotion = useMediaQuery("(prefers-reduced-motion: reduce)");
  const [listBusy, setListBusy] = useState(false);
  const [src, setSrc] = useState<string | null>(null);
  const [imgOk, setImgOk] = useState(false);
  const [lastTouch, setLastTouch] = useState(() => Date.now());
  const [paused, setPaused] = useState(false);

  const showPreview = recording || previewOn;
  const touch = () => { setLastTouch(Date.now()); setPaused(false); };

  // Imagen nueva cada 1,5 s mientras haya previa (salvo "Reducir movimiento" o en pausa).
  useEffect(() => {
    if (!showPreview) { setSrc(null); setImgOk(false); return; }
    setSrc(agent.cameraPreviewURL());
    if (reducedMotion) return;
    const t = setInterval(() => {
      if (document.visibilityState !== "visible") return;
      if (!recording && Date.now() - lastTouch > PREVIEW_AUTO_PAUSE_MS) { setPaused(true); return; }
      setSrc(agent.cameraPreviewURL());
    }, PREVIEW_REFRESH_MS);
    return () => clearInterval(t);
  }, [showPreview, reducedMotion, recording, lastTouch]);

  const noCameras = cameras !== null && cameras.length === 0;
  const options = (cameras ?? []).map(c => ({ label: c.name, value: c.id }));
  // La cámara en uso aparece aunque ya no esté en la lista (desenchufada, lista sin actualizar).
  if (selected && !options.some(o => o.value === selected.id)) options.unshift({ label: selected.name, value: selected.id });
  const canStart = !!selected && sessionReady && !busy && !disabled && !noCameras;
  const manualRefresh = reducedMotion || paused;

  async function refreshList() {
    if (listBusy) return;
    touch();
    setListBusy(true);
    try { await onRefreshList(); } finally { setListBusy(false); }
  }

  async function refreshView() {
    touch();
    // En pausa (o sin previa), Tatana pudo cortarla: se vuelve a pedir antes de recargar la imagen.
    if (!recording && (paused || !imgOk)) await onRefreshPreview();
    setSrc(agent.cameraPreviewURL());
  }

  return (
    <div id={id} className="space-y-3 rounded-fx-md border border-fx-border bg-fx-surface-2 p-3">
      {/* Cámara */}
      <div className="space-y-1.5">
        <label htmlFor={selectId} className="block text-xs font-medium text-fx-text-2">Cámara</label>
        <div className="flex items-stretch gap-2">
          <Dropdown
            inputId={selectId}
            value={selected?.id ?? null}
            options={options}
            optionLabel="label"
            optionValue="value"
            placeholder={cameras === null ? "Buscando cámaras…" : "Elegí una cámara"}
            onChange={e => {
              const cam = (cameras ?? []).find(c => c.id === e.value);
              if (cam) { touch(); onSelect({ id: cam.id, name: cam.name }); }
            }}
            disabled={recording || disabled || noCameras || cameras === null}
            aria-describedby={recording ? `${id}-in-use` : undefined}
            className="min-w-0 flex-1"
          />
          <Button
            type="button"
            size="small"
            outlined
            severity="secondary"
            icon={listBusy
              ? <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
              : <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
            label="Actualizar lista"
            onClick={() => { void refreshList(); }}
            disabled={recording || disabled}
            aria-busy={listBusy || undefined}
            className="min-h-11 shrink-0 sm:min-h-0"
          />
        </div>
        {noCameras && (
          <p role="status" className="m-0 text-xs text-fx-text-2">
            {camerasError ?? "No se encontró ninguna cámara en esta PC."}
          </p>
        )}
      </div>

      {/* Vista previa: espacio reservado (16:9) para que el bloque no salte al llegar la imagen. */}
      {!noCameras && (
        <div className="space-y-1.5">
          <div className="relative aspect-video w-full max-w-[320px] overflow-hidden rounded-fx-md border border-fx-border bg-fx-surface-3">
            {previewProblem && !recording ? (
              <div className="flex h-full items-center gap-2 p-3 text-xs text-fx-text-2">
                <VideoOff className="h-4 w-4 shrink-0 text-fx-warning" aria-hidden="true" />
                <p role="status" className="m-0">
                  {cameraFailText(previewProblem.detail, { preview: true })}
                  {!previewProblem.detail && previewProblem.error ? ` ${previewProblem.error}` : ""}
                </p>
              </div>
            ) : showPreview && src ? (
              <>
                {/* eslint-disable-next-line @next/next/no-img-element -- imagen local de Tatana que cambia cada 1,5 s */}
                <img
                  src={src}
                  alt="Vista previa de la cámara del procedimiento"
                  width={320}
                  height={180}
                  onLoad={() => setImgOk(true)}
                  onError={() => setImgOk(false)}
                  className={cn("h-full w-full object-cover", !imgOk && "invisible")}
                />
                {!imgOk && (
                  <span className="absolute inset-0 flex items-center justify-center gap-1.5 text-xs text-fx-text-3">
                    <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
                    Esperando la imagen de la cámara…
                  </span>
                )}
              </>
            ) : (
              <span className="flex h-full items-center justify-center gap-1.5 text-xs text-fx-text-3">
                <Video className="h-3.5 w-3.5" aria-hidden="true" />
                {selected ? "Abriendo la vista previa…" : "Elegí una cámara para ver la vista previa"}
              </span>
            )}
          </div>
          {showPreview && manualRefresh && !previewProblem && (
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                size="small"
                text
                severity="secondary"
                icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                label="Actualizar vista"
                onClick={() => { void refreshView(); }}
                aria-describedby={paused ? previewHelpId : undefined}
                className="min-h-11 sm:min-h-0"
              />
              {paused && !reducedMotion && (
                <span id={previewHelpId} className="text-xs text-fx-text-3">Vista previa en pausa para no dejar la cámara encendida.</span>
              )}
            </div>
          )}
        </div>
      )}

      <p id={privacyId} className="m-0 text-xs text-fx-text-2">
        Encuadrá las manos y el celular, no las caras. La cámara graba sin audio salvo que marques «Con micrófono de la PC».
      </p>

      <div className="flex items-start gap-2.5">
        <Checkbox
          inputId={micId}
          checked={withMic}
          onChange={e => { touch(); onWithMic(!!e.checked); }}
          disabled={recording || disabled}
          icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />}
          aria-describedby={privacyId}
          className="mt-0.5"
        />
        <label htmlFor={micId} className={cn("text-fx-body-sm text-fx-text", recording || disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer")}>
          Con micrófono de la PC
        </label>
      </div>

      {recording ? (
        <p id={`${id}-in-use`} role="status" className="m-0 flex items-center gap-1.5 text-xs font-medium text-fx-text">
          <Video className="h-3.5 w-3.5 shrink-0 text-fx-danger" aria-hidden="true" />
          La cámara está grabando el procedimiento.
        </p>
      ) : (
        <Button
          type="button"
          size="small"
          icon={busy
            ? <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
            : <Video className="h-3.5 w-3.5" aria-hidden="true" />}
          label={busy ? "Abriendo la cámara…" : "Empezar a grabar la cámara"}
          onClick={() => { if (canStart) onStart(); }}
          disabled={!selected || !sessionReady || disabled || noCameras}
          aria-disabled={busy || undefined}
          aria-busy={busy || undefined}
          className={cn("min-h-11 w-full sm:min-h-0", busy && "cursor-not-allowed opacity-60")}
        />
      )}
    </div>
  );
}
