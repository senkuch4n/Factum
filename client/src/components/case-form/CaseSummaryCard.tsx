"use client";

import { useId, useState } from "react";
import { Button } from "primereact/button";
import { Tag } from "primereact/tag";
import { AlertTriangle, CheckCircle2, ChevronDown, FileText, Hash, Maximize2, User } from "lucide-react";
import { cn } from "@/lib/utils";
import { isImeiMissing } from "@/lib/pericial";
import {
  datosProponente, fechaEnPalabras, fraseIntegracion, isFutureIsoDate, tramiteAnte, type SummarySection,
} from "@/lib/case-summary";
import type { Device } from "@/lib/agent";
import type { DeviceConnection } from "@/hooks/useDeviceConnection";
import type { CaseFormData } from "@/types";
import { SpecRow } from "@/components/SpecRow";
import { CaptureControls } from "@/components/PhoneFrame";
import { ConnectionIndicator, DisconnectedBanner } from "./ConnectionIndicator";
import { ImeiMissingNotice } from "./ImeiMissingNotice";
import { DeviceStage } from "./DeviceStage";

export interface ShotState {
  src: string | null;
  loading: boolean;
  error: string | null;
  stale: boolean;
  disabledReason: string | null;
  showCaptured: boolean;
}

export interface FlashKey { key: string; n: number }

interface Props {
  variant: "aside" | "drawer";
  form: CaseFormData;
  sections: SummarySection[];
  /** Perfil en el índice: solo si está incompleto o tiene errores. */
  showPerfil: boolean;
  onGoToSection: (s: SummarySection) => void;
  /** Sección abierta del acordeón del formulario (se marca en el índice). */
  activeSection: SummarySection["id"] | null;
  flashKey: FlashKey | null;
  device: Device;
  osLabel: string;
  isIOS: boolean;
  imeiDisplay: string;
  imeiManual: boolean;
  connection: DeviceConnection;
  shot: ShotState;
  onRefreshShot: () => void;
  onZoomShot: () => void;
}

const CARATULA_CLAMP = 140;

/** Marcador de dato vacío: placeholder sutil, nunca texto de ejemplo que se confunda con un dato. */
function Empty({ children }: { children: string }) {
  return <span className="font-normal text-fx-text-3">{children}</span>;
}

/** Rótulo de bloque de la ficha (sentence case, tenue: el dato es lo que pesa). */
function Caption({ children, as: As = "p" }: { children: React.ReactNode; as?: "p" | "dt" }) {
  return <As className="m-0 text-xs text-fx-text-3">{children}</As>;
}

/** Destello breve del dato que cambió al salir del campo (nunca por tecla). */
function Flash({ keys, flash }: { keys: string[]; flash: FlashKey | null }) {
  if (!flash || !keys.includes(flash.key)) return null;
  return (
    <span
      key={flash.n}
      aria-hidden="true"
      className="pointer-events-none absolute -inset-x-1.5 -inset-y-0.5 rounded-fx-sm bg-fx-accent-soft motion-safe:animate-[fx-summary-flash_var(--fx-dur-slow)_var(--fx-ease-out)_both] motion-reduce:hidden"
    />
  );
}

/** Fila con destello: el contenido va por encima del fondo animado. */
function Row({ keys, flash, className, children }: { keys: string[]; flash: FlashKey | null; className?: string; children: React.ReactNode }) {
  return (
    <div className={cn("relative isolate", className)}>
      <Flash keys={keys} flash={flash} />
      <div className="relative">{children}</div>
    </div>
  );
}

/** En la ficha lateral, lo secundario se pliega en pantallas bajas (sin scroll interno). */
const TALL_ONLY = "[@media(max-height:959px)]:hidden";
/** Avisos de la ficha en pantallas bajas: el texto secundario pasa a `sr-only`. */
const SHORT_COMPACT = "[@media(max-height:799px)]:py-2 [@media(max-height:799px)]:[&_p+p]:sr-only";

/**
 * Ficha viva de la causa (formulario-causa-rediseno §6.7): cómo va a quedar la
 * causa en el informe, el índice de faltantes y el equipo compacto. No es
 * región viva: solo el resumen de faltantes es `role="status"`.
 */
export function CaseSummaryCard({
  variant, form, sections, showPerfil, onGoToSection, activeSection, flashKey,
  device, osLabel, isIOS, imeiDisplay, imeiManual, connection, shot, onRefreshShot, onZoomShot,
}: Props) {
  const aside = variant === "aside";
  const [caratulaOpen, setCaratulaOpen] = useState(false);
  const [specsOpen, setSpecsOpen] = useState(false);
  const specsId = useId();
  const caratulaId = useId();

  const nro = form.nro_referencia.trim();
  const tipo = form.tipo_causa.trim();
  const fecha = fechaEnPalabras(form.fecha_intervencion);
  const future = isFutureIsoDate(form.fecha_intervencion);
  const caratula = form.caratula.trim();
  const tramite = tramiteAnte(form.nombre_tribunal, form.sala_tribunal);
  const integracion = fraseIntegracion(form.integrantes);
  const organismo = form.organismo_tribunal.trim();
  const propone = datosProponente(form.nombre_proponente, form.profesion_proponente, form.matricula_proponente);

  const index = sections.filter(s => s.id !== "perfil" || showPerfil);
  const missing = index.reduce((n, s) => n + s.missing, 0);
  const model = `${device.manufacturer} ${device.model}`.trim();
  const imeiOk = !isImeiMissing(imeiDisplay);

  // Partes: solo las cargadas (las vacías son ruido; el índice ya dice qué falta).
  const partes = ([
    ["Denunciante", form.parte_denunciante.trim(), ["parte_denunciante"]],
    ["Denunciada", form.parte_denunciada.trim(), ["parte_denunciada"]],
    ["Propone", propone, ["nombre_proponente", "profesion_proponente", "matricula_proponente"]],
    ["Titular del dispositivo", form.nombre_denunciante.trim(), ["nombre_denunciante"]],
  ] as const).filter(([, value]) => !!value);

  return (
    <div
      className={cn(
        "text-fx-body-sm text-fx-text-2",
        // Ficha lateral: columna del alto del viewport (sin scroll interno); el
        // escenario del teléfono se lleva el alto que sobra (flex-1).
        aside ? "flex h-full flex-col gap-4 [@media(max-height:719px)]:gap-3 motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]" : "space-y-4",
      )}
    >
      {/* 1. Equipo: el teléfono es el protagonista de la ficha. */}
      <div className={cn(aside && "flex min-h-0 flex-1 flex-col")}>
        <div
          className={cn(
            "relative isolate flex justify-center overflow-hidden rounded-fx-lg bg-[radial-gradient(ellipse_closest-side_at_50%_48%,var(--fx-surface-3)_0%,transparent_100%)] pb-2 pt-4",
            aside && "min-h-[9rem] max-h-[25rem] flex-1",
          )}
        >
          {/* Sombra de apoyo, decorativa. */}
          <span aria-hidden="true" className="absolute bottom-7 left-1/2 -z-10 h-3 w-24 -translate-x-1/2 rounded-full bg-fx-text opacity-15 blur-md" />
          <DeviceStage
            src={shot.src}
            platform={device.platform ?? "android"}
            loading={shot.loading}
            error={shot.error}
            stale={shot.stale}
            variant={variant}
            className={aside ? "flex h-full min-h-0 flex-col items-center" : undefined}
            sizeClassName={aside ? "min-h-0 flex-1 w-auto" : "w-28"}
          />
        </div>

        <div className="mt-3 flex shrink-0 items-start gap-2">
          <img src={isIOS ? "/apple.svg" : "/android.svg"} alt="" width={14} height={14} className="mt-0.5 h-3.5 w-3.5 shrink-0 dark:invert" />
          <div className="min-w-0 flex-1">
            <p className="m-0 truncate font-medium text-fx-text" title={model}>{model}</p>
            <div className="flex items-center gap-2">
              <p className="m-0 min-w-0 flex-1 truncate text-xs text-fx-text-3">{osLabel}</p>
              {aside && <ConnectionIndicator connection={connection} />}
            </div>
            {!imeiOk && (
              <p className="m-0 mt-0.5 flex items-center gap-1 text-xs font-medium text-fx-warning">
                <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> IMEI no detectado
              </p>
            )}
          </div>
        </div>

        {/* Controles de la captura + "Ver captura" en la misma fila; "Pantalla capturada" (#52) como estado. */}
        <CaptureControls
          loading={shot.loading}
          onRefresh={onRefreshShot}
          error={shot.error}
          disabledReason={shot.disabledReason}
          className="mt-1.5 shrink-0 gap-2"
          trailing={
            // Botón de ícono (con nombre accesible y title) para que entre en la fila de "Actualizar captura".
            <Button
              type="button" text severity="secondary" size="small"
              icon={<Maximize2 className="h-3.5 w-3.5" aria-hidden="true" />}
              aria-label="Ver captura"
              title="Ver captura"
              aria-haspopup="dialog"
              onClick={onZoomShot}
              disabled={!shot.src}
              className="h-8 w-8 p-0 [@media(pointer:coarse)]:h-11 [@media(pointer:coarse)]:w-11"
            />
          }
        />
        <div role="status" className="flex h-4 shrink-0 items-center justify-center">
          {shot.showCaptured && (
            <span className="inline-flex items-center gap-1 text-xs text-fx-success motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
              <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
              Pantalla capturada
            </span>
          )}
        </div>

        {aside && (
          // < 720 px de alto los avisos quedan solo para el lector: el indicador "Desconectado", el motivo
          // de "Actualizar captura" y "IMEI no detectado" ya lo dicen a la vista (la ficha no desborda).
          <div className="mt-3 shrink-0 space-y-2 empty:hidden [@media(max-height:719px)]:sr-only">
            {/* 720–799 px de alto: solo el título de cada aviso (el detalle queda para el lector). */}
            <DisconnectedBanner connection={connection} className={SHORT_COMPACT} />
            {imeiManual && <ImeiMissingNotice className={SHORT_COMPACT} />}
          </div>
        )}
      </div>

      {/* 2. Carátula: expediente, tipo, fecha y carátula, como en el informe. */}
      <div className="shrink-0 space-y-2.5 border-t border-fx-border pt-4">
        <Row keys={["nro_referencia", "tipo_causa"]} flash={flashKey}>
          <Caption>Expediente</Caption>
          <div className="mt-0.5 flex items-baseline gap-2">
            <p className="m-0 min-w-0 flex-1 break-all text-fx-h3 tabular-nums text-fx-text" translate="no">
              {nro || <Empty>Sin número</Empty>}
            </p>
            {tipo && <Tag value={tipo} title={tipo} className="max-w-[45%] shrink-0 overflow-hidden" />}
          </div>
        </Row>
        {/* En pantallas muy bajas la fecha se pliega (ya está en palabras debajo del campo). */}
        <Row keys={["fecha_intervencion"]} flash={flashKey} className={cn(aside && "[@media(max-height:719px)]:hidden")}>
          <p className="m-0 text-fx-text-2">
            {fecha ? <span className="first-letter:uppercase inline-block">{fecha}</span> : <Empty>Sin fecha de intervención</Empty>}
            {future && (
              <span className="text-fx-warning">
                {" · "}
                <AlertTriangle className="inline h-3.5 w-3.5 align-[-2px]" aria-hidden="true" /> posterior a hoy
              </span>
            )}
          </p>
        </Row>
        <Row keys={["caratula"]} flash={flashKey}>
          <p id={caratulaId} className={cn("m-0 whitespace-pre-line break-words text-fx-text", !caratulaOpen && "line-clamp-3 [@media(max-height:719px)]:line-clamp-2")}>
            {caratula || <Empty>La carátula aparece acá a medida que la escribís.</Empty>}
          </p>
          {caratula.length > CARATULA_CLAMP && (
            <button
              type="button"
              onClick={() => setCaratulaOpen(o => !o)}
              aria-expanded={caratulaOpen}
              aria-controls={caratulaId}
              className="mt-0.5 cursor-pointer rounded-fx-sm border-0 bg-transparent p-0 text-xs font-medium text-fx-accent-text underline-offset-2 hover:underline fx-focus-ring"
            >
              {caratulaOpen ? "Ver menos" : "Ver todo"}
            </button>
          )}
        </Row>
      </div>

      {/* 3. Progreso: única región viva de la ficha (cambia solo cuando cambia N) + índice. */}
      <div className="shrink-0 border-t border-fx-border pt-4">
        <p role="status" aria-atomic="true" className={cn("m-0 flex items-center gap-1.5 text-xs font-medium", missing ? "text-fx-text" : "text-fx-success")}>
          {missing > 0 ? (
            <>
              <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-fx-warning" aria-hidden="true" />
              {missing === 1 ? "Falta 1 obligatorio" : `Faltan ${missing} obligatorios`}
            </>
          ) : (
            <>
              <CheckCircle2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              Todos los obligatorios completos
            </>
          )}
        </p>
        <nav aria-label="Secciones del formulario" className="-mx-2 mt-1.5">
          <ul className="m-0 grid list-none grid-cols-2 gap-x-1 p-0">
            {index.map(s => (
              <li key={s.id} className="min-w-0">
                <button
                  type="button"
                  onClick={() => onGoToSection(s)}
                  // Abre esa sección del formulario; la abierta queda marcada (no solo por color).
                  aria-current={activeSection === s.id ? "step" : undefined}
                  className={cn(
                    "flex w-full min-h-8 cursor-pointer items-center gap-1.5 rounded-fx-md border-0 px-2 text-left text-fx-body-sm text-fx-text transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-2 fx-focus-ring [@media(pointer:coarse)]:min-h-11",
                    activeSection === s.id ? "bg-fx-surface-2 font-semibold" : "bg-transparent",
                  )}
                >
                  <span className="min-w-0 flex-1 truncate">{s.label}</span>
                  {s.missing > 0 ? (
                    <span className="inline-flex h-5 min-w-5 shrink-0 items-center justify-center rounded-fx-pill bg-fx-warning-soft px-1.5 text-xs font-medium tabular-nums text-fx-warning">
                      {s.missing}<span className="sr-only"> {s.missing === 1 ? "faltante" : "faltantes"}</span>
                    </span>
                  ) : (
                    <>
                      <CheckCircle2 className="h-3.5 w-3.5 shrink-0 text-fx-success" aria-hidden="true" />
                      <span className="sr-only">completa</span>
                    </>
                  )}
                </button>
              </li>
            ))}
          </ul>
        </nav>
      </div>

      {/* 4. Detalle (trámite, partes cargadas, especificaciones): en la ficha lateral, solo si hay alto. */}
      <div className={cn("shrink-0 space-y-3 border-t border-fx-border pt-4", aside && TALL_ONLY)}>
        <Row keys={["nombre_tribunal", "sala_tribunal", "organismo_tribunal", "integrantes"]} flash={flashKey}>
          <Caption>Trámite</Caption>
          <p className="m-0 mt-0.5 break-words text-fx-text">
            {tramite ? <>Trámite ante {tramite}{integracion}</> : <Empty>Sin destinatario</Empty>}
          </p>
          {organismo && <p className="m-0 mt-0.5 break-words text-xs text-fx-text-2">{organismo}</p>}
        </Row>

        {partes.length > 0 && (
          <dl className="m-0 grid grid-cols-1 gap-3">
            {partes.map(([label, value, keys]) => (
              <Row key={label} keys={[...keys]} flash={flashKey}>
                <Caption as="dt">{label}</Caption>
                <dd className="m-0 mt-0.5 break-words text-fx-text">{value}</dd>
              </Row>
            ))}
          </dl>
        )}

        <div>
          <button
            type="button"
            onClick={() => setSpecsOpen(o => !o)}
            aria-expanded={specsOpen}
            aria-controls={specsId}
            className="-mx-2 flex w-[calc(100%+1rem)] min-h-8 cursor-pointer items-center gap-2 rounded-fx-md border-0 bg-transparent px-2 text-left text-xs text-fx-text-2 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-2 hover:text-fx-text fx-focus-ring [@media(pointer:coarse)]:min-h-11"
          >
            <span className="flex-1">Especificaciones del equipo</span>
            <ChevronDown
              className={cn("h-4 w-4 transition-transform duration-fx-fast ease-fx motion-reduce:transition-none", specsOpen && "rotate-180")}
              aria-hidden="true"
            />
          </button>
          <dl id={specsId} hidden={!specsOpen} className="m-0">
            <SpecRow icon={Hash} label="IMEI" value={imeiOk ? imeiDisplay : "—"} mono accent={!imeiManual} />
            <SpecRow icon={FileText} label="Serial" value={device.serial.slice(0, 24)} mono />
            {device.name && device.name !== device.model && (
              <SpecRow icon={User} label="Nombre" value={device.name} />
            )}
          </dl>
        </div>
      </div>
    </div>
  );
}
