"use client";

import { motion, AnimatePresence } from "framer-motion";
import { RefreshCw, Loader2, Smartphone } from "lucide-react";
import { cn } from "@/lib/utils";

export function PhoneFrame({ src, platform, loading, onRefresh }: {
  src: string | null;
  platform: "android" | "ios";
  loading: boolean;
  onRefresh: () => void;
}) {
  const isIOS = platform === "ios";

  return (
    <div className="relative mx-auto" style={{ width: 160, height: 310 }}>

      <div
        className="absolute inset-0 rounded-[32px]"
        style={{
          background: "linear-gradient(145deg, #2a2a2e 0%, #1a1a1e 60%, #111114 100%)",
          boxShadow: "0 0 0 1.5px rgba(255,255,255,0.10), 0 20px 60px rgba(0,0,0,0.8), inset 0 1px 0 rgba(255,255,255,0.07)",
        }}
      />

      <div className="absolute left-[-3px] top-20 w-1 h-8 rounded-l-full" style={{ background: "rgba(255,255,255,0.12)" }} />
      <div className="absolute left-[-3px] top-32 w-1 h-6 rounded-l-full" style={{ background: "rgba(255,255,255,0.12)" }} />
      <div className="absolute left-[-3px] top-40 w-1 h-6 rounded-l-full" style={{ background: "rgba(255,255,255,0.12)" }} />
      <div className="absolute right-[-3px] top-24 w-1 h-10 rounded-r-full" style={{ background: "rgba(255,255,255,0.12)" }} />

      <div
        className="absolute overflow-hidden"
        style={{ inset: "10px 6px", borderRadius: isIOS ? "24px" : "22px", background: "#0a0a0a" }}
      >
        {isIOS && (
          <div
            className="absolute top-2 left-1/2 -translate-x-1/2 z-10"
            style={{ width: 44, height: 10, background: "#0a0a0a", borderRadius: 20, boxShadow: "0 0 0 1px rgba(255,255,255,0.06)" }}
          />
        )}
        {!isIOS && (
          <div
            className="absolute top-2.5 left-1/2 -translate-x-1/2 z-10 rounded-full"
            style={{ width: 7, height: 7, background: "#1a1a1a", boxShadow: "0 0 0 1px rgba(255,255,255,0.08)" }}
          />
        )}

        <AnimatePresence mode="wait">
          {loading ? (
            <motion.div
              key="loading"
              className="absolute inset-0 flex flex-col items-center justify-center gap-2"
              style={{ background: "#0f0f14" }}
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
              role="status"
              aria-live="polite"
            >
              <Loader2 className="w-6 h-6 animate-spin" style={{ color: "rgba(255,255,255,0.4)" }} aria-hidden="true" />
              <span className="text-[9px] font-mono" style={{ color: "rgba(255,255,255,0.25)" }}>capturando…</span>
            </motion.div>
          ) : src ? (
            <motion.img
              key="screenshot"
              src={src}
              alt="Pantalla del dispositivo"
              className="absolute inset-0 w-full h-full object-cover"
              initial={{ opacity: 0 }} animate={{ opacity: 1 }}
              transition={{ duration: 0.2 }}
            />
          ) : (
            <motion.div
              key="empty"
              className="absolute inset-0 flex flex-col items-center justify-center gap-3"
              style={{ background: "linear-gradient(160deg, #0d0d16 0%, #0a0a10 100%)" }}
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
            >
              <div className="space-y-1.5 text-center px-3">
                {[0.4, 0.25, 0.35, 0.2].map((w, i) => (
                  <div
                    key={i}
                    className="mx-auto h-1.5 rounded-full animate-pulse"
                    style={{ width: `${w * 100}%`, background: "rgba(255,255,255,0.1)", animationDelay: `${i * 0.15}s` }}
                  />
                ))}
              </div>
              <Smartphone className="w-7 h-7 mt-1" style={{ color: "rgba(255,255,255,0.2)" }} strokeWidth={1} aria-hidden="true" />
            </motion.div>
          )}
        </AnimatePresence>

        <div
          className="absolute inset-0 pointer-events-none"
          style={{ background: "linear-gradient(135deg, rgba(255,255,255,0.04) 0%, transparent 50%)" }}
        />
      </div>

      {isIOS && (
        <div
          className="absolute bottom-2 left-1/2 -translate-x-1/2 rounded-full"
          style={{ width: 36, height: 2.5, background: "rgba(255,255,255,0.3)" }}
        />
      )}
      {!isIOS && (
        <div className="absolute bottom-2 left-1/2 -translate-x-1/2 flex items-center gap-3" aria-hidden="true">
          {["◁", "●", "▢"].map((s, i) => (
            <span key={i} className="text-[8px]" style={{ color: "rgba(255,255,255,0.25)" }}>{s}</span>
          ))}
        </div>
      )}

      <motion.button
        className="absolute -bottom-8 left-1/2 -translate-x-1/2 flex items-center gap-1 text-[10px] font-mono rounded-full px-2.5 py-1 transition-colors"
        style={{ background: "var(--bg-elevated)", border: "1px solid var(--border-md)", color: "var(--text-muted)" }}
        onClick={onRefresh}
        disabled={loading}
        whileTap={{ scale: 0.95 }}
        title="Actualizar captura"
      >
        <RefreshCw className={cn("w-2.5 h-2.5", loading && "animate-spin")} aria-hidden="true" />
        actualizar
      </motion.button>
    </div>
  );
}
