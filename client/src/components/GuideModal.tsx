"use client";

import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { LifeBuoy } from "lucide-react";
import { USBGuide } from "./USBGuide";

interface Props {
  visible: boolean;
  onClose: () => void;
  onSupport?: () => void;
}

/**
 * Guía de conexión USB en un panel lateral: `Dialog` de Prime con
 * `position="right"` (variante drawer del pt). Así cumple `role="dialog"`,
 * `aria-modal`, foco atrapado, Escape, click en la máscara y retorno del foco
 * al disparador.
 */
export function GuideModal({ visible, onClose, onSupport }: Props) {
  return (
    <Dialog
      visible={visible}
      onHide={onClose}
      position="right"
      dismissableMask
      draggable={false}
      resizable={false}
      header="Ayuda con la conexión"
      footer={
        <div className="flex w-full flex-wrap items-center justify-between gap-2 text-xs text-fx-text-3">
          <span>Guía de uso de Factum</span>
          {onSupport && (
            <Button
              link
              size="small"
              icon={<LifeBuoy className="h-3.5 w-3.5" aria-hidden="true" />}
              label="¿No conecta? Contactar soporte"
              onClick={onSupport}
              className="px-0"
            />
          )}
        </div>
      }
    >
      <USBGuide onDone={onClose} />
    </Dialog>
  );
}
