"use client";

import { motion } from "framer-motion";
import { Check } from "lucide-react";
import { IOS_STEPS } from "./data";
import { GuideStepList, TerminalBlock, type GuideStep } from "./Mockups";

export function IOSGuide({ onDone }: { onDone: () => void }) {
  const steps: GuideStep[] = IOS_STEPS.map((s) => ({
    title: s.title,
    detail: s.detail,
    tip: s.tip,
    node: s.type === "terminal" ? <TerminalBlock command={s.command} /> : undefined,
  }));

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <img src="/apple.svg" alt="" className="h-5 w-5 object-contain opacity-70 dark:opacity-70 dark:invert" />
        <div>
          <h2 className="text-sm font-bold" style={{ color: "var(--text-primary)" }}>iPhone (iOS)</h2>
          <p className="text-xs" style={{ color: "var(--text-muted)" }}>Preparación del dispositivo</p>
        </div>
        <span className="ml-auto text-xs font-semibold" style={{ color: "var(--text-muted)" }}>
          {IOS_STEPS.length} pasos
        </span>
      </div>

      <div>
        <p className="section-label mb-2.5">En la PC y en el iPhone</p>
        <GuideStepList steps={steps} />
      </div>

      <motion.button className="btn-success w-full" onClick={onDone} whileTap={{ scale: 0.98 }}>
        <Check className="h-4 w-4" aria-hidden="true" /> Listo, conectar el iPhone
      </motion.button>
    </div>
  );
}
