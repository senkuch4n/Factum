"use client";

import { useEffect } from "react";
import { motion, AnimatePresence, useReducedMotion } from "framer-motion";
import { Smartphone, CheckCircle2 } from "lucide-react";
import type { Case } from "@/types";

interface Props {
  cas: Case;
  deviceConnected: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ResumeDeviceModal({ cas, deviceConnected, onConfirm, onCancel }: Props) {
  const isIOS = cas.device.platform === "ios";
  const serialShort = cas.device.serial.length > 10
    ? cas.device.serial.slice(0, 10) + "…"
    : cas.device.serial;
  const prefersReducedMotion = useReducedMotion();

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onCancel();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onCancel]);

  return (
    <motion.div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "rgba(0,0,0,0.7)", backdropFilter: "blur(14px)", overscrollBehavior: "contain" }}
      initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
    >
      <motion.div
        className="w-full max-w-sm rounded-lg overflow-hidden shadow-2xl"
        style={{ background: "var(--bg-card)", border: "1px solid var(--border)" }}
        initial={{ scale: 0.88, y: 28, opacity: 0 }}
        animate={{ scale: 1, y: 0, opacity: 1 }}
        exit={{ scale: 0.94, y: 10, opacity: 0 }}
        transition={{ type: "spring", stiffness: 380, damping: 28 }}
      >
        <div className="px-5 pt-5 pb-4" style={{ borderBottom: "1px solid var(--border)" }}>
          <div className="flex items-center gap-3" role="status" aria-live="polite">
            <AnimatePresence mode="wait">
              {deviceConnected ? (
                <motion.div
                  key="connected-icon"
                  initial={{ scale: 0 }} animate={{ scale: 1 }}
                  transition={{ type: "spring", stiffness: 400, damping: 18 }}
                  className="w-9 h-9 rounded-md flex items-center justify-center flex-shrink-0"
                  style={{ background: "rgba(16,185,129,0.12)", border: "1px solid rgba(16,185,129,0.25)" }}
                >
                  <CheckCircle2 className="w-5 h-5 text-emerald-500" aria-hidden="true" />
                </motion.div>
              ) : (
                <motion.div
                  key="waiting-icon"
                  className="relative w-9 h-9 rounded-md flex items-center justify-center flex-shrink-0"
                  style={{ background: "rgba(245,158,11,0.12)", border: "1px solid rgba(245,158,11,0.25)" }}
                >
                  <Smartphone className="w-4 h-4 text-amber-500" aria-hidden="true" />
                  <motion.div
                    className="absolute -inset-1 rounded-md pointer-events-none"
                    style={{ border: "1px solid rgba(245,158,11,0.4)" }}
                    animate={{ scale: [1, 1.35, 1], opacity: [0.6, 0, 0.6] }}
                    transition={{ duration: 2, repeat: prefersReducedMotion ? 0 : Infinity, ease: "easeInOut" }}
                    aria-hidden="true"
                  />
                </motion.div>
              )}
            </AnimatePresence>
            <AnimatePresence mode="wait">
              {deviceConnected ? (
                <motion.div key="h-connected" initial={{ opacity: 0, x: 8 }} animate={{ opacity: 1, x: 0 }}>
                  <p className="text-sm font-bold text-emerald-600 dark:text-emerald-400">Dispositivo detectado</p>
                  <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>Listo para retomar la inspección</p>
                </motion.div>
              ) : (
                <motion.div key="h-waiting" initial={{ opacity: 0, x: 8 }} animate={{ opacity: 1, x: 0 }}>
                  <p className="text-sm font-bold" style={{ color: "var(--text-primary)" }}>Conectá el dispositivo</p>
                  <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>Este caso requiere el dispositivo original</p>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </div>

        <div className="p-4 space-y-3">
          <motion.div
            className="rounded-md p-3.5 flex items-center gap-3"
            animate={{
              background: deviceConnected ? "rgba(16,185,129,0.05)" : "var(--bg-elevated)",
              borderColor: deviceConnected ? "rgba(16,185,129,0.22)" : "var(--border)",
            }}
            transition={{ duration: 0.35 }}
            style={{ border: "1px solid var(--border)" }}
          >
            <div
              className="w-10 h-10 rounded-md flex items-center justify-center text-base flex-shrink-0"
              style={{ background: "var(--bg-input)", border: "1px solid var(--border)" }}
            >
              <img
                src={isIOS ? "/apple.svg" : "/android.svg"}
                alt={isIOS ? "iOS" : "Android"}
                className="w-5 h-5 opacity-70 dark:invert dark:opacity-65"
              />
            </div>
            <div className="flex-1 min-w-0">
              <p className="text-sm font-bold truncate" style={{ color: "var(--text-primary)" }}>
                {cas.device.manufacturer} {cas.device.model}
              </p>
              <p className="text-[10px] font-mono mt-0.5" style={{ color: "var(--text-muted)" }}>
                {isIOS ? "UDID" : "Serial"}: {serialShort}
              </p>
              <p className="text-[10px] mt-0.5" style={{ color: "var(--text-muted)" }}>
                Expte:{" "}
                <span className="font-semibold" style={{ color: "var(--text-secondary)" }}>
                  {cas.nro_referencia}
                </span>
              </p>
            </div>
            <AnimatePresence mode="wait">
              {deviceConnected ? (
                <motion.div
                  key="badge-ok"
                  initial={{ scale: 0 }} animate={{ scale: 1 }}
                  transition={{ type: "spring", stiffness: 400, damping: 18 }}
                  className="w-7 h-7 rounded-full flex items-center justify-center flex-shrink-0"
                  style={{ background: "rgba(16,185,129,0.15)", border: "1px solid rgba(16,185,129,0.3)" }}
                >
                  <CheckCircle2 className="w-4 h-4 text-emerald-500" aria-hidden="true" />
                </motion.div>
              ) : (
                <motion.div
                  key="badge-wait"
                  className="w-7 h-7 rounded-full flex items-center justify-center flex-shrink-0"
                  style={{ background: "rgba(245,158,11,0.1)", border: "1px solid rgba(245,158,11,0.22)" }}
                >
                  <motion.div
                    className="w-2 h-2 rounded-full bg-amber-400"
                    animate={{ opacity: [1, 0.3, 1] }}
                    transition={{ duration: 1.4, repeat: prefersReducedMotion ? 0 : Infinity }}
                    aria-hidden="true"
                  />
                </motion.div>
              )}
            </AnimatePresence>
          </motion.div>

          <AnimatePresence mode="wait">
            {deviceConnected ? (
              <motion.div
                key="msg-connected"
                initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
                className="flex items-center gap-2 px-1"
              >
                <div className="w-1.5 h-1.5 rounded-full bg-emerald-500 flex-shrink-0" />
                <p className="text-xs font-medium text-emerald-600 dark:text-emerald-400">
                  Dispositivo conectado y reconocido — podés continuar la inspección
                </p>
              </motion.div>
            ) : (
              <motion.div
                key="msg-waiting"
                initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
                className="space-y-2.5"
              >
                <div className="flex items-center gap-2 px-1">
                  <motion.div
                    className="w-1.5 h-1.5 rounded-full bg-amber-400 flex-shrink-0"
                    animate={{ opacity: [1, 0.3, 1] }}
                    transition={{ duration: 1.4, repeat: prefersReducedMotion ? 0 : Infinity }}
                    aria-hidden="true"
                  />
                  <p className="text-xs" style={{ color: "var(--text-muted)" }}>
                    Esperando conexión USB — se detectará automáticamente
                  </p>
                </div>
                <div className="grid grid-cols-2 gap-2">
                  {[
                    isIOS
                      ? "Conectá el iPhone con el cable Lightning o USB-C"
                      : "Conectá el dispositivo con el cable USB",
                    isIOS
                      ? 'Desbloqueá y tocá "Confiar en este equipo"'
                      : "Desbloqueá el dispositivo si está en pantalla de bloqueo",
                  ].map((tip, i) => (
                    <div key={i} className="flex items-start gap-1.5">
                      <div
                        className="w-4 h-4 rounded-full flex items-center justify-center text-[9px] font-bold flex-shrink-0 mt-0.5"
                        style={{ background: "rgba(245,158,11,0.15)", color: "#f59e0b" }}
                      >
                        {i + 1}
                      </div>
                      <p className="text-[10px] leading-tight" style={{ color: "var(--text-muted)" }}>{tip}</p>
                    </div>
                  ))}
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        <div className="px-4 pb-5 space-y-2">
          <AnimatePresence>
            {deviceConnected && (
              <motion.button
                initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
                className="w-full py-2.5 rounded-md font-bold text-sm text-white flex items-center justify-center gap-2"
                style={{
                  background: "var(--green)",
                  boxShadow: "0 1px 2px rgba(0,0,0,0.12)",
                }}
                onClick={onConfirm}
                whileTap={{ scale: 0.98 }}
              >
                <CheckCircle2 className="w-4 h-4" aria-hidden="true" /> Continuar inspección
              </motion.button>
            )}
          </AnimatePresence>
          <button
            className="w-full py-2 text-xs rounded-md font-medium"
            style={{ color: "var(--text-muted)", background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
            onClick={onCancel}
          >
            Cancelar
          </button>
        </div>
      </motion.div>
    </motion.div>
  );
}
