"use client";

import type { ReactNode } from "react";
import { useEffect, useRef, useState } from "react";
import { Check, Copy, Terminal, Zap } from "lucide-react";
import { cn } from "@/lib/utils";

/* Piezas de la guía USB. Los campos `mockup`/`dialog` de data.ts quedan como
   datos sin renderizar: no se inventan pantallas del teléfono. */

export function TerminalBlock({ command }: { command: string }) {
  const [copied, setCopied] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);

  function handleCopy() {
    navigator.clipboard.writeText(command).then(() => {
      setCopied(true);
      if (timer.current) clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), 2000);
    }).catch(() => { /* sin portapapeles: el comando es seleccionable (select-all) */ });
  }

  return (
    <div className="overflow-hidden rounded-fx-md border border-fx-border-strong">
      <div className="flex items-center gap-1.5 bg-fx-surface-3 px-3 py-2">
        <Terminal className="h-3.5 w-3.5 text-fx-text-2" aria-hidden="true" />
        <span className="text-fx-label uppercase text-fx-text-2">Terminal</span>
      </div>
      <div className="flex items-start gap-2 bg-fx-bg px-3 py-3">
        <span className="mt-0.5 shrink-0 font-mono text-xs text-fx-text-3" aria-hidden="true">$</span>
        <span translate="no" className="flex-1 break-all font-mono text-xs leading-relaxed text-fx-text select-all">
          {command}
        </span>
        <button
          type="button"
          onClick={handleCopy}
          className="-my-1.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-sm text-fx-text-2 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring"
          aria-label={copied ? "Comando copiado" : "Copiar comando"}
        >
          {copied
            ? <Check className="h-3.5 w-3.5 text-fx-success" strokeWidth={3} aria-hidden="true" />
            : <Copy className="h-3.5 w-3.5" aria-hidden="true" />}
        </button>
        <span role="status" className="sr-only">{copied ? "Comando copiado" : ""}</span>
      </div>
    </div>
  );
}

export function StepTip({ tip, color = "indigo" }: { tip: string; color?: "indigo" | "amber" }) {
  return (
    <div
      className={cn(
        "flex items-start gap-2 rounded-fx-md border px-3 py-2.5 text-xs",
        color === "amber"
          ? "bg-fx-warning-soft border-fx-warning text-fx-warning"
          : "bg-fx-info-soft border-fx-info text-fx-info",
      )}
    >
      <Zap className="mt-0.5 h-3 w-3 shrink-0" aria-hidden="true" />
      <span>{tip}</span>
    </div>
  );
}

/* Resalta los nombres de opción entre comillas (como los resaltados en negrita del
   PDF oficial) sin inventar pantallas. */
export function emphasize(text: string) {
  return text.split(/(".*?")/g).map((part, i) =>
    part.length > 1 && part.startsWith('"') && part.endsWith('"') ? (
      <span key={i} className="font-bold text-fx-text">{part.slice(1, -1)}</span>
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
    <ol className="m-0 space-y-2 p-0">
      {steps.map((step, i) => (
        <li key={i} className="fx-card flex list-none gap-3 p-3.5">
          <span
            className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-fx-surface-3 text-xs font-bold text-fx-text"
            aria-hidden="true"
          >
            {i + 1}
          </span>
          <div className="min-w-0 flex-1 space-y-1.5">
            <p className="text-fx-body-sm font-semibold leading-snug text-fx-text">{emphasize(step.title)}</p>
            {step.detail && <p className="text-xs leading-relaxed text-fx-text-2">{step.detail}</p>}
            {step.node}
            {step.tip && <StepTip tip={step.tip} color="amber" />}
          </div>
        </li>
      ))}
    </ol>
  );
}
