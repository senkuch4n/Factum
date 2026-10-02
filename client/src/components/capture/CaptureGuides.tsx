import { Info, Loader2, Wifi } from "lucide-react";

const ENTER = "motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]";

/**
 * Guía de conexión AirPlay (grabación "airplay" y respaldo de screenshot).
 * Es instrucción, no advertencia: tono `info`.
 */
export function AirplayConnectGuide({ airplayReceiverName }: { airplayReceiverName?: string | null }) {
  const steps = [
    "Deslizá esquina superior derecha del iPhone",
    'Tocá "Espejo de pantalla"',
    `Seleccioná "${airplayReceiverName ?? "…"}"`,
  ];
  return (
    <div className={`space-y-2.5 rounded-fx-lg border border-fx-info bg-fx-info-soft p-3 ${ENTER}`}>
      <p className="m-0 text-fx-label uppercase text-fx-info">Conectar el iPhone por AirPlay:</p>
      <ol className="m-0 grid grid-cols-3 gap-2 p-0">
        {steps.map((label, i) => (
          <li key={i} className="flex flex-col items-center gap-1.5 text-center">
            <span
              aria-hidden="true"
              className="flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md border border-fx-info bg-fx-surface-1 text-xs font-bold text-fx-info"
            >
              {i + 1}
            </span>
            <span className="text-xs leading-tight text-fx-text-2">{label}</span>
          </li>
        ))}
      </ol>
      {airplayReceiverName ? (
        <p className="m-0 flex items-center gap-2 text-xs text-fx-text-2">
          <Wifi className="h-3.5 w-3.5 shrink-0 text-fx-info" aria-hidden="true" />
          <span>
            Nombre del receptor:{" "}
            <span translate="no" className="font-mono font-bold text-fx-text">{airplayReceiverName}</span>
          </span>
        </p>
      ) : (
        <p role="status" className="m-0 flex items-center gap-2 text-xs text-fx-text-2">
          <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin text-fx-info" aria-hidden="true" />
          Iniciando receptor AirPlay…
        </p>
      )}
    </div>
  );
}

/**
 * Guía "Para detener la grabación" del modo nativo del iPhone (on_device).
 * Ilustraciones en `currentColor`: `text-fx-info` en el `<svg>`, y el rojo de
 * grabación/stop en `text-fx-danger` sobre el elemento puntual.
 */
export function OnDeviceStopGuide() {
  const steps = [
    {
      label: "Deslizá esquina superior derecha",
      icon: (
        <svg viewBox="0 0 20 20" className="h-3.5 w-3.5 text-fx-info" fill="none" aria-hidden="true">
          <rect x="3" y="1" width="14" height="18" rx="3" stroke="currentColor" strokeWidth="1.4" />
          <path d="M7 5 L10 2 L13 5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
          <line x1="10" y1="2" x2="10" y2="8" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
        </svg>
      ),
    },
    {
      label: "Tocá el botón de grabación",
      icon: (
        <svg viewBox="0 0 20 20" className="h-3.5 w-3.5 text-fx-info" fill="none" aria-hidden="true">
          <circle cx="10" cy="10" r="8" stroke="currentColor" strokeWidth="1.4" />
          <circle cx="10" cy="10" r="4.5" fill="currentColor" className="text-fx-danger" />
        </svg>
      ),
    },
    {
      label: "Confirmá Detener en el iPhone",
      icon: (
        <svg viewBox="0 0 20 20" className="h-3.5 w-3.5 text-fx-danger" fill="none" aria-hidden="true">
          <circle cx="10" cy="10" r="8" stroke="currentColor" strokeWidth="1.4" />
          <rect x="6.5" y="6.5" width="7" height="7" rx="1.5" fill="currentColor" />
        </svg>
      ),
    },
  ];
  return (
    <div className={`space-y-2.5 rounded-fx-lg border border-fx-info bg-fx-info-soft p-3 ${ENTER}`}>
      <p className="m-0 text-fx-label uppercase text-fx-info">Para detener la grabación:</p>
      <ol className="m-0 grid grid-cols-3 gap-2 p-0">
        {steps.map(({ icon, label }, i) => (
          <li key={i} className="flex flex-col items-center gap-1.5 text-center">
            <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-fx-md border border-fx-info bg-fx-surface-1">
              {icon}
            </span>
            <span className="text-xs leading-tight text-fx-text-2">{label}</span>
          </li>
        ))}
      </ol>
      <p className="m-0 flex items-center gap-1.5 border-t border-fx-info pt-2 text-xs font-medium text-fx-text">
        <Info className="h-3.5 w-3.5 shrink-0 text-fx-info" aria-hidden="true" />
        <span>Luego tocá <strong>«Detener»</strong> acá para extraer el video</span>
      </p>
    </div>
  );
}
