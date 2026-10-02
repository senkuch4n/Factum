"use client";

import type { ReactNode } from "react";
import { Dialog, type DialogPassThroughOptions } from "primereact/dialog";
import { DIALOG_MEDIA_PT } from "@/lib/prime/pt/dialog";
import { cn } from "@/lib/utils";

interface FxMediaDialogProps {
  visible: boolean;
  onHide: () => void;
  /** Obligatorio: es el `aria-labelledby` del diálogo (también con `titleHidden`). */
  title: ReactNode;
  /** Ícono a la izquierda del título (decorativo: pasarlo con `aria-hidden`). */
  icon?: ReactNode;
  /** Título solo para lector de pantalla (visor de imagen). */
  titleHidden?: boolean;
  /** Default true. `false` = sin X y sin Escape (cámara externa grabando). */
  closable?: boolean;
  /** Default false: el click en la máscara no cierra (hay estado que se perdería). */
  dismissableMask?: boolean;
  /** `md` = 40rem (pantalla completa en < sm); `full` = siempre pantalla completa. */
  size?: "md" | "full";
  footer?: ReactNode;
  onShow?: () => void;
  children: ReactNode;
}

const ROOT_SIZE = {
  md: "max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0",
  full: "!w-screen !h-screen !max-h-full rounded-none border-0",
} as const;

/** `className` de una sección de pt definida como objeto. */
const classOf = (section: unknown) => (section as { className?: string } | undefined)?.className;

/**
 * Variante "media" del Dialog de Prime (DP1 A): webcam, cámara externa y
 * visor. La clase `dark` en la raíz del panel resuelve todos los tokens
 * `--fx-*` del subárbol en oscuro, así la superficie de cámara es siempre
 * oscura, también en modo claro, sin tokens nuevos ni hex. La máscara queda
 * fuera de la raíz y sigue el tema de la página.
 *
 * Todo va por `pt` de instancia (objetos) y no por `className`/`maskClassName`:
 * el `className` de props se fusiona antes que el pt global (pierde en el
 * merge) y en unstyled `maskClassName` no se aplica.
 *
 * Los overlays de Prime que se abren desde adentro (panel del Dropdown) se
 * portalean a `body` y no heredan `.dark`: llevan `panelClassName="dark"`.
 */
export function FxMediaDialog({
  visible, onHide, title, icon, titleHidden, closable = true, dismissableMask = false,
  size = "md", footer, onShow, children,
}: FxMediaDialogProps) {
  const full = size === "full";
  const pt: DialogPassThroughOptions = {
    ...DIALOG_MEDIA_PT,
    root: { className: cn("dark", classOf(DIALOG_MEDIA_PT.root), ROOT_SIZE[size]) },
    content: {
      className: cn(
        classOf(DIALOG_MEDIA_PT.content),
        full ? "flex-1 min-h-0 h-full" : "max-sm:flex-1",
      ),
    },
    mask: { className: full ? "p-0" : "max-sm:p-0" },
  };

  return (
    <Dialog
      visible={visible}
      onHide={onHide}
      onShow={onShow}
      modal
      closable={closable}
      closeOnEscape={closable}
      dismissableMask={dismissableMask}
      draggable={false}
      resizable={false}
      blockScroll
      footer={footer}
      pt={pt}
      header={
        <span className="flex items-center gap-2">
          {icon}
          <span className={titleHidden ? "sr-only" : undefined}>{title}</span>
        </span>
      }
    >
      {children}
    </Dialog>
  );
}
