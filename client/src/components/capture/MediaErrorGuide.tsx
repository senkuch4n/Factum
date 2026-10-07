import { Monitor, Settings } from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";

/** Botonera al pie del contenido (depende de la fase, por eso no va en `footer`). */
export const MEDIA_ACTIONS = "flex items-center justify-between gap-3 border-t border-fx-border bg-fx-surface-1 px-4 py-3";

/** Guía de error de cámara (la comparten la webcam y la cámara externa). */
export function MediaErrorGuide({ guide }: { guide: { title: string; steps: string[]; extra?: string } }) {
  return (
    <div className="space-y-4 p-5 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
      <FxBanner tone="error">{guide.title}</FxBanner>
      <div>
        <p className="m-0 mb-2.5 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
          <Settings className="h-3.5 w-3.5" aria-hidden="true" /> Cómo solucionarlo
        </p>
        <ol className="m-0 list-none space-y-2 p-0">
          {guide.steps.map((step, i) => (
            <li key={i} className="flex gap-3 text-fx-body-sm text-fx-text-2">
              <span aria-hidden="true" className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-fx-surface-3 text-xs font-bold text-fx-text">
                {i + 1}
              </span>
              {step}
            </li>
          ))}
        </ol>
        {guide.extra && (
          <p className="m-0 mt-3 flex gap-2 rounded-fx-md bg-fx-surface-2 p-3 text-xs text-fx-text-2">
            <Monitor className="mt-0.5 h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
            {guide.extra}
          </p>
        )}
      </div>
    </div>
  );
}
