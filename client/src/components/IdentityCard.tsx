"use client";

import { useState } from "react";
import { Button } from "primereact/button";
import { Camera, Check, Loader2, RotateCcw } from "lucide-react";
import { agentFileURL } from "@/lib/agent";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { Lightbox } from "./Lightbox";

/**
 * Fila de "roster" para la identificación de una persona (perito / titular):
 * avatar circular a la izquierda, nombre + rol + DNI en el medio y acción de
 * captura a la derecha. Va dentro de un `<li>` de una lista con `divide-y`.
 */
export function IdentityCard({
  label, role, icon: Icon, name, dni, blobURL, agentFilename, fileURL, onCapture, loading, done, disabled,
}: {
  label: string; role: string; icon: React.ElementType;
  name?: string; dni?: string;
  blobURL?: string; agentFilename?: string;
  /** URL ya resuelta (carpeta del caso en Tatana, flujo agent); manda sobre `agentFilename`. */
  fileURL?: string;
  onCapture: () => void; loading: boolean; done: boolean;
  /** Durante el envío de evidencia: no se puede tomar ni retomar la foto (verla sí). */
  disabled?: boolean;
}) {
  const [lightbox, setLightbox] = useState(false);
  const previewSrc = blobURL || fileURL || (agentFilename ? agentFileURL(agentFilename) : undefined);
  const showPhoto = done && !!previewSrc;

  const subtitle = name
    ? dni ? `${name} · DNI ${dni}` : name
    : dni ? `DNI ${dni}`
    : showPhoto ? "Foto registrada"
    : "Sin foto";

  return (
    <>
      <div className="flex items-center gap-3.5 py-3.5">
        {/* ── Avatar ── */}
        <button
          type="button"
          onClick={showPhoto ? () => setLightbox(true) : onCapture}
          disabled={loading || (!!disabled && !showPhoto)}
          aria-label={showPhoto ? `Ver foto — ${label}` : `Tomar foto — ${label}`}
          aria-haspopup="dialog"
          className={cn("relative h-14 w-14 shrink-0 rounded-full disabled:cursor-wait", FOCUS_RING)}
        >
          {showPhoto ? (
            /* contenido de imagen */
            <img
              src={previewSrc}
              alt=""
              className="h-full w-full rounded-full object-cover motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
            />
          ) : (
            <span className="flex h-full w-full items-center justify-center rounded-full border-2 border-dashed border-fx-border-strong bg-fx-surface-1 text-fx-text-3">
              {loading
                ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                : <Icon className="h-5 w-5" aria-hidden="true" />}
            </span>
          )}

          {showPhoto && (
            <span
              aria-hidden="true"
              className="absolute -bottom-0.5 -right-0.5 flex h-5 w-5 items-center justify-center rounded-full bg-fx-success text-fx-on-accent ring-2 ring-fx-surface-2"
            >
              <Check className="h-3 w-3" strokeWidth={3} aria-hidden="true" />
            </span>
          )}
        </button>

        {/* ── Nombre + rol + DNI ── */}
        <div className="min-w-0 flex-1">
          <p className="m-0 flex flex-wrap items-baseline gap-x-1.5 text-fx-body-sm leading-tight">
            <span className="font-semibold text-fx-text">{label}</span>
            <span className="text-fx-text-3">· {role}</span>
          </p>
          <p className="m-0 mt-0.5 truncate text-xs text-fx-text-2">{subtitle}</p>
        </div>

        {/* ── Acción ── */}
        <Button
          type="button"
          severity="secondary"
          size="small"
          icon={showPhoto
            ? <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            : <Camera className="h-3.5 w-3.5" aria-hidden="true" />}
          label={loading ? "Abriendo…" : showPhoto ? "Retomar" : "Tomar foto"}
          loading={loading}
          disabled={disabled}
          onClick={onCapture}
          aria-label={`${showPhoto ? "Retomar" : "Tomar"} foto — ${label}`}
          aria-haspopup="dialog"
          className="shrink-0 min-h-11 sm:min-h-0"
          // En < sm "Retomar" queda solo con el ícono (el nombre lo da el aria-label).
          pt={{ label: { className: showPhoto && !loading ? "hidden sm:inline" : undefined } }}
        />
      </div>

      <Lightbox
        src={lightbox && previewSrc ? previewSrc : null}
        onClose={() => setLightbox(false)}
        alt={`Foto — ${label}`}
      />
    </>
  );
}
