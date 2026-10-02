"use client";

import type { ReactNode } from "react";
import { Message } from "primereact/message";
import { AlertCircle, AlertTriangle, CheckCircle2, Info, X } from "lucide-react";
import { CLOSE_BUTTON } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";

type Tone = "error" | "info" | "warn" | "success";

interface FxBannerProps {
  tone: Tone;
  children: ReactNode;
  /** Default por tono: AlertCircle / Info / AlertTriangle / CheckCircle2. */
  icon?: ReactNode;
  /** Si viene, se muestra un botón cerrar (X) con aria-label "Cerrar aviso". */
  onClose?: () => void;
  /** Default: "alert" en error, "status" en el resto. "note" para demos estáticas. */
  role?: "alert" | "status" | "note";
  className?: string;
}

const ICON_CLASS = "h-4 w-4 shrink-0 mt-0.5";

function defaultIcon(tone: Tone) {
  switch (tone) {
    case "error":
      return <AlertCircle className={ICON_CLASS} aria-hidden="true" />;
    case "warn":
      return <AlertTriangle className={ICON_CLASS} aria-hidden="true" />;
    case "success":
      return <CheckCircle2 className={ICON_CLASS} aria-hidden="true" />;
    default:
      return <Info className={ICON_CLASS} aria-hidden="true" />;
  }
}

/**
 * Aviso en línea del dashboard: `Message` de Prime (pt `message`) con ícono,
 * texto y cierre opcional. El `aria-live` lo pone el pt (`assertive` en error,
 * `polite` en el resto); el `role` de props pisa el `role="alert"` interno.
 */
export function FxBanner({ tone, children, icon, onClose, role, className }: FxBannerProps) {
  return (
    <Message
      severity={tone}
      role={role ?? (tone === "error" ? "alert" : "status")}
      className={cn("w-full motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]", className)}
      content={
        <>
          {icon ?? defaultIcon(tone)}
          <div className="min-w-0 flex-1 font-medium">{children}</div>
          {onClose && (
            <button
              type="button"
              onClick={onClose}
              aria-label="Cerrar aviso"
              className={cn(CLOSE_BUTTON, "-my-1 -mr-1 w-7 h-7 text-current hover:text-current")}
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          )}
        </>
      }
    />
  );
}
