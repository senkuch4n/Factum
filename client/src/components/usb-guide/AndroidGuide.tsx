"use client";

import { useState } from "react";
import { motion } from "framer-motion";
import { ChevronLeft, Check, Cable, ExternalLink, ZoomIn } from "lucide-react";
import { BRANDS } from "./data";
import { AndroidVersionCard, StepTip, GuideStepList, emphasize } from "./Mockups";
import { Lens } from "@/components/ui/lens";

/* Glow que sigue al cursor dentro de una card (estilo Aceternity Card Spotlight). */
function handleGlowMove(e: React.MouseEvent<HTMLElement>) {
  const r = e.currentTarget.getBoundingClientRect();
  e.currentTarget.style.setProperty("--gx", `${e.clientX - r.left}px`);
  e.currentTarget.style.setProperty("--gy", `${e.clientY - r.top}px`);
}

/* Ficha oficial (páginas del PDF "Guía rápida — Depuración por USB" del GIF).
   Se muestra tal cual para las marcas que están en el documento; "Otra marca"
   no tiene ficha y usa la lista de pasos. */
const SHEETS: Record<string, string> = {
  motorola: "/usb-guide/motorola.png",
  samsung: "/usb-guide/samsung.png",
  xiaomi: "/usb-guide/xiaomi.png",
  huawei: "/usb-guide/huawei.png",
  lg: "/usb-guide/lg.png",
};

export function AndroidGuide({ onDone }: { onDone: () => void }) {
  const [brand, setBrand] = useState<(typeof BRANDS)[number] | null>(null);

  if (!brand) {
    return (
      <div className="space-y-5">
        {/* <AndroidVersionCard /> */}

        <div className="space-y-2.5">
          <p className="section-label">¿De qué marca es el celular?</p>
          <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-3">
            {BRANDS.map((b, i) => (
              <motion.button
                key={b.id}
                className="card card-hover group relative flex flex-col items-center gap-2.5 p-4 text-center"
                onClick={() => setBrand(b)}
                onMouseMove={handleGlowMove}
                initial={{ opacity: 0, scale: 0.94 }}
                animate={{ opacity: 1, scale: 1 }}
                transition={{ delay: i * 0.05 }}
                whileHover={{ scale: 1.02 }}
                whileTap={{ scale: 0.98 }}
              >
                <span
                  aria-hidden="true"
                  className="pointer-events-none absolute inset-0 rounded-[10px] opacity-0 transition-opacity duration-200 group-hover:opacity-100"
                  style={{ background: "radial-gradient(150px circle at var(--gx, 50%) var(--gy, 50%), rgba(13,148,136,0.18), transparent 70%)" }}
                />
                <div
                  className="relative flex h-14 w-14 items-center justify-center rounded-xl transition-colors group-hover:border-teal-500/40"
                  style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
                >
                  {b.emoji.startsWith("/") ? (
                    <img src={b.emoji} alt={b.name} className="h-8 w-8 object-contain dark:opacity-80 dark:invert" />
                  ) : (
                    <span className="text-2xl">{b.emoji}</span>
                  )}
                </div>
                <div className="relative">
                  <p
                    className="text-sm font-semibold leading-tight transition-colors group-hover:text-teal-600 dark:group-hover:text-teal-300"
                    style={{ color: "var(--text-primary)" }}
                  >
                    {b.name}
                  </p>
                  <p className="mt-0.5 text-[11px]" style={{ color: "var(--text-muted)" }}>{b.steps.length} pasos</p>
                </div>
              </motion.button>
            ))}
          </div>
        </div>
      </div>
    );
  }

  // Pasos del menú del teléfono vs. avisos que aparecen recién al enchufar el cable.
  const menuSteps = brand.steps.filter(s => !(s as { type?: string }).type);
  const plugSteps = brand.steps.filter(s => !!(s as { type?: string }).type);
  const sheet = SHEETS[brand.id];

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-3">
        <button
          className="flex h-8 w-8 items-center justify-center rounded-md transition-colors"
          style={{ background: "var(--btn-secondary-bg)", border: "1px solid var(--border)" }}
          onClick={() => setBrand(null)}
          aria-label="Volver a la lista de marcas"
        >
          <ChevronLeft className="h-4 w-4" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
        </button>
        <div className="flex items-center gap-2">
          {brand.emoji.startsWith("/") ? (
            <img src={brand.emoji} alt="" className="h-5 w-5 object-contain dark:opacity-80 dark:invert" />
          ) : (
            <span className="text-lg">{brand.emoji}</span>
          )}
          <div>
            <h2 className="text-sm font-bold" style={{ color: "var(--text-primary)" }}>{brand.name}</h2>
            <p className="text-xs" style={{ color: "var(--text-muted)" }}>{brand.note}</p>
          </div>
        </div>
        <span className="ml-auto text-xs font-semibold" style={{ color: "var(--text-muted)" }}>
          {sheet ? "Ficha oficial" : `${menuSteps.length} pasos`}
        </span>
      </div>

      {sheet ? (
        <div>
          <p className="section-label mb-2.5">Guía rápida del GIF — Depuración por USB</p>
          <div className="overflow-hidden rounded-xl bg-white" style={{ border: "1px solid var(--border)" }}>
            <Lens zoomFactor={2.4} lensSize={220}>
              <img src={sheet} alt={`Guía de depuración por USB para ${brand.name}`} className="block w-full" />
            </Lens>
          </div>
          <div className="mt-1.5 flex items-center justify-between text-[11px]" style={{ color: "var(--text-muted)" }}>
            <span className="flex items-center gap-1">
              <ZoomIn className="h-3 w-3" aria-hidden="true" /> Pasá el cursor para acercar
            </span>
            <a
              href={sheet}
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-1 font-medium transition-colors hover:text-[var(--blue-lg)]"
            >
              Abrir en pestaña nueva <ExternalLink className="h-3 w-3" aria-hidden="true" />
            </a>
          </div>
        </div>
      ) : (
        <div>
          <p className="section-label mb-2.5">En el celular del denunciante</p>
          <GuideStepList steps={menuSteps.map(s => ({ title: s.title, detail: s.detail, tip: s.tip }))} />
        </div>
      )}

      {plugSteps.length > 0 && (
        <div
          className="rounded-xl p-3.5"
          style={{ background: "rgba(13,148,136,0.05)", border: "1px solid rgba(13,148,136,0.16)" }}
        >
          <p className="section-label flex items-center gap-1.5" style={{ color: "var(--blue-lg)" }}>
            <Cable className="h-3 w-3" aria-hidden="true" /> Al enchufar el cable, en el celular
          </p>
          <ul className="mt-2.5 space-y-2.5">
            {plugSteps.map((step, i) => (
              <li key={i} className="space-y-1">
                <p className="text-sm font-semibold leading-snug" style={{ color: "var(--text-primary)" }}>
                  {emphasize(step.title)}
                </p>
                {step.detail && (
                  <p className="text-xs leading-relaxed" style={{ color: "var(--text-secondary)" }}>{step.detail}</p>
                )}
                {step.tip && <StepTip tip={step.tip} color="amber" />}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="flex gap-3">
        <button className="btn-secondary" onClick={() => setBrand(null)}>
          <ChevronLeft className="h-4 w-4" aria-hidden="true" /> Otra marca
        </button>
        <motion.button className="btn-success flex-1" onClick={onDone} whileTap={{ scale: 0.98 }}>
          <Check className="h-4 w-4" aria-hidden="true" /> Listo, conectar el celular
        </motion.button>
      </div>
    </div>
  );
}
