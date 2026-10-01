"use client";

import type { ElementType, ReactNode } from "react";
import { useState } from "react";
import { ChevronLeft, ChevronRight, Check, Info, HelpCircle, Terminal, Copy, Zap } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Mockup } from "./types";

export function StepIcon({ icon, className }: { icon: ElementType | string; className?: string }) {
  if (typeof icon === "string") {
    if (icon.startsWith("/")) {
      return (
        <img
          src={icon}
          alt=""
          className={cn("opacity-80 dark:invert dark:opacity-70", className ?? "w-10 h-10")}
        />
      );
    }
    return <>{icon}</>;
  }
  const Icon = icon as ElementType<{ className?: string }>;
  return <Icon className={cn("w-10 h-10", className)} />;
}

export function PhoneMockup({ mockup }: { mockup: Mockup }) {
  return (
    <div className="phone-mock mx-auto" style={{ maxWidth: 220 }} aria-hidden="true">
      <div className="phone-mock-bar">
        <span>9:41</span>
        <div className="flex items-center gap-1">
          <span>●●●</span><span>▮▮▮</span><span>⬡</span>
        </div>
      </div>
      <div className="phone-mock-header">
        <ChevronLeft className="w-4 h-4 text-[#0a84ff]" />
        <span>{mockup.header}</span>
      </div>
      {mockup.section && <div className="phone-mock-section">{mockup.section}</div>}
      {mockup.rows.map((row, i) => (
        <div key={i} className={cn("phone-mock-row", row.highlight && "phone-mock-row-active")}>
          <span className={row.highlight ? "font-semibold text-white" : ""}>{row.text}</span>
          {row.value && <span className={row.highlight ? "phone-mock-row-tap" : "phone-mock-row-value"}>{row.value}</span>}
          {!row.value && !row.highlight && <ChevronRight className="w-3 h-3 text-[#8e8e93]" />}
          {row.highlight && !row.value && <span className="phone-mock-row-tap">TAP ×7</span>}
        </div>
      ))}
    </div>
  );
}

export function DialogMockup({ title, message, confirm }: { title: string; message: string; confirm: string }) {
  return (
    <div className="mx-auto rounded-lg overflow-hidden" style={{ maxWidth: 220, background: "rgba(255,255,255,0.08)", border: "1px solid rgba(255,255,255,0.15)" }} aria-hidden="true">
      <div className="p-4 text-center space-y-2">
        <p className="font-bold text-xs" style={{ color: "var(--text-primary)" }}>{title}</p>
        <p className="text-[10px] leading-relaxed" style={{ color: "var(--text-secondary)" }}>{message}</p>
      </div>
      <div style={{ borderTop: "1px solid rgba(255,255,255,0.1)" }} className="grid grid-cols-2">
        <span className="py-2.5 text-xs block text-center" style={{ color: "#0a84ff", borderRight: "1px solid rgba(255,255,255,0.1)" }}>No confiar</span>
        <span className="py-2.5 text-xs font-bold block text-center" style={{ color: "#0a84ff" }}>{confirm}</span>
      </div>
    </div>
  );
}

export function NotifMockup({ label }: { label: string }) {
  return (
    <div className="mx-auto rounded-md overflow-hidden" style={{ maxWidth: 220, background: "rgba(255,255,255,0.06)", border: "1px solid rgba(255,255,255,0.12)" }} aria-hidden="true">
      <div className="px-3 py-2 flex items-center gap-2" style={{ borderBottom: "1px solid rgba(255,255,255,0.08)" }}>
        <span className="text-base">🔌</span>
        <div>
          <p className="text-[10px] font-bold" style={{ color: "var(--text-primary)" }}>Sistema · USB</p>
          <p className="text-[9px]" style={{ color: "var(--text-secondary)" }}>{label}</p>
        </div>
      </div>
      <div className="px-3 py-2 space-y-1">
        {["Solo carga", "Transferencia de archivos (MTP)", "PTP (fotos)", "MIDI"].map((opt, i) => (
          <div key={opt} className={cn("flex items-center gap-2 rounded-lg px-2 py-1.5", i === 1 && "font-bold")} style={{ background: i === 1 ? "rgba(13,148,136,0.2)" : "transparent" }}>
            <div className={cn("w-3 h-3 rounded-full border-2 flex items-center justify-center", i === 1 ? "border-teal-500" : "border-gray-500")}>
              {i === 1 && <div className="w-1.5 h-1.5 rounded-full bg-teal-500" />}
            </div>
            <span className="text-[9px]" style={{ color: i === 1 ? "var(--blue)" : "var(--text-secondary)" }}>{opt}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

export function AndroidVersionCard() {
  return (
    <div className="space-y-3 p-4 rounded-lg" style={{ background: "rgba(13,148,136,0.05)", border: "1px solid rgba(13,148,136,0.15)" }}>
      <p className="section-label text-teal-600 dark:text-teal-400 flex items-center gap-1.5">
        <Info className="w-3 h-3" aria-hidden="true" /> ¿Por qué importa la versión de Android?
      </p>
      <div className="grid grid-cols-2 gap-2">
        {[
          { range: "Android ≥ 11", icon: "🎙️", color: "rgba(16,185,129,0.1)", border: "rgba(16,185,129,0.25)", text: "Grabación con audio", sub: "Pantalla + sonido del sistema" },
          { range: "Android 9–10",  icon: "📹", color: "rgba(45,212,191,0.08)", border: "rgba(45,212,191,0.2)", text: "Solo video", sub: "Sin audio del sistema" },
        ].map(item => (
          <div key={item.range} className="rounded-md p-3 text-center space-y-1" style={{ background: item.color, border: `1px solid ${item.border}` }}>
            <span className="text-xl">{item.icon}</span>
            <p className="text-xs font-bold" style={{ color: "var(--text-primary)" }}>{item.range}</p>
            <p className="text-xs font-semibold" style={{ color: "var(--text-secondary)" }}>{item.text}</p>
            <p className="text-[10px]" style={{ color: "var(--text-muted)" }}>{item.sub}</p>
          </div>
        ))}
      </div>
      <div className="flex items-start gap-2 text-xs" style={{ color: "var(--text-secondary)" }}>
        <HelpCircle className="w-3 h-3 flex-shrink-0 mt-0.5 text-teal-500" aria-hidden="true" />
        <span>Para ver la versión: <strong style={{ color: "var(--text-primary)" }}>Ajustes → Acerca del teléfono → Versión de Android</strong></span>
      </div>
    </div>
  );
}

export function TerminalBlock({ command }: { command: string }) {
  const [copied, setCopied] = useState(false);

  function handleCopy() {
    navigator.clipboard.writeText(command).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  return (
    <div className="mx-auto rounded-md overflow-hidden" style={{ maxWidth: 320, border: "1px solid rgba(99,102,241,0.3)" }}>
      <div className="flex items-center gap-2 px-3 py-2" style={{ background: "rgba(99,102,241,0.15)", borderBottom: "1px solid rgba(99,102,241,0.2)" }}>
        <div className="flex gap-1.5" aria-hidden="true">
          <div className="w-2.5 h-2.5 rounded-full bg-red-400/70" />
          <div className="w-2.5 h-2.5 rounded-full bg-amber-400/70" />
          <div className="w-2.5 h-2.5 rounded-full bg-green-400/70" />
        </div>
        <Terminal className="w-3 h-3 ml-1" style={{ color: "#818cf8" }} aria-hidden="true" />
        <span className="text-[9px] font-semibold uppercase tracking-wider" style={{ color: "#818cf8" }}>Terminal</span>
      </div>
      <div className="px-3 py-3 flex items-start gap-2" style={{ background: "#0d1117" }}>
        <span className="text-[10px] font-mono mt-0.5 flex-shrink-0" style={{ color: "#6b82a8" }} aria-hidden="true">$</span>
        <span className="text-[10px] font-mono leading-relaxed break-all flex-1" style={{ color: "#99f6e4" }}>
          {command}
        </span>
        <button
          onClick={handleCopy}
          className="flex-shrink-0 p-1 rounded transition-colors"
          style={{ color: copied ? "#10b981" : "#6b82a8" }}
          title="Copiar comando"
          aria-label={copied ? "Comando copiado" : "Copiar comando"}
        >
          {copied ? <Check className="w-3 h-3" strokeWidth={3} aria-hidden="true" /> : <Copy className="w-3 h-3" aria-hidden="true" />}
        </button>
      </div>
    </div>
  );
}

export function StepTip({ tip, color = "indigo" }: { tip: string; color?: "indigo" | "amber" }) {
  const styles = color === "amber"
    ? { background: "rgba(245,158,11,0.08)", border: "1px solid rgba(245,158,11,0.18)", color: "var(--amber)" }
    : { background: "rgba(99,102,241,0.08)", border: "1px solid rgba(99,102,241,0.18)", color: "#818cf8" };

  return (
    <div className="flex items-start gap-2 text-xs rounded-md px-3 py-2.5" style={styles}>
      <Zap className="w-3 h-3 flex-shrink-0 mt-0.5" aria-hidden="true" />
      <span>{tip}</span>
    </div>
  );
}

/* Resalta los nombres de opción entre comillas (como los resaltados en negrita del
   PDF oficial) sin inventar pantallas. */
export function emphasize(text: string) {
  return text.split(/(".*?")/g).map((part, i) =>
    part.length > 1 && part.startsWith('"') && part.endsWith('"') ? (
      <span key={i} className="font-bold" style={{ color: "var(--blue-lg)" }}>{part.slice(1, -1)}</span>
    ) : (
      <span key={i}>{part}</span>
    ),
  );
}

export interface GuideStep {
  title: string;
  detail?: string;
  tip?: string;
  node?: ReactNode;
}

/* Lista numerada de pasos en texto — mismo formato para Android ("Otra marca")
   e iPhone. Sin capturas de pantalla inventadas. */
export function GuideStepList({ steps }: { steps: GuideStep[] }) {
  return (
    <ol className="space-y-2">
      {steps.map((step, i) => (
        <li
          key={i}
          className="flex gap-3 rounded-xl p-3.5"
          style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}
        >
          <span
            className="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full text-[11px] font-bold"
            style={{ background: "rgba(13,148,136,0.12)", color: "var(--blue-lg)", border: "1px solid rgba(13,148,136,0.25)" }}
          >
            {i + 1}
          </span>
          <div className="min-w-0 flex-1 space-y-1.5">
            <p className="text-sm font-semibold leading-snug" style={{ color: "var(--text-primary)" }}>
              {emphasize(step.title)}
            </p>
            {step.detail && (
              <p className="text-xs leading-relaxed" style={{ color: "var(--text-muted)" }}>{step.detail}</p>
            )}
            {step.node}
            {step.tip && <StepTip tip={step.tip} color="amber" />}
          </div>
        </li>
      ))}
    </ol>
  );
}
