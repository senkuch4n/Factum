"use client";

import { useState } from "react";
import { Button } from "primereact/button";
import { ChevronLeft, Check, Cable, ExternalLink, ZoomIn } from "lucide-react";
import { BRANDS } from "./data";
import { StepTip, GuideStepList, emphasize } from "./Mockups";
import { Lens } from "@/components/ui/lens";

/* Ficha oficial (páginas del PDF "Guía rápida — Depuración por USB").
   Se muestra tal cual para las marcas que están en el documento; "Otra marca"
   no tiene ficha y usa la lista de pasos. */
const SHEETS: Record<string, string> = {
  motorola: "/usb-guide/motorola.png",
  samsung: "/usb-guide/samsung.png",
  xiaomi: "/usb-guide/xiaomi.png",
  huawei: "/usb-guide/huawei.png",
  lg: "/usb-guide/lg.png",
};

const SECTION_LABEL = "mb-2.5 text-fx-label uppercase text-fx-text-2";

function BrandLogo({ src, className }: { src: string; className: string }) {
  return src.startsWith("/")
    ? (
      /* contenido de imagen: logos monocromos, se invierten en oscuro */
      <img src={src} alt="" className={`${className} object-contain dark:opacity-80 dark:invert`} />
    )
    : <span aria-hidden="true" className="text-2xl">{src}</span>;
}

export function AndroidGuide({ onDone }: { onDone: () => void }) {
  const [brand, setBrand] = useState<(typeof BRANDS)[number] | null>(null);

  if (!brand) {
    return (
      <div className="space-y-2.5">
        <p id="android-brands-title" className="text-fx-label uppercase text-fx-text-2">¿De qué marca es el celular?</p>
        <ul aria-labelledby="android-brands-title" className="m-0 grid grid-cols-2 gap-3 p-0 sm:grid-cols-3">
          {BRANDS.map((b) => (
            <li key={b.id} className="list-none">
              <button
                type="button"
                className="fx-card fx-card-interactive flex w-full flex-col items-center gap-2.5 p-4 text-center min-h-11"
                onClick={() => setBrand(b)}
              >
                <span className="flex h-14 w-14 items-center justify-center rounded-fx-lg border border-fx-border bg-fx-surface-2">
                  <BrandLogo src={b.emoji} className="h-8 w-8" />
                </span>
                <span>
                  <span className="block text-fx-body-sm font-semibold leading-tight text-fx-text">{b.name}</span>
                  <span className="mt-0.5 block text-xs text-fx-text-3">{b.steps.length} pasos</span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    );
  }

  // Pasos del menú del teléfono vs. avisos que aparecen recién al enchufar el cable.
  const menuSteps = brand.steps.filter(s => !(s as { type?: string }).type);
  const plugSteps = brand.steps.filter(s => !!(s as { type?: string }).type);
  const sheet = SHEETS[brand.id];

  return (
    <div className="space-y-5 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
      <div className="flex items-center gap-3">
        <Button
          text
          severity="secondary"
          icon={<ChevronLeft className="h-4 w-4" aria-hidden="true" />}
          aria-label="Volver a la lista de marcas"
          onClick={() => setBrand(null)}
        />
        <div className="flex min-w-0 items-center gap-2">
          <BrandLogo src={brand.emoji} className="h-5 w-5" />
          <div className="min-w-0">
            <h3 className="text-fx-body font-bold text-fx-text">{brand.name}</h3>
            <p className="text-xs text-fx-text-3">{brand.note}</p>
          </div>
        </div>
        <span className="ml-auto shrink-0 text-xs font-semibold text-fx-text-3">
          {sheet ? "Ficha oficial" : `${menuSteps.length} pasos`}
        </span>
      </div>

      {sheet ? (
        <div>
          <p className={SECTION_LABEL}>Guía rápida — Depuración por USB</p>
          {/* contenido de imagen: la ficha PNG tiene fondo blanco */}
          <div className="overflow-hidden rounded-fx-lg border border-fx-border bg-white">
            <Lens zoomFactor={2.4} lensSize={220}>
              <img src={sheet} alt={`Guía de depuración por USB para ${brand.name}`} className="block w-full" />
            </Lens>
          </div>
          <div className="mt-1.5 flex flex-wrap items-center justify-between gap-2 text-xs text-fx-text-3">
            <span className="flex items-center gap-1">
              <ZoomIn className="h-3.5 w-3.5" aria-hidden="true" /> Pasá el cursor para acercar
            </span>
            <a
              href={sheet}
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-1 rounded-fx-sm font-medium text-fx-accent-text hover:underline fx-focus-ring"
            >
              Abrir en pestaña nueva <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
            </a>
          </div>
        </div>
      ) : (
        <div>
          <p className={SECTION_LABEL}>En el celular a inspeccionar</p>
          <GuideStepList steps={menuSteps.map(s => ({ title: s.title, detail: s.detail, tip: s.tip }))} />
        </div>
      )}

      {plugSteps.length > 0 && (
        <div className="rounded-fx-lg border border-fx-info bg-fx-info-soft p-4">
          <p className="flex items-center gap-1.5 text-fx-label uppercase text-fx-info">
            <Cable className="h-3.5 w-3.5" aria-hidden="true" /> Al enchufar el cable, en el celular
          </p>
          <ul className="m-0 mt-2.5 space-y-2.5 p-0">
            {plugSteps.map((step, i) => (
              <li key={i} className="list-none space-y-1">
                <p className="text-fx-body-sm font-semibold leading-snug text-fx-text">{emphasize(step.title)}</p>
                {step.detail && <p className="text-xs leading-relaxed text-fx-text-2">{step.detail}</p>}
                {step.tip && <StepTip tip={step.tip} color="amber" />}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="flex flex-col gap-3 sm:flex-row">
        <Button
          severity="secondary"
          icon={<ChevronLeft className="h-4 w-4" aria-hidden="true" />}
          label="Otra marca"
          onClick={() => setBrand(null)}
        />
        <Button
          icon={<Check className="h-4 w-4" aria-hidden="true" />}
          label="Listo, conectar el celular"
          onClick={onDone}
          className="flex-1"
        />
      </div>
    </div>
  );
}
