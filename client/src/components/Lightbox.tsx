"use client";

import { useState } from "react";
import { FxMediaDialog } from "./overlay/FxMediaDialog";

/**
 * Visor ampliado de una imagen de evidencia. `src === null` = cerrado; se
 * guarda el último `src` no nulo para seguir mostrándolo durante la salida.
 * Escape, la X y el click en el fondo cierran; el foco vuelve al disparador.
 */
export function Lightbox({
  src, onClose, alt = "Imagen ampliada de la evidencia",
}: { src: string | null; onClose: () => void; alt?: string }) {
  // Ajuste de estado durante el render (patrón de React), como ResumeDeviceModal.
  const [shown, setShown] = useState<string | null>(src);
  if (src && src !== shown) setShown(src);
  const shownSrc = src ?? shown;

  return (
    <FxMediaDialog
      visible={src !== null}
      onHide={onClose}
      title="Imagen ampliada"
      titleHidden
      size="full"
      dismissableMask
    >
      <div
        className="flex h-full w-full items-center justify-center p-4"
        onClick={e => { if (e.target === e.currentTarget) onClose(); }}
      >
        {shownSrc && (
          /* contenido de imagen */
          <img src={shownSrc} alt={alt} className="max-h-full max-w-full rounded-fx-md object-contain shadow-fx-3" />
        )}
      </div>
    </FxMediaDialog>
  );
}
