"use client";

import { useEffect } from "react";
import { motion } from "framer-motion";
import { X } from "lucide-react";

export function Lightbox({ src, onClose }: { src: string; onClose: () => void }) {
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <motion.div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "rgba(0,0,0,0.92)", overscrollBehavior: "contain" }}
      initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
      onClick={onClose}
    >
      <button
        className="absolute top-5 right-5 w-9 h-9 rounded-full flex items-center justify-center"
        style={{ background: "rgba(255,255,255,0.1)", border: "1px solid rgba(255,255,255,0.15)" }}
        onClick={onClose}
        aria-label="Cerrar imagen"
      >
        <X className="w-4 h-4 text-white" aria-hidden="true" />
      </button>
      <motion.img
        src={src}
        alt="Imagen ampliada de la evidencia"
        className="max-w-full max-h-full rounded-md object-contain"
        style={{ boxShadow: "0 32px 80px rgba(0,0,0,0.8)" }}
        initial={{ scale: 0.92, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        transition={{ type: "spring", stiffness: 300, damping: 25 }}
        onClick={e => e.stopPropagation()}
      />
    </motion.div>
  );
}
