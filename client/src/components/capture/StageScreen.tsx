"use client";

import { useState } from "react";
import { ExternalLink, FileText, ImageIcon, Maximize2, Volume2 } from "lucide-react";
import { FOCUS_RING, FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { srcOf, type GItem } from "./gallery";

/**
 * Imagen con fallback: si el archivo no carga (agente mock, red, archivo
 * movido) se muestra un ícono en vez del ícono roto del navegador.
 */
export function SafeImg({ src, alt, className }: { src?: string; alt: string; className?: string }) {
  const [errored, setErrored] = useState(false);
  if (!src || errored) {
    return (
      <div className={cn("flex items-center justify-center bg-fx-surface-3 text-fx-text-3", className)}>
        <ImageIcon className="h-6 w-6" aria-hidden="true" />
      </div>
    );
  }
  /* contenido de imagen */
  return <img src={src} alt={alt} className={className} onError={() => setErrored(true)} />;
}

/**
 * Contenido de la pantalla del marco de teléfono según el tipo de ítem. Va
 * dentro de `MEDIA_SURFACE` (tokens oscuros). Los videos de grabación del
 * agente no pasan por acá: usan <VideoCard> con sus variantes.
 */
export function StageScreen({ item, onExpand }: { item: GItem; onExpand: (src: string) => void }) {
  const src = srcOf(item);

  if (item.kind === "screenshot" || item.kind === "local-image") {
    return (
      <>
        <SafeImg src={src} alt={item.name} className="h-full w-full object-contain" />
        <button
          type="button"
          onClick={() => onExpand(src)}
          aria-label="Ver en grande"
          aria-haspopup="dialog"
          className={cn(
            "absolute bottom-3 right-3 z-20 flex h-11 w-11 items-center justify-center rounded-full",
            "bg-fx-overlay text-fx-text transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3",
            FOCUS_RING,
          )}
        >
          <Maximize2 className="h-4 w-4" aria-hidden="true" />
        </button>
      </>
    );
  }
  if (item.kind === "local-video") {
    /* contenido de imagen */
    return <video src={src} controls playsInline preload="metadata" className="h-full w-full object-contain" />;
  }
  if (item.kind === "local-audio") {
    return (
      <div className="flex h-full w-full flex-col items-center justify-center gap-4 px-6">
        <div className="flex h-16 w-16 items-center justify-center rounded-full bg-fx-surface-3 text-fx-text-2">
          <Volume2 className="h-7 w-7" aria-hidden="true" />
        </div>
        <audio src={src} controls className="w-full" />
      </div>
    );
  }
  return (
    <div className="flex h-full w-full flex-col items-center justify-center gap-3 px-6 text-center">
      <FileText className="h-10 w-10 text-fx-text-3" aria-hidden="true" />
      <a href={src} target="_blank" rel="noreferrer" className={cn(FX_BUTTON_SECONDARY, "px-3.5 py-2 text-xs")}>
        <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" /> Abrir archivo
      </a>
    </div>
  );
}

/** Estado vacío de la pantalla del escenario. */
export function StageEmpty() {
  return (
    <div className="flex h-full w-full flex-col items-center justify-center gap-2 px-6 text-center">
      <ImageIcon className="h-7 w-7 text-fx-text-3" aria-hidden="true" />
      <p className="m-0 text-xs font-medium text-fx-text-2">Sin capturas todavía</p>
      <p className="m-0 text-[11px] leading-relaxed text-fx-text-3">
        Usá <span className="text-fx-text">Pantalla</span> o <span className="text-fx-text">Grabar</span> para empezar
      </p>
    </div>
  );
}
