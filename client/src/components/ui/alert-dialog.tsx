"use client";

/**
 * Alert dialog de confirmación — adaptado de scrollxui (docs/components/alert-dialog).
 *
 * El original arrastra `@radix-ui/react-alert-dialog` + `@scrollxui/button`, con
 * un botón de acción que hace `rotateX/rotateY/translateZ` en 3D al hover y un
 * "shake" al click afuera — nada de eso pega con factum. Acá se reconstruye a
 * mano siguiendo el patrón de los modales que ya existen (GuideModal/SoporteModal):
 * fixed centrado, backdrop, entrada spring, Escape = cancelar. Diferencias propias
 * de un alert-dialog: `role="alertdialog"`, foco inicial en "Cancelar", y el click
 * en el backdrop NO cierra (evita descartes accidentales).
 */

import { useEffect, useRef } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { AlertTriangle } from "lucide-react";
import { cn } from "@/lib/utils";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: () => void;
  title: string;
  description?: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** rojo para acciones destructivas (default), teal para confirmaciones neutras */
  tone?: "destructive" | "neutral";
}

export function ConfirmDialog({
  open,
  onOpenChange,
  onConfirm,
  title,
  description,
  confirmLabel = "Confirmar",
  cancelLabel = "Cancelar",
  tone = "destructive",
}: Props) {
  const cancelRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    cancelRef.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onOpenChange(false); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onOpenChange]);

  const destructive = tone === "destructive";

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[70] flex items-center justify-center p-4"
          style={{ background: "rgba(0,0,0,0.7)", overscrollBehavior: "contain" }}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
        >
          <motion.div
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="confirm-title"
            aria-describedby={description ? "confirm-desc" : undefined}
            className="w-full max-w-sm overflow-hidden rounded-lg"
            style={{ background: "var(--bg-card)", border: "1px solid var(--border)", boxShadow: "0 24px 64px rgba(0,0,0,0.35)" }}
            initial={{ opacity: 0, scale: 0.94, y: 16 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: 8 }}
            transition={{ type: "spring", stiffness: 380, damping: 30 }}
          >
            <div className="flex items-start gap-3 p-5">
              <div
                className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg"
                style={{
                  background: destructive ? "rgba(220,38,38,0.12)" : "rgba(45,212,191,0.12)",
                  border: `1px solid ${destructive ? "rgba(220,38,38,0.25)" : "var(--border-accent)"}`,
                }}
              >
                <AlertTriangle
                  className="h-4 w-4"
                  style={{ color: destructive ? "#dc2626" : "var(--blue-lg)" }}
                  aria-hidden="true"
                />
              </div>
              <div className="min-w-0 flex-1">
                <p id="confirm-title" className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>
                  {title}
                </p>
                {description && (
                  <p id="confirm-desc" className="mt-1 text-[13px] leading-snug" style={{ color: "var(--text-muted)" }}>
                    {description}
                  </p>
                )}
              </div>
            </div>

            <div className="flex justify-end gap-2 border-t px-5 py-3" style={{ borderColor: "var(--border)" }}>
              <button
                ref={cancelRef}
                type="button"
                onClick={() => onOpenChange(false)}
                className="btn-secondary btn-sm rounded-lg"
              >
                {cancelLabel}
              </button>
              <button
                type="button"
                onClick={() => { onConfirm(); onOpenChange(false); }}
                className={cn("btn-sm rounded-lg font-medium text-white")}
                style={{ background: destructive ? "#dc2626" : "var(--blue-lg)" }}
              >
                {confirmLabel}
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

export default ConfirmDialog;
