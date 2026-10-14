"use client";

import { useId, useState } from "react";
import { Checkbox } from "primereact/checkbox";
import { Check, ChevronDown, Clock, Server } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  clockReasonLabel, describeDeviceSkew, describePcServerSkew, exceedsThreshold, formatDateTime,
  REASON_LABEL, viewExceedsThreshold, type ClockView, type DetailRowView, type DetailsView,
} from "@/lib/device-details";
import { ClockSkewWarning } from "./DeviceClockLine";

const CAPTION = "m-0 text-fx-label uppercase text-fx-text-3";

/**
 * Ficha técnica completa (metadatos-dispositivo §10.2 y §10.6): mediciones de
 * reloj, las filas con valor o "No disponible" + motivo visible (no solo
 * tooltip) y, plegadas al final, las que no aplican a la plataforma.
 *
 * `readOnly` (detalle del caso): sin casilla editable; el estado del SSID se
 * dice en texto.
 */
export function DeviceDetailsPanel({
  view, thresholdSeconds, readOnly = false, onIncludeSsidChange, ssidSaving = false,
}: {
  view: DetailsView;
  thresholdSeconds: number;
  readOnly?: boolean;
  onIncludeSsidChange?: (include: boolean) => void;
  ssidSaving?: boolean;
}) {
  const uid = useId();
  const [naOpen, setNaOpen] = useState(false);
  const shown = view.rows.filter(r => r.reason !== "not_applicable");
  const notApplicable = view.rows.filter(r => r.reason === "not_applicable");
  const readAt = formatDateTime(view.readAt);
  const exceeded = viewExceedsThreshold(view, thresholdSeconds);
  const ssidId = `${uid}-ssid`;
  const naId = `${uid}-na`;

  return (
    <div className="space-y-5 text-fx-body-sm">
      <p className="m-0 text-xs text-fx-text-2">
        {readAt ? <>Lectura del {readAt}</> : "Lectura de esta sesión"}
        {view.agentVersion && <> · Tatana <span translate="no">v{view.agentVersion}</span></>}
        <br />
        <span className="text-fx-text-3">
          Id. de conexión ({view.platform === "ios" ? "UDID" : "ADB"}):{" "}
          <span translate="no" className="break-all font-mono">{view.connectionId}</span>
        </span>
      </p>

      {/* ── Reloj ── */}
      <section aria-labelledby={`${uid}-clock`} className="space-y-2.5">
        <h3 id={`${uid}-clock`} className={CAPTION}>Reloj del dispositivo</h3>
        {exceeded && <ClockSkewWarning thresholdSeconds={thresholdSeconds} />}
        <dl className="m-0 space-y-2">
          <ClockRow clock={view.clockStart} label="Al inicio" thresholdSeconds={thresholdSeconds} />
          {view.clockClose && <ClockRow clock={view.clockClose} label="Al generar" thresholdSeconds={thresholdSeconds} />}
          {view.source === "case" && <PcServerRow view={view} />}
        </dl>
      </section>

      {/* ── Ficha ── */}
      <section aria-labelledby={`${uid}-fields`} className="space-y-1">
        <h3 id={`${uid}-fields`} className={CAPTION}>Datos del dispositivo</h3>
        <dl className="m-0">
          {shown.map(r => (
            <FieldRow key={r.key} row={r}>
              {r.key === "wifi_ssid" && view.ssid && view.wifiConnected && (
                readOnly ? (
                  <p className="m-0 mt-1 text-xs text-fx-text-3">
                    {view.includeSsid ? "Se incluye en el informe." : "No se incluye en el informe."}
                  </p>
                ) : (
                  <div className="mt-1.5 flex items-start gap-2">
                    <Checkbox
                      inputId={ssidId}
                      checked={view.includeSsid}
                      disabled={ssidSaving}
                      onChange={e => onIncludeSsidChange?.(!!e.checked)}
                      icon={<Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />}
                      className="mt-0.5"
                    />
                    <label htmlFor={ssidId} className="cursor-pointer text-xs text-fx-text">
                      Incluir el nombre de la red en el informe
                      {ssidSaving && <span className="text-fx-text-3"> · guardando…</span>}
                    </label>
                  </div>
                )
              )}
            </FieldRow>
          ))}
        </dl>

        {notApplicable.length > 0 && (
          <div className="pt-1">
            <button
              type="button"
              onClick={() => setNaOpen(o => !o)}
              aria-expanded={naOpen}
              aria-controls={naId}
              className="-mx-2 flex w-[calc(100%+1rem)] min-h-8 cursor-pointer items-center gap-2 rounded-fx-md border-0 bg-transparent px-2 text-left text-xs text-fx-text-2 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-2 hover:text-fx-text fx-focus-ring [@media(pointer:coarse)]:min-h-11"
            >
              <span className="flex-1">No aplica a esta plataforma ({notApplicable.length})</span>
              <ChevronDown
                className={cn("h-4 w-4 transition-transform duration-fx-fast ease-fx motion-reduce:transition-none", naOpen && "rotate-180")}
                aria-hidden="true"
              />
            </button>
            <ul id={naId} hidden={!naOpen} className="m-0 list-none p-0 pt-1 text-xs text-fx-text-3">
              {notApplicable.map(r => <li key={r.key} className="py-0.5">{r.label}</li>)}
            </ul>
          </div>
        )}
      </section>
    </div>
  );
}

/** Una fila etiqueta/valor; sin dato: "No disponible" tenue + el motivo en texto visible. */
function FieldRow({ row, children }: { row: DetailRowView; children?: React.ReactNode }) {
  return (
    <div className="grid grid-cols-1 gap-x-4 gap-y-0.5 border-b border-fx-border py-2 last:border-0 sm:grid-cols-[11rem_minmax(0,1fr)]">
      <dt className="text-xs text-fx-text-3">{row.label}</dt>
      <dd className="m-0 min-w-0">
        {row.value !== null ? (
          <span
            translate={row.mono ? "no" : undefined}
            className={cn("block text-fx-text", row.mono ? "break-all font-mono text-xs" : "break-words")}
          >
            {row.value}
          </span>
        ) : (
          <>
            <span className="block text-fx-text-3">No disponible</span>
            <span className="block text-xs text-fx-text-3">{row.reason ? REASON_LABEL[row.reason] : null}</span>
          </>
        )}
        {children}
      </dd>
    </div>
  );
}

function ClockRow({ clock, label, thresholdSeconds }: { clock: ClockView | null; label: string; thresholdSeconds: number }) {
  const when = formatDateTime(clock?.recordedAt, true);
  const ok = clock && clock.deviceMinusPcMs !== null;
  const over = ok && exceedsThreshold(clock.deviceMinusPcMs, thresholdSeconds);
  // dt/dd hijos directos del <div> agrupador (HTML válido): el ícono va dentro del dt.
  return (
    <div>
      <dt className="flex items-center gap-2.5 text-xs text-fx-text-3">
        <Clock className={cn("h-3.5 w-3.5 shrink-0", over ? "text-fx-warning" : "text-fx-text-3")} aria-hidden="true" />
        <span>{label}{when && <> · <time dateTime={clock?.recordedAt ?? undefined}>{when}</time></>}</span>
      </dt>
      <dd className={cn("m-0 pl-6", ok ? "text-fx-text" : "text-fx-text-3")}>
        {ok ? describeDeviceSkew(clock.deviceMinusPcMs!, clock.marginMs) : `No disponible (${clockReasonLabel(clock?.reason)})`}
      </dd>
    </div>
  );
}

/** PC↔servidor: solo con la ficha guardada (lo calcula la API). Se muestra la de inicio y, si hay, la de cierre. */
function PcServerRow({ view }: { view: DetailsView }) {
  const measures = [view.clockStart, view.clockClose].filter((c): c is ClockView => !!c);
  if (measures.length === 0) return null;
  const text = (c: ClockView) => {
    if (c.pcMinusServerMs !== null) return describePcServerSkew(c.pcMinusServerMs, c.pcServerMarginMs);
    return c.pcServerReason === "invalid" ? "No disponible (la medición no fue válida)" : "No medida";
  };
  return (
    <div>
      <dt className="flex items-center gap-2.5 text-xs text-fx-text-3">
        <Server className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        Esta PC y el servidor de Factum
      </dt>
      {measures.map(c => (
        <dd key={c.kind} className={cn("m-0 pl-6", c.pcMinusServerMs !== null ? "text-fx-text" : "text-fx-text-3")}>
          {measures.length > 1 && <span className="text-fx-text-3">{c.kind === "start" ? "Al inicio: " : "Al generar: "}</span>}
          {text(c)}
        </dd>
      ))}
    </div>
  );
}
