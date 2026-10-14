"use client";

import { useEffect, useId, useState } from "react";
import { ChevronRight, Clock, ClockAlert, ClockCheck, Film } from "lucide-react";
import { api, type CaseTimestamp, type DeviceDetails, type RecordingSession } from "@/lib/api";
import { describeEventSkew, formatDuration } from "@/lib/device-details";
import { reasonText, sessionSummary, trackLabel } from "@/lib/recording-session";
import { sealText } from "@/components/capture/EvidenceTray";
import { CopyButton } from "@/components/feedback/CopyButton";
import { cn } from "@/lib/utils";

interface Props {
  caseId: string;
  session: RecordingSession;
  deviceDetails?: DeviceDetails | null;
  className?: string;
}

/** "14/10/2026 14:02:10" en hora local. */
function dateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleString("es-AR", { hour12: false });
}

/** "14:02:10" en hora local. */
function time(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleTimeString("es-AR", { hour12: false });
}

/** "14/10/2026 14:02:10 a 14:32:11" (el fin sin fecha si es el mismo día, como el informe §8.4). */
function span(from: string, to: string | null | undefined): string {
  if (!to) return `desde ${dateTime(from)}`;
  const a = new Date(from);
  const b = new Date(to);
  const sameDay = a.toDateString() === b.toDateString();
  return `${dateTime(from)} a ${sameDay ? time(to) : dateTime(to)}`;
}

/** Línea del reloj (§8.4): con la medición de la ficha (#79), la diferencia; si no, la frase sola. */
function clockLine(details: DeviceDetails | null | undefined): string {
  const base = "Las horas de esta sección corresponden al reloj de la estación de trabajo.";
  const m = typeof details?.clock_start?.device_minus_pc_ms === "number" ? details.clock_start
    : typeof details?.clock_close?.device_minus_pc_ms === "number" ? details.clock_close : null;
  if (!m || typeof m.device_minus_pc_ms !== "number") return base;
  const when = m.kind === "close" ? "cierre" : "inicio";
  const skew = describeEventSkew(m.device_minus_pc_ms, m.margin_ms);
  return `${base} Al ${when} del caso: ${skew.charAt(0).toLowerCase()}${skew.slice(1)}. Tenelo en cuenta para comparar la hora que se ve en la pantalla del celular dentro del video.`;
}

/**
 * "Registro audiovisual" (grabacion-sesion-completa §9.7) en el resultado y en
 * el detalle del caso: resumen y un desplegable con tramos, huecos y marcas.
 * Solo se muestra si la sesión tiene tramos guardados.
 */
export function SessionRecordPanel({ caseId, session, deviceDetails, className }: Props) {
  const headingId = useId();
  const summary = sessionSummary(session);
  const [seals, setSeals] = useState<Record<string, CaseTimestamp> | null>(null);
  const [sealing, setSealing] = useState(false);

  useEffect(() => {
    if (summary.segments === 0) return;
    const ctrl = new AbortController();
    api.getCaseTimestamps(caseId, ctrl.signal)
      .then(res => {
        setSealing(res.enabled || res.evidence.some(r => r.timestamp));
        const map: Record<string, CaseTimestamp> = {};
        res.evidence.forEach(r => { if (r.timestamp) map[r.filename] = r.timestamp; });
        setSeals(map);
      })
      .catch(() => { /* backend sin sellado o sin respuesta: el panel queda sin sellos */ });
    return () => ctrl.abort();
  }, [caseId, summary.segments]);

  if (summary.segments === 0) return null;

  const saved = session.segments
    .filter(s => s.state === "saved")
    .sort((a, b) => a.started_at.localeCompare(b.started_at) || (a.track === "pantalla" ? -1 : 1));
  const gaps = [...session.gaps].sort((a, b) => a.from.localeCompare(b.from));
  const marks = [...session.marks].sort((a, b) => a.at.localeCompare(b.at));
  const sealed = seals ? saved.filter(s => seals[s.filename]?.status === "granted").length : 0;

  const parts = [
    `${summary.segments} ${summary.segments === 1 ? "tramo" : "tramos"}`,
    formatDuration(summary.totalSeconds),
    `${summary.gaps} ${summary.gaps === 1 ? "hueco" : "huecos"}`,
    ...(sealing && seals ? [`sellos ${sealed}/${summary.segments}`] : []),
  ];

  return (
    <section aria-labelledby={headingId} className={cn("rounded-fx-lg border border-fx-border bg-fx-surface-2 p-4 space-y-3", className)}>
      <h3 id={headingId} className="m-0 flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
        <Film className="h-3.5 w-3.5" aria-hidden="true" /> Registro audiovisual
      </h3>
      <p className="m-0 text-fx-body-sm font-medium text-fx-text">{parts.join(" · ")}</p>

      <details className="group">
        <summary
          className={cn(
            "flex min-h-11 cursor-pointer list-none items-center gap-1.5 rounded-fx-md text-fx-body-sm font-medium text-fx-text-2",
            "hover:text-fx-text fx-focus-ring [&::-webkit-details-marker]:hidden",
          )}
        >
          <ChevronRight
            className="h-3.5 w-3.5 transition-transform duration-fx-fast ease-fx group-open:rotate-90 motion-reduce:transition-none"
            aria-hidden="true"
          />
          Ver tramos, huecos y marcas
        </summary>

        <div className="mt-2 space-y-4">
          <div>
            <h4 className="m-0 mb-1.5 text-xs font-semibold text-fx-text-2">Tramos</h4>
            <ul className="m-0 list-none space-y-2 p-0">
              {saved.map(s => {
                const seal = seals?.[s.filename] ?? null;
                const SealIcon = seal?.status === "granted" ? ClockCheck : seal?.status === "failed" ? ClockAlert : Clock;
                return (
                  <li key={s.id} className="rounded-fx-md border border-fx-border bg-fx-surface-1 px-3 py-2 text-xs">
                    <p className="m-0 font-semibold text-fx-text">
                      {trackLabel(s.track)} · tramo {s.index}
                      <span className="font-normal text-fx-text-2">
                        {" · "}{span(s.started_at, s.stopped_at)} ({formatDuration(s.duration_seconds ?? s.recorded_seconds)})
                      </span>
                    </p>
                    <p className="m-0 mt-0.5 text-fx-text-2">
                      {s.validation === "incomplete"
                        ? `Archivo incompleto: el video dura ${formatDuration(s.duration_seconds ?? 0)} de ${formatDuration(s.recorded_seconds)} grabados`
                        : "Archivo completo"}
                      {s.recovered ? " · recuperado tras el cierre de Tatana" : ""}
                    </p>
                    <p translate="no" className="m-0 mt-0.5 break-all font-mono text-fx-text-3">{s.filename}</p>
                    {s.sha256 && (
                      <div className="mt-1 flex flex-wrap items-center gap-2">
                        <span translate="no" className="select-all font-mono text-fx-text-2" title={s.sha256}>
                          SHA-256 {s.sha256.slice(0, 16)}…
                        </span>
                        <CopyButton text={s.sha256} label={`Copiar SHA-256 del tramo ${s.index}`} />
                      </div>
                    )}
                    {seal && (
                      <p className="m-0 mt-1 flex items-start gap-1.5 text-fx-text-2">
                        <SealIcon
                          className={cn("mt-px h-3.5 w-3.5 shrink-0", seal.status === "granted" ? "text-fx-success" : seal.status === "failed" ? "text-fx-warning" : "text-fx-text-3")}
                          aria-hidden="true"
                        />
                        {sealText(seal)}
                      </p>
                    )}
                  </li>
                );
              })}
            </ul>
          </div>

          <div>
            <h4 className="m-0 mb-1.5 text-xs font-semibold text-fx-text-2">Huecos</h4>
            {gaps.length === 0 ? (
              <p className="m-0 text-xs text-fx-text-2">Sin huecos: la grabación fue continua entre el primer y el último tramo.</p>
            ) : (
              <ul className="m-0 list-disc space-y-1 pl-5 text-xs text-fx-text-2">
                {gaps.map(g => (
                  <li key={g.id}>
                    {g.to
                      ? `${span(g.from, g.to)} (${formatDuration((Date.parse(g.to) - Date.parse(g.from)) / 1000)})`
                      : `Desde ${dateTime(g.from)}`}
                    {" — "}{trackLabel(g.track)}: {reasonText(g.reason)}
                    {g.segment_lost ? " — el tramo en curso no se pudo recuperar" : ""}
                    {!g.to ? " (la grabación no se reanudó)" : ""}
                  </li>
                ))}
              </ul>
            )}
          </div>

          {marks.length > 0 && (
            <div>
              <h4 className="m-0 mb-1.5 text-xs font-semibold text-fx-text-2">Marcas de momento</h4>
              <ul className="m-0 list-disc space-y-1 pl-5 text-xs text-fx-text-2">
                {marks.map(m => (
                  <li key={m.id}>
                    {time(m.at)} — tramo {m.segment_index}, minuto {minuteOf(m.offset_seconds)} del video —{" "}
                    {m.text ? m.text : m.filename
                      ? <>Captura <span translate="no" className="font-mono">{m.filename}</span></>
                      : "Marca sin texto"}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <p className="m-0 text-xs text-fx-text-3">{clockLine(deviceDetails)}</p>
        </div>
      </details>
    </section>
  );
}

function minuteOf(seconds: number): string {
  const t = Math.max(0, Math.floor(seconds));
  return `${String(Math.floor(t / 60)).padStart(2, "0")}:${String(t % 60).padStart(2, "0")}`;
}
