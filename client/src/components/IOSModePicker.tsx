"use client";

import { useEffect } from "react";
import { motion } from "framer-motion";
import { Smartphone } from "lucide-react";

export type IOSRecordMode = "video_only" | "with_mic" | "on_device" | "airplay";

const IOS_MODES: Array<{
  mode: IOSRecordMode;
  color: string;
  bg: string;
  border: string;
  badge: string | null;
  title: string;
  subtitle: string;
  desc: string;
  illustration: React.ReactNode;
}> = [
  {
    mode: "video_only",
    color: "#6366f1", bg: "rgba(99,102,241,0.07)", border: "rgba(99,102,241,0.3)",
    badge: null,
    title: "Solo pantalla", subtitle: "Sin audio",
    desc: "Grabación de pantalla estándar. Ideal para capturas visuales sin necesidad de sonido.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="w-14 h-10" aria-hidden="true">
        <rect x="2" y="1" width="20" height="34" rx="3" stroke="#6366f1" strokeWidth="1.4" fill="#6366f1" fillOpacity="0.08"/>
        <rect x="4.5" y="5" width="15" height="23" rx="1.5" fill="#6366f1" fillOpacity="0.1"/>
        <circle cx="12" cy="31.5" r="1.5" fill="#6366f1" fillOpacity="0.35"/>
        <circle cx="17.5" cy="8" r="2.5" fill="#ef4444"/>
        <path d="M36 15 L40 12 L40 24 L36 21" stroke="#6366f1" strokeWidth="1.3" strokeLinejoin="round" fill="#6366f1" fillOpacity="0.1"/>
        <path d="M42 14 Q46 18 42 22" stroke="#6366f1" strokeWidth="1.3" strokeLinecap="round" fill="none"/>
        <line x1="33" y1="11" x2="49" y2="27" stroke="#ef4444" strokeWidth="1.6" strokeLinecap="round"/>
      </svg>
    ),
  },
  {
    mode: "with_mic",
    color: "#0ea5e9", bg: "rgba(14,165,233,0.07)", border: "rgba(14,165,233,0.3)",
    badge: null,
    title: "Pantalla + micrófono de PC", subtitle: "Audio ambiente",
    desc: "El micrófono de esta computadora capta el audio del altavoz del iPhone.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="w-14 h-10" aria-hidden="true">
        <rect x="1" y="5" width="14" height="24" rx="2.5" stroke="#0ea5e9" strokeWidth="1.3" fill="#0ea5e9" fillOpacity="0.07"/>
        <rect x="3" y="8" width="10" height="16" rx="1" fill="#0ea5e9" fillOpacity="0.1"/>
        <circle cx="13" cy="7.5" r="2" fill="#ef4444"/>
        <path d="M18 14 Q22 10 22 17 Q22 24 18 20" stroke="#0ea5e9" strokeWidth="1.3" strokeLinecap="round" fill="none"/>
        <path d="M24 11 Q30 6 30 17 Q30 28 24 23" stroke="#0ea5e9" strokeWidth="1.3" strokeLinecap="round" fill="none" opacity="0.5"/>
        <rect x="37" y="5" width="8" height="14" rx="4" stroke="#0ea5e9" strokeWidth="1.3" fill="#0ea5e9" fillOpacity="0.15"/>
        <path d="M33 18 Q33 28 41 28 Q49 28 49 18" stroke="#0ea5e9" strokeWidth="1.3" strokeLinecap="round" fill="none"/>
        <line x1="41" y1="28" x2="41" y2="33" stroke="#0ea5e9" strokeWidth="1.3" strokeLinecap="round"/>
        <line x1="38" y1="33" x2="44" y2="33" stroke="#0ea5e9" strokeWidth="1.3" strokeLinecap="round"/>
      </svg>
    ),
  },
  {
    mode: "on_device",
    color: "#10b981", bg: "rgba(16,185,129,0.07)", border: "rgba(16,185,129,0.3)",
    badge: "RECOMENDADO",
    title: "Grabación nativa del iPhone", subtitle: "Audio del sistema incluido",
    desc: "Captura audio de apps (WhatsApp, videos). El fiscal activa la grabación desde el Centro de Control.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="w-14 h-10" aria-hidden="true">
        <rect x="8" y="1" width="22" height="36" rx="3.5" stroke="#10b981" strokeWidth="1.4" fill="#10b981" fillOpacity="0.06"/>
        <rect x="10.5" y="5" width="17" height="26" rx="2" fill="#10b981" fillOpacity="0.08"/>
        <circle cx="19" cy="34" r="1.5" fill="#10b981" fillOpacity="0.4"/>
        <rect x="15" y="3" width="8" height="3.5" rx="1.75" fill="#10b981" fillOpacity="0.25"/>
        <rect x="12" y="8" width="5" height="5" rx="1" fill="#10b981" fillOpacity="0.18"/>
        <rect x="20" y="8" width="5" height="5" rx="1" fill="#10b981" fillOpacity="0.18"/>
        <rect x="12" y="15" width="5" height="5" rx="1" fill="#10b981" fillOpacity="0.18"/>
        <circle cx="22.5" cy="17.5" r="5.5" stroke="#10b981" strokeWidth="1.5"/>
        <circle cx="22.5" cy="17.5" r="3" fill="#10b981"/>
        <circle cx="22.5" cy="17.5" r="7.5" stroke="#10b981" strokeWidth="0.6" opacity="0.3"/>
        <circle cx="46" cy="7" r="3.5" fill="#10b981"/>
        <circle cx="46" cy="7" r="5.5" stroke="#10b981" strokeWidth="0.8" opacity="0.28"/>
      </svg>
    ),
  },
  {
    mode: "airplay",
    color: "#f59e0b", bg: "rgba(245,158,11,0.07)", border: "rgba(245,158,11,0.3)",
    badge: "30 FPS",
    title: "Espejo AirPlay", subtitle: "30 fps · Audio nativo del iPhone",
    desc: "El iPhone duplica su pantalla a esta PC por WiFi. Requiere misma red. Alta calidad sin cables.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="w-14 h-10" aria-hidden="true">
        <rect x="1" y="4" width="30" height="22" rx="2.5" stroke="#f59e0b" strokeWidth="1.3" fill="#f59e0b" fillOpacity="0.07"/>
        <rect x="3.5" y="6.5" width="25" height="17" rx="1.5" fill="#f59e0b" fillOpacity="0.1"/>
        <line x1="13" y1="26" x2="18" y2="26" stroke="#f59e0b" strokeWidth="1.3" strokeLinecap="round"/>
        <line x1="15.5" y1="23.5" x2="15.5" y2="26" stroke="#f59e0b" strokeWidth="1.3" strokeLinecap="round"/>
        <rect x="34" y="6" width="14" height="24" rx="2.5" stroke="#f59e0b" strokeWidth="1.3" fill="#f59e0b" fillOpacity="0.07"/>
        <rect x="36" y="9" width="10" height="17" rx="1" fill="#f59e0b" fillOpacity="0.1"/>
        <circle cx="41" cy="28.5" r="1.2" fill="#f59e0b" fillOpacity="0.4"/>
        <path d="M22 15 L27 12 L27 18 Z" fill="#f59e0b" fillOpacity="0.7"/>
        <path d="M20 11 Q33 15 20 19" stroke="#f59e0b" strokeWidth="0.8" strokeLinecap="round" fill="none" opacity="0.4"/>
      </svg>
    ),
  },
];

export function IOSModePicker({
  onSelect,
  onCancel,
}: {
  onSelect: (mode: IOSRecordMode) => void;
  onCancel: () => void;
}) {
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
      style={{ background: "rgba(0,0,0,0.65)", backdropFilter: "blur(12px)", overscrollBehavior: "contain" }}
      initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
      onClick={onCancel}
    >
      <motion.div
        className="w-full max-w-md rounded-lg overflow-hidden shadow-2xl"
        style={{ background: "var(--bg-card)", border: "1px solid var(--border)" }}
        initial={{ scale: 0.92, y: 24, opacity: 0 }}
        animate={{ scale: 1, y: 0, opacity: 1 }}
        exit={{ scale: 0.94, y: 10, opacity: 0 }}
        transition={{ type: "spring", stiffness: 380, damping: 28 }}
        onClick={e => e.stopPropagation()}
      >
        <div className="px-5 pt-5 pb-4" style={{ borderBottom: "1px solid var(--border)" }}>
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-md flex items-center justify-center flex-shrink-0"
              style={{ background: "rgba(99,102,241,0.12)", border: "1px solid rgba(99,102,241,0.22)" }}>
              <Smartphone className="w-4 h-4" style={{ color: "#818cf8" }} aria-hidden="true" />
            </div>
            <div>
              <h3 className="text-sm font-bold" style={{ color: "var(--text-primary)" }}>Modo de grabación iOS</h3>
              <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>Seleccioná cómo capturar el audio durante la inspección</p>
            </div>
          </div>
        </div>

        <div className="p-4 space-y-2.5">
          {IOS_MODES.map(({ mode, color, bg, border, badge, title, subtitle, desc, illustration }) => (
            <button key={mode}
              className="w-full flex items-center gap-3.5 p-3.5 rounded-md text-left transition duration-150"
              style={{ background: "var(--bg-input)", border: "1px solid var(--border)" }}
              onClick={() => onSelect(mode)}
              onMouseEnter={e => { e.currentTarget.style.borderColor = border; e.currentTarget.style.background = bg; }}
              onMouseLeave={e => { e.currentTarget.style.borderColor = "var(--border)"; e.currentTarget.style.background = "var(--bg-input)"; }}
              onFocus={e => { e.currentTarget.style.borderColor = border; e.currentTarget.style.background = bg; }}
              onBlur={e => { e.currentTarget.style.borderColor = "var(--border)"; e.currentTarget.style.background = "var(--bg-input)"; }}
            >
              <div className="flex-shrink-0 rounded-md flex items-center justify-center overflow-hidden"
                style={{ width: 64, height: 44, background: bg, border: `1px solid ${border}` }}>
                {illustration}
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-1.5 flex-wrap">
                  <p className="text-sm font-semibold leading-tight" style={{ color: "var(--text-primary)" }}>{title}</p>
                  {badge && (
                    <span className="text-[8px] font-bold px-1.5 py-0.5 rounded tracking-wide"
                      style={{ background: bg, color, border: `1px solid ${border}` }}>{badge}</span>
                  )}
                </div>
                <p className="text-[10px] font-semibold mt-0.5" style={{ color }}>{subtitle}</p>
                <p className="text-xs mt-1 leading-relaxed" style={{ color: "var(--text-muted)" }}>{desc}</p>
              </div>
            </button>
          ))}
        </div>

        <div className="px-5 pb-5">
          <button className="w-full text-xs py-2.5 rounded-md font-medium"
            style={{ color: "var(--text-muted)", background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
            onClick={onCancel}>
            Cancelar
          </button>
        </div>
      </motion.div>
    </motion.div>
  );
}
