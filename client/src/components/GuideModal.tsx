"use client";

import { useEffect } from "react";
import { motion } from "framer-motion";
import { X, LifeBuoy } from "lucide-react";
import { USBGuide } from "./USBGuide";

export function GuideModal({ onClose, onSupport }: { onClose: () => void; onSupport?: () => void }) {
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <motion.div
      className="fixed inset-0 z-50 flex justify-end"
      style={{ background: "rgba(0,0,0,0.55)", overscrollBehavior: "contain" }}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      onClick={onClose}
    >
      <motion.div
        className="flex h-full w-full max-w-xl flex-col md:max-w-2xl"
        style={{
          background: "var(--bg-surface)",
          borderLeft: "1px solid var(--border)",
          boxShadow: "-24px 0 64px rgba(0,0,0,0.35)",
        }}
        initial={{ x: "100%" }}
        animate={{ x: 0 }}
        exit={{ x: "100%" }}
        transition={{ type: "spring", stiffness: 320, damping: 34 }}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-label="Ayuda con la conexión"
      >
        <div
          className="flex flex-shrink-0 items-center justify-between px-5 py-3.5"
          style={{ borderBottom: "1px solid var(--border)" }}
        >
          <p className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>
            Ayuda con la conexión
          </p>
          <button
            onClick={onClose}
            className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-md transition-colors hover:bg-[var(--bg-hover)]"
            style={{ color: "var(--text-muted)" }}
            aria-label="Cerrar"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          <USBGuide onDone={onClose} />
        </div>

        <div
          className="flex flex-shrink-0 items-center justify-between gap-3 px-5 py-3 text-[11px]"
          style={{ borderTop: "1px solid var(--border)", color: "var(--text-muted)" }}
        >
          <span>Guía de uso de Factum</span>
          {onSupport && (
            <button
              type="button"
              onClick={onSupport}
              className="flex flex-shrink-0 items-center gap-1.5 font-medium transition-colors hover:text-[var(--blue-lg)]"
            >
              <LifeBuoy className="h-3 w-3" aria-hidden="true" /> ¿No conecta? Contactar soporte
            </button>
          )}
        </div>
      </motion.div>
    </motion.div>
  );
}
