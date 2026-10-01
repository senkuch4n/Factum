"use client";

import { motion, AnimatePresence, useReducedMotion } from "framer-motion";
import { Smartphone, RefreshCw, Wifi, ChevronRight, Cpu, Fingerprint, Signal, AlertTriangle, HelpCircle } from "lucide-react";
import type { Device } from "@/lib/agent";
import { cn } from "@/lib/utils";

interface Props {
  devices: Device[];
  agentOnline: boolean;
  loading: boolean;
  onSelect: (d: Device) => void;
  onRefresh: () => void;
  onOpenGuide?: () => void;
}

export function DeviceConnect({ devices, agentOnline, loading, onSelect, onRefresh, onOpenGuide }: Props) {
  const prefersReducedMotion = useReducedMotion();
  return (
    <div className="space-y-6">
      {/* Main illustration */}
      <div className="text-center pt-2 pb-4">
        <div className="relative inline-block mb-5">
          <motion.div
            className="scanline-container w-16 h-16 mx-auto rounded-lg flex items-center justify-center"
            style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
            animate={{ y: [0, -5, 0] }}
            transition={{ duration: 2.8, repeat: prefersReducedMotion ? 0 : Infinity, ease: "easeInOut" }}
          >
            <Smartphone className="w-8 h-8" style={{ color: "var(--blue-lg)" }} strokeWidth={1.3} aria-hidden="true" />
          </motion.div>

          {/* USB cable effect */}
          <motion.div
            className="absolute -bottom-3 left-1/2 -translate-x-1/2 w-0.5 h-8 rounded-full"
            style={{ background: "linear-gradient(to bottom, var(--border-md), transparent)" }}
            animate={{ opacity: [1, 0.3, 1], scaleY: [1, 0.85, 1] }}
            transition={{ duration: 1.8, repeat: prefersReducedMotion ? 0 : Infinity }}
            aria-hidden="true"
          />
        </div>

        <h2 className="step-title">
          Conectá el celular
        </h2>
        <p className="text-sm mt-2 max-w-xs mx-auto" style={{ color: "var(--text-secondary)" }}>
          Enchufá el cable USB. El sistema detecta el dispositivo automáticamente.
        </p>
        {onOpenGuide && (
          <button
            onClick={onOpenGuide}
            className="inline-flex items-center gap-1.5 mt-3 text-xs font-medium transition-colors"
            style={{ color: "var(--text-muted)" }}
            onMouseEnter={e => { (e.currentTarget as HTMLButtonElement).style.color = "var(--blue-lg)"; }}
            onMouseLeave={e => { (e.currentTarget as HTMLButtonElement).style.color = "var(--text-muted)"; }}
          >
            <HelpCircle className="w-3.5 h-3.5" aria-hidden="true" />
            ¿No sabés cómo preparar el celular?
          </button>
        )}
      </div>

      {/* Agent offline warning */}
      {!agentOnline && (
        <motion.div
          className="flex items-start gap-3 rounded-md px-4 py-3 border border-amber-500/20 bg-amber-500/[0.07]"
          initial={{ opacity: 0, y: 8 }}
          animate={{ opacity: 1, y: 0 }}
          role="status"
          aria-live="polite"
        >
          <AlertTriangle className="w-4 h-4 text-amber-400 flex-shrink-0 mt-0.5" aria-hidden="true" />
          <div>
            <p className="font-semibold text-amber-300 text-sm">Agente no activo</p>
            <p className="text-amber-500 text-xs mt-0.5">Pedile al técnico que inicie el agente en esta PC</p>
            <code className="block mt-2 text-xs font-mono text-amber-300 px-3 py-1.5 rounded-lg" style={{ background: "rgba(245,158,11,0.1)" }}>
              ./agent --mock
            </code>
          </div>
        </motion.div>
      )}

      {/* Device list */}
      <AnimatePresence mode="wait">
        {loading ? (
          <motion.div
            key="loading"
            className="flex flex-col items-center gap-4 py-10"
            initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            role="status"
            aria-live="polite"
          >
            <div className="relative w-12 h-12">
              <div className="absolute inset-0 rounded-full border border-teal-500/20" />
              <motion.div
                className="absolute inset-0 rounded-full border-t border-teal-400"
                animate={{ rotate: 360 }}
                transition={{ duration: 1, repeat: Infinity, ease: "linear" }}
                aria-hidden="true"
              />
            </div>
            <p className="text-sm" style={{ color: "var(--text-secondary)" }}>Buscando dispositivos…</p>
          </motion.div>
        ) : devices.length === 0 ? (
          <motion.div
            key="empty"
            className="text-center py-10 space-y-3"
            initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          >
            <div className="w-14 h-14 mx-auto rounded-lg flex items-center justify-center" style={{ background: "rgba(99,130,190,0.06)", border: "1px solid var(--border)" }}>
              <Smartphone className="w-7 h-7" style={{ color: "var(--text-muted)" }} strokeWidth={1.5} aria-hidden="true" />
            </div>
            <p className="text-sm" style={{ color: "var(--text-secondary)" }}>No se encontró ningún dispositivo</p>
            <motion.button
              className="btn-secondary btn-sm mx-auto"
              onClick={onRefresh}
              whileTap={{ scale: 0.95, rotate: 180 }}
            >
              <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" /> Buscar de nuevo
            </motion.button>
          </motion.div>
        ) : (
          <motion.div
            key="list"
            className="space-y-2.5"
            initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          >
            <p className="section-label mb-3">
              {devices.length === 1 ? "1 dispositivo encontrado" : `${devices.length} dispositivos`}
            </p>
            {devices.map((device, i) => (
              <motion.button
                key={device.serial}
                className={cn("card card-hover w-full text-left p-4 group")}
                onClick={() => onSelect(device)}
                initial={{ opacity: 0, x: -16 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: i * 0.08 }}
                whileHover={{ scale: 1.005 }}
                whileTap={{ scale: 0.995 }}
              >
                <div className="flex items-center gap-4">
                  <div className="w-11 h-11 rounded-md flex items-center justify-center flex-shrink-0"
                    style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
                  >
                    <Smartphone className="w-5 h-5" style={{ color: "var(--blue-lg)" }} strokeWidth={1.5} aria-hidden="true" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <p className="font-semibold text-sm transition-colors" style={{ color: "var(--text-primary)" }}>
                        {device.manufacturer} {device.model}
                      </p>
                      {device.platform === "ios" && (
                        <span className="text-[9px] font-bold px-1.5 py-0.5 rounded-full flex-shrink-0"
                          style={{ background: "rgba(99,102,241,0.12)", color: "#818cf8", border: "1px solid rgba(99,102,241,0.25)" }}>
                          iOS
                        </span>
                      )}
                    </div>
                    <div className="flex flex-wrap gap-x-4 gap-y-0.5 mt-1">
                      <span className="text-xs flex items-center gap-1" style={{ color: "var(--text-muted)" }}>
                        <Signal className="w-2.5 h-2.5" aria-hidden="true" />
                        {device.platform === "ios"
                          ? `iOS ${device.ios_version ?? device.android_version}`
                          : `Android ${device.android_version}`}
                      </span>
                      {device.imei && device.imei !== "INGRESAR_MANUALMENTE" && (
                        <span className="text-xs flex items-center gap-1 font-mono" style={{ color: "var(--text-muted)" }}>
                          <Fingerprint className="w-2.5 h-2.5" aria-hidden="true" /> {device.imei}
                        </span>
                      )}
                      <span className="text-xs flex items-center gap-1 font-mono" style={{ color: "var(--text-muted)" }}>
                        <Cpu className="w-2.5 h-2.5" aria-hidden="true" /> {device.serial.slice(0, 20)}{device.serial.length > 20 ? "…" : ""}
                      </span>
                    </div>
                  </div>
                  <ChevronRight className="w-4 h-4 flex-shrink-0 group-hover:translate-x-0.5 transition" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
                </div>
              </motion.button>
            ))}
          </motion.div>
        )}
      </AnimatePresence>

      {/* Tips */}
      {agentOnline && devices.length === 0 && !loading && (
        <motion.div className="space-y-2" initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ delay: 0.5 }}>
          <div className="flex gap-3 rounded-md px-4 py-3 text-sm" style={{ border: "1px solid var(--border)", background: "var(--bg-elevated)" }}>
            <Wifi className="w-4 h-4 flex-shrink-0 mt-0.5" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
            <div>
              <p className="font-semibold text-xs" style={{ color: "var(--text-primary)" }}>Android — ¿no aparece?</p>
              <p className="text-xs mt-0.5" style={{ color: "var(--text-secondary)" }}>
                Activá <strong style={{ color: "var(--text-primary)" }}>Depuración USB</strong> en Ajustes → Opciones de desarrollo. Si pide autorizar, tocá Permitir.
              </p>
            </div>
          </div>
          <div className="flex gap-3 rounded-md px-4 py-3 text-sm" style={{ border: "1px solid var(--border)", background: "var(--bg-elevated)" }}>
            <img src="/apple.svg" alt="iOS" className="w-4 h-4 mt-0.5 flex-shrink-0 opacity-70 dark:invert dark:opacity-65" />
            <div>
              <p className="font-semibold text-xs" style={{ color: "var(--text-primary)" }}>iPhone — ¿no aparece?</p>
              <p className="text-xs mt-0.5" style={{ color: "var(--text-secondary)" }}>
                Conectá el cable y tocá <strong style={{ color: "var(--text-primary)" }}>Confiar</strong> en la pantalla del iPhone. En iOS 16+ activá también <strong style={{ color: "var(--text-primary)" }}>Modo Desarrollador</strong> en Ajustes → Privacidad.
              </p>
            </div>
          </div>
        </motion.div>
      )}
    </div>
  );
}
