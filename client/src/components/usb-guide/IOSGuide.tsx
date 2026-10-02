"use client";

import { Button } from "primereact/button";
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
    <div className="space-y-5 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
      <div className="flex items-center gap-2">
        {/* contenido de imagen: SVG monocromo, se invierte en oscuro */}
        <img src="/apple.svg" alt="" className="h-5 w-5 object-contain opacity-70 dark:invert" />
        <div>
          <h3 className="text-fx-body font-bold text-fx-text">iPhone (iOS)</h3>
          <p className="text-xs text-fx-text-3">Preparación del dispositivo</p>
        </div>
        <span className="ml-auto text-xs font-semibold text-fx-text-3">{IOS_STEPS.length} pasos</span>
      </div>

      <div>
        <p className="mb-2.5 text-fx-label uppercase text-fx-text-2">En la PC y en el iPhone</p>
        <GuideStepList steps={steps} />
      </div>

      <Button
        icon={<Check className="h-4 w-4" aria-hidden="true" />}
        label="Listo, conectar el iPhone"
        onClick={onDone}
        className="w-full"
      />
    </div>
  );
}
