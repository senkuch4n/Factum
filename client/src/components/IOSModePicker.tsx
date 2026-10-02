"use client";

import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { Tag } from "primereact/tag";
import { Smartphone } from "lucide-react";

export type IOSRecordMode = "video_only" | "with_mic" | "on_device" | "airplay";

/* Ilustraciones en currentColor: toman `text-fx-text-2` del contenedor; los
   elementos de grabación (rojo) llevan `text-fx-danger` puntual. */
const IOS_MODES: Array<{
  mode: IOSRecordMode;
  badge: { label: string; severity: "info" | "secondary" } | null;
  title: string;
  subtitle: string;
  desc: string;
  illustration: React.ReactNode;
}> = [
  {
    mode: "video_only",
    badge: null,
    title: "Solo pantalla", subtitle: "Sin audio",
    desc: "Grabación de pantalla estándar. Ideal para capturas visuales sin necesidad de sonido.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="h-10 w-14" aria-hidden="true">
        <rect x="2" y="1" width="20" height="34" rx="3" stroke="currentColor" strokeWidth="1.4" fill="currentColor" fillOpacity="0.08"/>
        <rect x="4.5" y="5" width="15" height="23" rx="1.5" fill="currentColor" fillOpacity="0.1"/>
        <circle cx="12" cy="31.5" r="1.5" fill="currentColor" fillOpacity="0.35"/>
        <circle cx="17.5" cy="8" r="2.5" fill="currentColor" className="text-fx-danger"/>
        <path d="M36 15 L40 12 L40 24 L36 21" stroke="currentColor" strokeWidth="1.3" strokeLinejoin="round" fill="currentColor" fillOpacity="0.1"/>
        <path d="M42 14 Q46 18 42 22" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" fill="none"/>
        <line x1="33" y1="11" x2="49" y2="27" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" className="text-fx-danger"/>
      </svg>
    ),
  },
  {
    mode: "with_mic",
    badge: null,
    title: "Pantalla + micrófono de PC", subtitle: "Audio ambiente",
    desc: "El micrófono de esta computadora capta el audio del altavoz del iPhone.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="h-10 w-14" aria-hidden="true">
        <rect x="1" y="5" width="14" height="24" rx="2.5" stroke="currentColor" strokeWidth="1.3" fill="currentColor" fillOpacity="0.07"/>
        <rect x="3" y="8" width="10" height="16" rx="1" fill="currentColor" fillOpacity="0.1"/>
        <circle cx="13" cy="7.5" r="2" fill="currentColor" className="text-fx-danger"/>
        <path d="M18 14 Q22 10 22 17 Q22 24 18 20" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" fill="none"/>
        <path d="M24 11 Q30 6 30 17 Q30 28 24 23" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" fill="none" opacity="0.5"/>
        <rect x="37" y="5" width="8" height="14" rx="4" stroke="currentColor" strokeWidth="1.3" fill="currentColor" fillOpacity="0.15"/>
        <path d="M33 18 Q33 28 41 28 Q49 28 49 18" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" fill="none"/>
        <line x1="41" y1="28" x2="41" y2="33" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round"/>
        <line x1="38" y1="33" x2="44" y2="33" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round"/>
      </svg>
    ),
  },
  {
    mode: "on_device",
    badge: { label: "Recomendado", severity: "info" },
    title: "Grabación nativa del iPhone", subtitle: "Audio del sistema incluido",
    desc: "Captura audio de apps (WhatsApp, videos). El perito activa la grabación desde el Centro de Control.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="h-10 w-14" aria-hidden="true">
        <rect x="8" y="1" width="22" height="36" rx="3.5" stroke="currentColor" strokeWidth="1.4" fill="currentColor" fillOpacity="0.06"/>
        <rect x="10.5" y="5" width="17" height="26" rx="2" fill="currentColor" fillOpacity="0.08"/>
        <circle cx="19" cy="34" r="1.5" fill="currentColor" fillOpacity="0.4"/>
        <rect x="15" y="3" width="8" height="3.5" rx="1.75" fill="currentColor" fillOpacity="0.25"/>
        <rect x="12" y="8" width="5" height="5" rx="1" fill="currentColor" fillOpacity="0.18"/>
        <rect x="20" y="8" width="5" height="5" rx="1" fill="currentColor" fillOpacity="0.18"/>
        <rect x="12" y="15" width="5" height="5" rx="1" fill="currentColor" fillOpacity="0.18"/>
        <circle cx="22.5" cy="17.5" r="5.5" stroke="currentColor" strokeWidth="1.5"/>
        <circle cx="22.5" cy="17.5" r="3" fill="currentColor" className="text-fx-danger"/>
        <circle cx="22.5" cy="17.5" r="7.5" stroke="currentColor" strokeWidth="0.6" opacity="0.3"/>
        <circle cx="46" cy="7" r="3.5" fill="currentColor" className="text-fx-danger"/>
        <circle cx="46" cy="7" r="5.5" stroke="currentColor" strokeWidth="0.8" opacity="0.28" className="text-fx-danger"/>
      </svg>
    ),
  },
  {
    mode: "airplay",
    badge: { label: "30 FPS", severity: "secondary" },
    title: "Espejo AirPlay", subtitle: "30 fps · Audio nativo del iPhone",
    desc: "El iPhone duplica su pantalla a esta PC por WiFi. Requiere misma red. Alta calidad sin cables.",
    illustration: (
      <svg viewBox="0 0 56 40" fill="none" className="h-10 w-14" aria-hidden="true">
        <rect x="1" y="4" width="30" height="22" rx="2.5" stroke="currentColor" strokeWidth="1.3" fill="currentColor" fillOpacity="0.07"/>
        <rect x="3.5" y="6.5" width="25" height="17" rx="1.5" fill="currentColor" fillOpacity="0.1"/>
        <line x1="13" y1="26" x2="18" y2="26" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round"/>
        <line x1="15.5" y1="23.5" x2="15.5" y2="26" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round"/>
        <rect x="34" y="6" width="14" height="24" rx="2.5" stroke="currentColor" strokeWidth="1.3" fill="currentColor" fillOpacity="0.07"/>
        <rect x="36" y="9" width="10" height="17" rx="1" fill="currentColor" fillOpacity="0.1"/>
        <circle cx="41" cy="28.5" r="1.2" fill="currentColor" fillOpacity="0.4"/>
        <path d="M22 15 L27 12 L27 18 Z" fill="currentColor" fillOpacity="0.7"/>
        <path d="M20 11 Q33 15 20 19" stroke="currentColor" strokeWidth="0.8" strokeLinecap="round" fill="none" opacity="0.4"/>
      </svg>
    ),
  },
];

/**
 * Selector del modo de grabación de iOS: Dialog estándar (es una elección, no
 * medios). Escape, la X y el click en la máscara cancelan (DP4 A).
 */
export function IOSModePicker({
  open, onSelect, onCancel,
}: {
  open: boolean;
  onSelect: (mode: IOSRecordMode) => void;
  onCancel: () => void;
}) {
  return (
    <Dialog
      visible={open}
      onHide={onCancel}
      dismissableMask
      draggable={false}
      resizable={false}
      // Ancho y pantalla completa en < sm por pt (el className de props pierde
      // contra el pt global y en unstyled maskClassName no se aplica).
      pt={{
        root: { className: "w-[min(28rem,100%)] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0" },
        mask: { className: "max-sm:p-0" },
        content: { className: "max-sm:flex-1" },
      }}
      header={
        <span className="flex items-center gap-3">
          <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md bg-fx-surface-3 text-fx-text-2">
            <Smartphone className="h-4 w-4" aria-hidden="true" />
          </span>
          Modo de grabación iOS
        </span>
      }
      footer={<Button severity="secondary" label="Cancelar" onClick={onCancel} className="w-full min-h-11" />}
    >
      <p className="m-0 text-fx-body-sm text-fx-text-2">Seleccioná cómo capturar el audio durante la inspección</p>
      <ul className="m-0 mt-4 list-none space-y-2.5 p-0">
        {IOS_MODES.map(({ mode, badge, title, subtitle, desc, illustration }) => (
          <li key={mode}>
            <button
              type="button"
              onClick={() => onSelect(mode)}
              className="fx-card fx-card-interactive flex w-full items-center gap-3.5 p-3.5 text-left"
            >
              <span className="flex h-11 w-16 shrink-0 items-center justify-center overflow-hidden rounded-fx-md border border-fx-border bg-fx-surface-2 text-fx-text-2">
                {illustration}
              </span>
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-center gap-1.5">
                  <span className="text-fx-body-sm font-semibold leading-tight text-fx-text">{title}</span>
                  {badge && <Tag severity={badge.severity} value={badge.label} />}
                </span>
                <span className="mt-0.5 block text-xs font-semibold text-fx-text-2">{subtitle}</span>
                <span className="mt-1 block text-xs leading-relaxed text-fx-text-3">{desc}</span>
              </span>
            </button>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}
