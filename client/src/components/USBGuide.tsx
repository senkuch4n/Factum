"use client";

import { useState } from "react";
import { motion } from "framer-motion";
import { ChevronRight } from "lucide-react";
import { IOSGuide } from "./usb-guide/IOSGuide";
import { AndroidGuide } from "./usb-guide/AndroidGuide";

interface Props { onDone: () => void; }

const TABS = [
  { value: "android", label: "Android", img: "/android.svg" },
  { value: "ios", label: "iPhone", img: "/apple.svg" },
] as const;

export function USBGuide({ onDone }: Props) {
  const [platform, setPlatform] = useState<"android" | "ios">("android");

  return (
    <div className="space-y-5">
      <div>
        <h2 className="step-title">Preparar el dispositivo</h2>
        <p className="mt-1.5 text-sm" style={{ color: "var(--text-secondary)" }}>
          Guía para habilitar la conexión USB del celular del denunciante.
        </p>
      </div>

      {/* Tabs de plataforma — Aceternity Animated Tabs (pill deslizante con layoutId). */}
      <div
        className="flex gap-1 rounded-xl p-1"
        style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
        role="tablist"
        aria-label="Tipo de dispositivo"
      >
        {TABS.map((t) => {
          const active = platform === t.value;
          return (
            <button
              key={t.value}
              role="tab"
              aria-selected={active}
              onClick={() => setPlatform(t.value)}
              className="relative flex-1 rounded-lg px-3 py-2 text-sm font-semibold transition-colors"
              style={{ color: active ? "var(--text-primary)" : "var(--text-muted)" }}
            >
              {active && (
                <motion.span
                  layoutId="guide-tab-pill"
                  transition={{ type: "spring", stiffness: 380, damping: 32 }}
                  className="absolute inset-0 rounded-lg"
                  style={{
                    background: "var(--bg-surface)",
                    border: "1px solid var(--border)",
                    boxShadow: "0 1px 3px rgba(0,0,0,0.1)",
                  }}
                />
              )}
              <span className="relative flex items-center justify-center gap-2">
                <img src={t.img} alt="" className="h-4 w-4 object-contain opacity-70 dark:opacity-70 dark:invert" />
                {t.label}
              </span>
            </button>
          );
        })}
      </div>

      {platform === "android" ? <AndroidGuide onDone={onDone} /> : <IOSGuide onDone={onDone} />}

      <button className="btn-ghost w-full text-sm" onClick={onDone}>
        El dispositivo ya está listo, continuar <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
    </div>
  );
}
