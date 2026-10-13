"use client";

import { useState, useEffect, useRef, useCallback, useMemo } from "react";
import { Button } from "primereact/button";
import { Calendar } from "primereact/calendar";
import type { CalendarPassThroughOptions } from "primereact/calendar";
import { Dialog } from "primereact/dialog";
import { InputText } from "primereact/inputtext";
import { InputTextarea } from "primereact/inputtextarea";
import { Tag } from "primereact/tag";
import {
  ChevronUp, Check, Pencil, ArrowLeft,
  Hash, User, FileText, Landmark, Building2, Scale, Calendar as CalendarIcon,
  MapPin, UserCheck, Briefcase, BadgeCheck, Smartphone, Phone,
  ArrowRight, CheckCircle2, ScanLine, RefreshCw, History, AlertTriangle,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { agent, agentFileURL, previewFileURL, PreviewUnsupportedError, type Device } from "@/lib/agent";
import {
  CASE_FORM_FOCUS_ORDER, CASE_REQUIRED_KEYS, PROFILE_REQUIRED_KEYS, PREFILL_KEYS,
  MAX_LEN_LINE, MAX_LEN_LONG, caseFieldId, errorKeyToFieldId, isImeiMissing, profileErrorKey,
  joinIntegrantes, cleanIntegrantes, prefillFromCase, prefillSources, prefilledKeys, todayIso,
  validateCaseForm, validateProfile,
  type CasePrefill, type PrefillKey,
} from "@/lib/pericial";
import {
  fechaEnPalabras, findSameExpediente, isFutureIsoDate, normalizeExpediente, sectionSummaryLine, summarySections, yesterdayIso,
  type SummarySection, type SummarySectionId,
} from "@/lib/case-summary";
import { CATALOG_LABELS } from "@/lib/catalogs";
import { useCatalogs } from "@/hooks/useCatalogs";
import { useMediaQuery } from "@/hooks/useMediaQuery";
import { scrollIntoContainer } from "@/lib/scroll";
import type { DeviceConnection } from "@/hooks/useDeviceConnection";
import type { Case, CaseFormData, CatalogId, ExpertProfile, IntegranteRow, ProfileFormData } from "@/types";
import { FormField, FormFieldStyleContext, describedBy } from "./FormField";
import { Lightbox } from "./Lightbox";
import { ExpertProfileCard } from "./ExpertProfileCard";
import { FxBanner } from "@/components/feedback/FxBanner";
import { StepHeader } from "@/components/wizard/StepHeader";
import { StepActions } from "@/components/wizard/StepActions";
import {
  CatalogAutoComplete, CatalogManageButton, type CatalogManageTarget, type CatalogStatus,
} from "@/components/form/CatalogAutoComplete";
import { CatalogManageDialog } from "@/components/form/CatalogManageDialog";
import { IntegrantesField } from "@/components/form/IntegrantesField";
import { CONNECTION_UI, ConnectionIndicator, DisconnectedBanner } from "@/components/case-form/ConnectionIndicator";
import { ImeiMissingNotice } from "@/components/case-form/ImeiMissingNotice";
import { CaseSummaryCard, type FlashKey, type ShotState } from "@/components/case-form/CaseSummaryCard";
import { CaseSummaryBar } from "@/components/case-form/CaseSummaryBar";
import { PrefillNotice, PREFILL_BLANK_ID, PREFILL_CHANGE_ID, PREFILL_UNDO_ID } from "@/components/case-form/PrefillNotice";
import { PrefillSourceDialog } from "@/components/case-form/PrefillSourceDialog";
import { DuplicateExpedienteNotice } from "@/components/case-form/DuplicateExpedienteNotice";

const FADE_IN = "motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]";

/** `YYYY-MM-DD` → `Date` local (sin pasar por UTC). Cualquier otro formato → null. */
function isoToLocalDate(s: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  if (!m) return null;
  return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
}

/** `Date` local → `YYYY-MM-DD` (mismo criterio que `todayIso()`). */
function localDateToIso(d: Date): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/**
 * Calendar no reenvía `aria-describedby`/`aria-invalid` al `<input>`: van por
 * `pt.input.root` (el InputText interno). Es un objeto, no una función, así se
 * suma al pt global de `inputtext` del hijo; `pr-10` deja lugar al botón.
 * `root` ocupa todo el ancho de la columna (el global es `sm:w-auto`).
 * `onKeyDown` va al `<input>`: lo usa el guard de Enter con el panel abierto.
 */
function calendarPt(aria: { describedBy?: string; invalid: boolean }, onKeyDown: (e: React.KeyboardEvent<HTMLInputElement>) => void): CalendarPassThroughOptions {
  return {
    root: { className: "w-full sm:w-full" },
    input: {
      root: {
        className: "pr-10",
        "aria-required": true,
        "aria-invalid": aria.invalid || undefined,
        "aria-describedby": aria.describedBy,
        // Prime abre el panel con Alt+↓ (y ↓) desde el input (wizard-pulido-ux M1).
        "aria-keyshortcuts": "Alt+ArrowDown",
        onKeyDown,
      },
    },
  } as unknown as CalendarPassThroughOptions; // `input` anidado (InputText hijo): los tipos lo declaran plano
}

/* ── Types ─────────────────────────────────────────────────── */
type SectionId = SummarySectionId;

/** Claves de texto del formulario que se renderizan con `renderField`. */
type TextFieldKey = Exclude<keyof CaseFormData, "imeiOverride" | "integrantes">;

interface FieldDef {
  key: TextFieldKey;
  label: string;
  icon: React.ElementType;
  required?: boolean;
  placeholder?: string;
  hint?: string;
  maxLength?: number;
  type?: "text" | "date";
  inputMode?: "text" | "numeric" | "tel";
  wide?: boolean;
  /** Campo con sugerencias del catálogo del perito (formulario-caso-catalogos). */
  catalog?: CatalogId;
  /** Fuente mono y sin traducción automática (expediente). */
  mono?: boolean;
  /** Textarea autoajustable que igual viaja en una línea (carátula, §6.9). */
  multiline?: boolean;
}

/** Lo que cambia en cada causa va primero (D6 A). */
// Grilla de dos columnas con filas parejas (reintento 1): expediente + fecha,
// carátula a todo el ancho, tipo + ámbito, objeto a todo el ancho.
const CAUSA: FieldDef[] = [
  { key: "nro_referencia",       label: "Número de causa / expediente", icon: Hash,     required: true, placeholder: "Ej.: 1234/2026", mono: true },
  { key: "fecha_intervencion",   label: "Fecha de intervención",       icon: CalendarIcon, required: true, type: "date" },
  { key: "caratula",             label: "Carátula",                    icon: FileText,  required: true, placeholder: "Carátula completa de la causa…", maxLength: MAX_LEN_LONG, wide: true, multiline: true },
  { key: "tipo_causa",           label: "Tipo de causa",               icon: Scale,     required: true, placeholder: "Ej.: disciplinaria, civil, penal…" },
  { key: "ambito_causa",         label: "Ámbito",                      icon: MapPin },
  { key: "objeto_causa",         label: "Objeto de la causa",          icon: FileText, wide: true },
];

/** Destinatario (los integrantes van a continuación, con `IntegrantesField`). */
const DESTINATARIO: FieldDef[] = [
  // "Destinatario (tribunal, fiscalía, persona…)" (D3): la aclaración va como ayuda
  // debajo del campo, así la etiqueta queda en una línea (reintento 1).
  { key: "nombre_tribunal",      label: "Destinatario", hint: "Tribunal, fiscalía o persona a quien se dirige el informe.", icon: Landmark, required: true, placeholder: "Tribunal, fiscalía o persona…", catalog: "destinatarios", wide: true },
  { key: "organismo_tribunal",   label: "Organismo",                   icon: Building2, placeholder: "Organismo del que depende…" },
  { key: "sala_tribunal",        label: "Sala",                        icon: Landmark,  hint: "Ej.: Sala II" },
];

const PARTES: FieldDef[] = [
  { key: "parte_denunciante",    label: "Parte denunciante",           icon: User,      required: true, catalog: "partes" },
  { key: "parte_denunciada",     label: "Parte denunciada",            icon: User,      required: true, hint: "Incluí el artículo si corresponde: «el Sr. …», «las Dras. …»", catalog: "partes" },
  { key: "nombre_proponente",    label: "Nombre de quien propone",     icon: UserCheck, required: true, wide: true, catalog: "partes" },
  { key: "profesion_proponente", label: "Profesión de quien propone",  icon: Briefcase, catalog: "profesiones" },
  { key: "matricula_proponente", label: "Matrícula de quien propone",  icon: BadgeCheck },
];

const EQUIPO: FieldDef[] = [
  { key: "nombre_denunciante",   label: "Titular del dispositivo",     icon: User,       required: true, placeholder: "Apellido y nombre completo…" },
  { key: "dni_denunciante",      label: "DNI del titular",             icon: Hash,       inputMode: "numeric", maxLength: 20 },
  { key: "tipo_dispositivo",     label: "Tipo de dispositivo",         icon: Smartphone, required: true, catalog: "tipos_dispositivo" },
  { key: "linea_dispositivo",    label: "Línea",                       icon: Phone,      inputMode: "tel", placeholder: "Número de línea…" },
];

const SECTION_OF: Record<string, SectionId> = {
  ...Object.fromEntries(CAUSA.map(f => [f.key, "causa"])),
  ...Object.fromEntries(DESTINATARIO.map(f => [f.key, "destinatario"])),
  integrantes: "destinatario",
  ...Object.fromEntries(PARTES.map(f => [f.key, "partes"])),
  ...Object.fromEntries(EQUIPO.map(f => [f.key, "equipo"])),
  imei: "equipo",
};

/** Primer campo de cada sección (destino del índice cuando no falta nada). */
const FIRST_FIELD: Record<SectionId, string> = {
  perfil: "profile-nombre",
  causa: caseFieldId("nro_referencia"),
  destinatario: caseFieldId("nombre_tribunal"),
  partes: caseFieldId("parte_denunciante"),
  equipo: caseFieldId("nombre_denunciante"),
};

function sectionOfErrorKey(key: string): SectionId {
  return key.startsWith("perfil.") ? "perfil" : SECTION_OF[key] ?? "causa";
}

function sectionOfFieldId(id: string): SectionId {
  if (id.startsWith("profile-")) return "perfil";
  if (id === "case-imei") return "equipo";
  if (id.startsWith("case-integrante")) return "destinatario";
  return SECTION_OF[id.replace(/^case-/, "")] ?? "causa";
}

const isPrefillKey = (k: string): k is PrefillKey => (PREFILL_KEYS as readonly string[]).includes(k);
const EMPTY_KEYS: ReadonlySet<PrefillKey> = new Set();

type PrefillValues = Pick<CaseFormData, PrefillKey>;

function pickPrefill(form: CaseFormData): PrefillValues {
  return Object.fromEntries(PREFILL_KEYS.map(k => [k, form[k]])) as PrefillValues;
}

/** Mismo valor visible (integrantes: misma frase). */
function sameValue(k: PrefillKey, a: PrefillValues, b: PrefillValues): boolean {
  if (k === "integrantes") {
    return joinIntegrantes(cleanIntegrantes(a.integrantes.map(r => r.value))) === joinIntegrantes(cleanIntegrantes(b.integrantes.map(r => r.value)));
  }
  return a[k] === b[k];
}

function isEmptyValue(k: PrefillKey, v: PrefillValues): boolean {
  return k === "integrantes" ? cleanIntegrantes(v.integrantes.map(r => r.value)).length === 0 : v[k].trim() === "";
}

/** Snapshot efímero para "Deshacer" (§6.10). */
interface PrefillUndo {
  /** Valores antes de la acción. */
  before: PrefillValues;
  /** Valores que dejó la acción (se restaura solo lo que sigue así o quedó vacío). */
  applied: PrefillValues;
  prefill: CasePrefill | null;
  /** "Empezar en blanco": cuántos campos se vaciaron y de qué causa venían. */
  blank: { count: number; from: Case | null } | null;
}

interface Props {
  device: Device;
  /** Estado real del equipo según Tatana (`useDeviceConnection` en la página). */
  connection: DeviceConnection;
  form: CaseFormData;
  errors: Record<string, string>;
  loading: boolean;
  /** "edit" = el caso ya existe (PUT); "create" = caso nuevo (POST). */
  mode: "create" | "edit";
  /** Borrador previo al informe pericial (`schema_version === 0`): sin "Cancelar". */
  isLegacy: boolean;
  profile: ExpertProfile | null;
  profileLoading: boolean;
  profileForm: ProfileFormData;
  /** Cambia en cada submit con errores: dispara el foco al primero. */
  focusErrorsTick: number;
  /** Campo a enfocar al montar (enlaces del checklist de "Generar"). */
  focusFieldId?: string | null;
  onFocusConsumed?: () => void;
  onChange: (f: CaseFormData) => void;
  onProfileChange: (f: ProfileFormData) => void;
  onClearError: (key: string) => void;
  /** "Siguiente" de una sección: suma los errores de esa sección (solo esa) a los del paso. */
  onSectionErrors: (errors: Record<string, string>) => void;
  onBack: () => void;
  onCancel: () => void;
  onSubmit: () => void | Promise<void>;
  /** Historial del perito ya cargado (precarga, buscador de origen y expediente repetido). */
  historyCases: Case[];
  /** id del caso que se edita (se excluye del aviso de expediente repetido); null en uno nuevo. */
  caseId: string | null;
  /** Precarga visible de un caso nuevo; null en edición / legacy / sin historial. */
  prefill: CasePrefill | null;
  onPrefillChange: (p: CasePrefill | null) => void;
}

/* ── Acordeón guiado: una sección abierta a la vez (reintento 2, Feedback 2) ── */

/** Orden de las secciones del paso 2 (el perfil solo cuenta si está incompleto o con errores). */
const SECTION_ORDER: SectionId[] = ["perfil", "causa", "destinatario", "partes", "equipo"];
const SECTION_LABEL: Record<SectionId, string> = {
  perfil: "Tus datos de perito", causa: "Causa", destinatario: "Destinatario", partes: "Partes", equipo: "Equipo",
};

/** Marca de paso: número (pendiente / activa) o check (completa y plegada). */
function StepDot({ n, active, done }: { n: number; active: boolean; done: boolean }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        "flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold tabular-nums",
        "transition-colors duration-fx-fast ease-fx motion-reduce:transition-none",
        active ? "bg-fx-accent text-fx-on-accent"
          : done ? "bg-fx-success-soft text-fx-success"
          : "border border-fx-border-strong text-fx-text-2",
      )}
    >
      {done && !active ? <Check className="h-3.5 w-3.5" strokeWidth={2.5} /> : n}
    </span>
  );
}

// Separadas por un divisor y aire (no por tarjetas anidadas). Plegada = resumen de una
// línea con "Editar"; abierta = campos + "Siguiente" (o la acción final) al pie.
function FormSection({
  id, step, title, description, open, onToggle, missing, summary, prefilled, footer, children,
}: {
  id: SectionId; step: number; title: string; description?: string; open: boolean; onToggle: () => void;
  missing: number; summary: string; prefilled: boolean; footer: React.ReactNode; children: React.ReactNode;
}) {
  const done = missing === 0;
  return (
    <section
      aria-labelledby={`case-section-${id}-title`}
      className={cn("border-t border-fx-border", open ? "pt-5" : "py-2.5")}
    >
      <h3 id={`case-section-${id}-title`} className="m-0 scroll-mt-24">
        <button
          type="button"
          id={`case-section-${id}-toggle`}
          onClick={onToggle}
          aria-expanded={open}
          aria-controls={`case-section-${id}`}
          className="group -mx-2 flex w-[calc(100%+1rem)] min-h-11 cursor-pointer items-center gap-3 rounded-fx-md border-0 bg-transparent px-2 text-left transition-colors duration-fx-fast ease-fx fx-focus-ring motion-reduce:transition-none [@media(hover:hover)]:hover:bg-fx-surface-2"
        >
          <StepDot n={step} active={open} done={done} />
          {open ? (
            <span className="min-w-0 flex-1">
              <span className="block text-fx-body font-semibold text-fx-text">{title}</span>
              {description && <span className="block text-xs font-normal text-fx-text-3">{description}</span>}
            </span>
          ) : (
            // Resumen de una línea: el lector lo lee como parte del nombre del botón.
            <span className="flex min-w-0 flex-1 items-baseline gap-2">
              <span className="shrink-0 text-fx-body font-semibold text-fx-text">{title}</span>
              <span className={cn("min-w-0 truncate text-fx-body-sm", summary ? "text-fx-text-2" : "text-fx-text-3")}>
                <span className="sr-only">: </span>{summary || "Sin completar"}
              </span>
            </span>
          )}
          {!open && prefilled && (
            // D3: lo precargado se sigue revisando aunque la sección esté plegada.
            <span className="inline-flex shrink-0 items-center gap-1 text-xs font-medium text-fx-accent-text" title="Datos tomados de la causa anterior: revisalos">
              <History className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="max-sm:sr-only">Precargado · revisar</span>
            </span>
          )}
          {missing > 0 ? (
            <span className="shrink-0 text-xs font-normal tabular-nums text-fx-text-2">
              {missing === 1 ? "Falta 1" : `Faltan ${missing}`}
            </span>
          ) : (
            <span className={cn("shrink-0 items-center gap-1 text-xs font-normal text-fx-success", open ? "inline-flex" : "sr-only")}>
              <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
              Completa
            </span>
          )}
          {open ? (
            <ChevronUp
              className="h-4 w-4 shrink-0 text-fx-text-3 transition-colors duration-fx-fast ease-fx group-hover:text-fx-text motion-reduce:transition-none"
              aria-hidden="true"
            />
          ) : (
            <span aria-hidden="true" className="inline-flex shrink-0 items-center gap-1 text-xs font-medium text-fx-text-2 transition-colors duration-fx-fast ease-fx group-hover:text-fx-text motion-reduce:transition-none">
              <Pencil className="h-3.5 w-3.5" />
              Editar
            </span>
          )}
        </button>
      </h3>
      <div
        id={`case-section-${id}`}
        role="region"
        aria-labelledby={`case-section-${id}-title`}
        hidden={!open}
        // Sin animar altura: el contenido aparece desde su encabezado (opacidad + 4 px).
        className="pt-4 pb-6 motion-safe:animate-[fx-section-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
      >
        {children}
        {footer}
      </div>
    </section>
  );
}

/** Pie de la sección abierta: "Siguiente" (valida la sección) o la acción final del paso. */
function SectionFooter({ nextLabel, submitLabel, loading }: { nextLabel: string | null; submitLabel: string; loading: boolean }) {
  return (
    <div className="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:items-center sm:justify-end sm:gap-4">
      {nextLabel && (
        <p id="case-section-next-hint" className="m-0 text-center text-xs text-fx-text-3 sm:text-right">
          A continuación: <span className="font-medium text-fx-text-2">{nextLabel}</span>
        </p>
      )}
      <Button
        type="submit"
        label={nextLabel ? "Siguiente" : loading ? "Guardando…" : submitLabel}
        aria-describedby={nextLabel ? "case-section-next-hint" : undefined}
        icon={<ArrowRight className="h-4 w-4" aria-hidden="true" />}
        iconPos="right"
        loading={!nextLabel && loading}
        // El pt sin estilos de Prime no ordena el ícono por `iconPos`: la flecha va después del texto.
        className="w-full flex-row-reverse sm:w-auto min-h-11"
      />
    </div>
  );
}

const IMEI_HINT = "No se detectó: ingresalo a mano (marcá *#06# en el equipo para verlo).";

/** Grilla de campos: dos columnas parejas, ritmo de 8 px (gap 16 × 24). */
// Entre lg y 1199 px el formulario queda angosto al lado de la ficha: una columna.
const FIELD_GRID = "grid grid-cols-1 items-start gap-x-4 gap-y-6 sm:grid-cols-2 lg:max-[1199px]:grid-cols-1";

/** Marca de campo precargado: ícono + texto accesible + title (no solo color). */
const PREFILL_BADGE = (
  <span className="inline-flex text-fx-accent-text" title="Tomado de la causa anterior">
    <History className="h-3.5 w-3.5" aria-hidden="true" />
    <span className="sr-only">(tomado de la causa anterior)</span>
  </span>
);

/* ════════════════════════════════════════════════════════════════
   MAIN COMPONENT
════════════════════════════════════════════════════════════════ */
export function CaseFormStep({
  device, connection, form, errors, loading, mode, isLegacy,
  profile, profileLoading, profileForm, focusErrorsTick, focusFieldId, onFocusConsumed,
  onChange, onProfileChange, onClearError, onSectionErrors, onBack, onCancel, onSubmit,
  historyCases, caseId, prefill, onPrefillChange,
}: Props) {
  const [screenshotSrc, setScreenshotSrc] = useState<string | null>(null);
  const [loadingShot,   setLoadingShot]   = useState(false);
  const [shotError,     setShotError]     = useState<string | null>(null);
  const [shotFailedAfterImage, setShotFailedAfterImage] = useState(false);
  const connectionRef = useRef(connection);
  connectionRef.current = connection;
  const screenshotSrcRef = useRef(screenshotSrc);
  screenshotSrcRef.current = screenshotSrc;
  const shotInFlight = useRef(false);
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => { mountedRef.current = false; };
  }, []);

  // Ficha al costado (lg+) o barra + panel (< lg): se monta una sola de las dos.
  const isLg = useMediaQuery("(min-width: 1024px)");

  const isIOS = device.platform === "ios";
  const osLabel = isIOS
    ? `iOS ${device.ios_version ?? device.android_version}`
    : `Android ${device.android_version}`;

  const imeiDisplay = form.imeiOverride.trim() || device.imei;
  // Carga manual también cuando el equipo no expuso el IMEI (viene vacío), no solo
  // con el centinela INGRESAR_MANUALMENTE: si no, el form manda IMEI vacío y el backend
  // rechaza con 400 sin que el perito tenga dónde cargarlo.
  const imeiManual  = isImeiMissing(device.imei);

  // ── Acordeón guiado: una sola sección abierta (null = todas plegadas) ──
  const profileIncomplete = !profileLoading && !profile?.is_complete;
  // Al entrar se abre la primera sección con faltantes (o la del enlace del checklist);
  // si no falta nada (edición), todas quedan plegadas como resumen.
  const [active, setActiveState] = useState<SectionId | null>(() => {
    if (focusFieldId) return sectionOfFieldId(focusFieldId);
    const initial = summarySections({ form, profileForm, profileIncomplete, imeiManual });
    return initial.find(s => s.id !== "perfil" && s.missing > 0)?.id ?? null;
  });
  /** El perito ya eligió una sección: la carga del perfil no la cambia. */
  const userNavigated = useRef(false);
  const setActive = useCallback((id: SectionId | null) => {
    userNavigated.current = true;
    setActiveState(id);
  }, []);
  // Perfil incompleto (se sabe recién al cargar): es la primera sección con faltantes.
  const profileInit = useRef(false);
  useEffect(() => {
    if (profileLoading || profileInit.current) return;
    profileInit.current = true;
    if (!profile?.is_complete && !userNavigated.current) setActiveState("perfil");
  }, [profileLoading, profile]);

  const reducedMotion = useMediaQuery("(prefers-reduced-motion: reduce)");
  const scrollBehavior: ScrollBehavior = reducedMotion ? "auto" : "smooth";

  function focusField(id: string, section: SectionId) {
    setActive(section);
    // Después del commit (la sección puede estar recién desplegada).
    window.setTimeout(() => {
      const el = document.getElementById(id);
      if (el) { el.focus({ preventScroll: true }); scrollIntoContainer(el, { block: "center", behavior: scrollBehavior }); }
    }, 60);
  }

  /** Abre una sección desde "Siguiente": la lleva arriba y enfoca su primer faltante. */
  function openSection(section: SectionId, fieldId: string) {
    setActive(section);
    window.setTimeout(() => {
      const field = document.getElementById(fieldId);
      const title = document.getElementById(`case-section-${section}-title`);
      if (title) scrollIntoContainer(title, { block: "start", behavior: scrollBehavior });
      else if (field) scrollIntoContainer(field, { block: "center", behavior: scrollBehavior });
      field?.focus({ preventScroll: true });
    }, 60);
  }

  // ── Indicador n/m: obligatorios de todo el paso ──
  const requiredValues = [
    ...CASE_REQUIRED_KEYS.map(k => form[k]),
    ...(profileIncomplete ? PROFILE_REQUIRED_KEYS.map(k => profileForm[k]) : []),
    ...(imeiManual ? [form.imeiOverride] : []),
  ];
  const requiredTotal  = requiredValues.length;
  const requiredFilled = requiredValues.filter(v => v.trim().length > 0).length;

  // ── Índice de la ficha (derivado en render; nada de efectos por tecla) ──
  const sections = useMemo(
    () => summarySections({ form, profileForm, profileIncomplete, imeiManual }),
    [form, profileForm, profileIncomplete, imeiManual],
  );
  const missingOf = (id: SectionId) => sections.find(s => s.id === id)?.missing ?? 0;
  const showPerfil = profileIncomplete || PROFILE_REQUIRED_KEYS.some(k => !!errors[profileErrorKey(k)]);
  const totalMissing = sections.reduce((n, s) => n + (s.id !== "perfil" || showPerfil ? s.missing : 0), 0);

  /** Próxima sección con faltantes después de `from` (sin volver atrás); null = toca crear el caso. */
  function nextWithMissing(from: SectionId): SectionId | null {
    const order = SECTION_ORDER.filter(id => id !== "perfil" || showPerfil);
    const start = order.indexOf(from);
    return order.slice(start + 1).find(id => missingOf(id) > 0) ?? null;
  }
  const nextSection = active ? nextWithMissing(active) : null;

  /** Errores de una sola sección, con las mismas reglas que el envío. */
  function sectionErrors(section: SectionId): Record<string, string> {
    if (section === "perfil") return validateProfile(profileForm);
    const all = validateCaseForm(form, { imeiManual });
    return Object.fromEntries(Object.entries(all).filter(([k]) => sectionOfErrorKey(k) === section));
  }

  /** "Siguiente" (o Enter dentro de la sección): valida solo la sección abierta y avanza. */
  function goNext(from: SectionId, to: SectionId) {
    const errs = sectionErrors(from);
    const keys = Object.keys(errs);
    if (keys.length > 0) {
      onSectionErrors(errs);
      const first = CASE_FORM_FOCUS_ORDER.find(k => errs[k]) ?? keys[0];
      focusField(errorKeyToFieldId(first), from);
      return;
    }
    const target = sections.find(s => s.id === to);
    openSection(to, target?.firstMissingFieldId ?? FIRST_FIELD[to]);
  }

  /** Clic en un encabezado: abre esa sección (y pliega la anterior) o pliega la abierta. */
  function toggleSection(section: SectionId) {
    setActive(active === section ? null : section);
  }

  // ── Captura de pantalla ──
  // Solo con el equipo conectado y sin otra captura en vuelo. El motivo del
  // último fallo queda visible mientras dice "Capturando…" (no se limpia al
  // empezar); con un fallo habiendo imagen previa, la imagen se conserva
  // atenuada como "Captura anterior".
  async function takeShot() {
    if (connectionRef.current !== "connected" || shotInFlight.current) return;
    shotInFlight.current = true;
    setLoadingShot(true);
    try {
      // Vista previa (wizard-concurrencia-pestanas DT5): no es evidencia y no emite
      // eventos. Con un Tatana sin `/preview`, captura común sin atribución (la
      // descartan las otras pestañas por no traer serial ni dueño).
      const platform = device.platform ?? "android";
      let src: string;
      try {
        const { url } = await agent.takePreview(device.serial, platform);
        src = previewFileURL(url);
      } catch (e) {
        if (!(e instanceof PreviewUnsupportedError)) throw e;
        const { filename } = await agent.takeScreenshot(device.serial, platform, null);
        src = agentFileURL(filename);
      }
      if (!mountedRef.current) return;
      setScreenshotSrc(src);
      setShotError(null);
      setShotFailedAfterImage(false);
    } catch (e) {
      if (!mountedRef.current) return;
      setShotError(e instanceof Error && e.message ? e.message : "No se pudo capturar la pantalla.");
      setShotFailedAfterImage(screenshotSrcRef.current !== null);
    } finally {
      shotInFlight.current = false;
      if (mountedRef.current) setLoadingShot(false);
    }
  }
  const takeShotRef = useRef(takeShot);
  takeShotRef.current = takeShot;

  // Captura en los flancos de la conexión: al montar conectado, y al volver a
  // conectarse (con 1 s de margen para que el equipo termine de enumerarse).
  // Montar desconectado no intenta capturar. Nunca hay loop: solo flancos.
  const prevConnection = useRef<DeviceConnection | null>(null);
  useEffect(() => {
    const prev = prevConnection.current;
    prevConnection.current = connection;
    if (connection !== "connected") return;
    if (prev === null) { void takeShot(); return; }
    if (prev !== "connected") {
      const t = window.setTimeout(() => { void takeShot(); }, 1000);
      return () => window.clearTimeout(t);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connection]);

  const connUi = CONNECTION_UI[connection];
  const stale = !!screenshotSrc && (shotFailedAfterImage || connection !== "connected");
  const showCaptured = !!screenshotSrc && !loadingShot && !stale && !shotError;
  const shot: ShotState = useMemo(() => ({
    src: screenshotSrc, loading: loadingShot, error: shotError, stale,
    disabledReason: connUi.disabledReason, showCaptured,
  }), [screenshotSrc, loadingShot, shotError, stale, connUi.disabledReason, showCaptured]);

  const onRefreshShot = useCallback(() => { void takeShotRef.current(); }, []);
  // Visor ampliado de la captura (botón "Ver captura" de la ficha; el teléfono 3D solo se gira).
  const [zoomSrc, setZoomSrc] = useState<string | null>(null);
  const onZoomShot = useCallback(() => setZoomSrc(screenshotSrcRef.current), []);

  // Al fallar la validación (submit con errores) enfocamos el primer campo con error,
  // en el orden visual; si está en una sección plegada, la despliega.
  useEffect(() => {
    if (focusErrorsTick === 0) return;
    const first = CASE_FORM_FOCUS_ORDER.find(k => errors[k]) ?? Object.keys(errors).find(k => errors[k]);
    if (first) focusField(errorKeyToFieldId(first), sectionOfErrorKey(first));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusErrorsTick]);

  // Enlace del checklist de "Generar": enfoca el campo pedido al montar.
  useEffect(() => {
    if (!focusFieldId) return;
    focusField(focusFieldId, sectionOfFieldId(focusFieldId));
    onFocusConsumed?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusFieldId]);

  function set(key: TextFieldKey | "imeiOverride", value: string, errorKey: string = key) {
    onChange({ ...form, [key]: value });
    if (errors[errorKey]) onClearError(errorKey);
  }

  function setIntegrantes(rows: IntegranteRow[]) {
    onChange({ ...form, integrantes: rows });
  }

  // ── Índice de la ficha → primer faltante de la sección ──
  const [drawerOpen, setDrawerOpen] = useState(false);
  const focusFieldRef = useRef(focusField);
  focusFieldRef.current = focusField;
  const drawerOpenRef = useRef(drawerOpen);
  drawerOpenRef.current = drawerOpen;
  const onGoToSection = useCallback((s: SummarySection) => {
    const id = s.firstMissingFieldId ?? FIRST_FIELD[s.id];
    if (drawerOpenRef.current) {
      // Primero se cierra el panel (y termina su salida) y después se enfoca.
      setDrawerOpen(false);
      window.setTimeout(() => focusFieldRef.current(id, s.id), 220);
    } else {
      focusFieldRef.current(id, s.id);
    }
  }, []);

  // ── Precarga visible (§6.10) ──
  const create = mode === "create";
  const source = useMemo(
    () => (create && prefill?.sourceId ? historyCases.find(c => c.id === prefill.sourceId) ?? null : null),
    [create, prefill, historyCases],
  );
  const sourceValues = useMemo(() => (source ? prefillFromCase(source) : null), [source]);
  const prefilled = useMemo(
    () => (sourceValues && prefill && !prefill.confirmed ? prefilledKeys(form, sourceValues) : EMPTY_KEYS),
    [form, sourceValues, prefill],
  );
  const [undo, setUndo] = useState<PrefillUndo | null>(null);
  const sources = useMemo(() => prefillSources(historyCases).filter(c => c.id !== caseId), [historyCases, caseId]);
  const showPrefillNotice = create && !!prefill && !prefill.confirmed && (!!source || !!undo);
  const [sourceDialogOpen, setSourceDialogOpen] = useState(false);

  /** Aplica valores de las claves reutilizables con un único objeto nuevo y limpia sus errores. */
  function applyPrefillValues(values: Partial<PrefillValues>) {
    onChange({ ...form, ...values });
    for (const k of Object.keys(values)) if (errors[k]) onClearError(k);
  }

  function focusSoon(id: string) {
    window.setTimeout(() => document.getElementById(id)?.focus(), 60);
  }

  function startBlank() {
    const before = pickPrefill(form);
    const cleared: Partial<PrefillValues> = {};
    for (const k of prefilled) {
      if (k === "integrantes") cleared.integrantes = [];
      else cleared[k] = "";
    }
    // Lo escrito a mano (claves fuera de `prefilled`) no se toca.
    setUndo({ before, applied: { ...before, ...cleared }, prefill, blank: { count: prefilled.size, from: source } });
    applyPrefillValues(cleared);
    onPrefillChange({ sourceId: null, confirmed: false });
    focusSoon(PREFILL_UNDO_ID);
  }

  function undoPrefill() {
    if (!undo) return;
    const now = pickPrefill(form);
    const restore: Partial<PrefillValues> = {};
    // Solo lo que el perito no tocó después: vacío o igual a lo que dejó la acción.
    for (const k of PREFILL_KEYS) {
      if (isEmptyValue(k, now) || sameValue(k, now, undo.applied)) {
        if (k === "integrantes") restore.integrantes = undo.before.integrantes;
        else restore[k] = undo.before[k];
      }
    }
    applyPrefillValues(restore);
    onPrefillChange(undo.prefill);
    setUndo(null);
    if (undo.prefill?.sourceId) focusSoon(PREFILL_BLANK_ID);
    else focusField(caseFieldId("nombre_tribunal"), "destinatario");
  }

  function keepPrefill() {
    if (prefill) onPrefillChange({ ...prefill, confirmed: true });
    setUndo(null);
    // Sigue en la sección abierta (o en Causa si están todas plegadas).
    const target = active ?? "causa";
    focusField(sections.find(x => x.id === target)?.firstMissingFieldId ?? FIRST_FIELD[target], target);
  }

  /** Elegir una causa de origen: reemplaza las 8 claves reutilizables (y queda "Deshacer"). */
  function pickSource(cas: Case) {
    const before = pickPrefill(form);
    const applied = prefillFromCase(cas);
    setUndo({ before, applied, prefill, blank: null });
    applyPrefillValues(applied);
    onPrefillChange({ sourceId: cas.id, confirmed: false });
  }

  // ── Expediente repetido (§6.12): se mira al salir del campo, nunca por tecla ──
  const [dupCheckedFor, setDupCheckedFor] = useState<string | null>(null);
  const [dupDismissedKey, setDupDismissedKey] = useState<string | null>(null);
  const dups = useMemo(() => {
    if (dupCheckedFor === null) return [];
    const key = normalizeExpediente(form.nro_referencia);
    if (normalizeExpediente(dupCheckedFor) !== key || key === dupDismissedKey) return [];
    return findSameExpediente(historyCases, form.nro_referencia, caseId);
  }, [dupCheckedFor, dupDismissedKey, form.nro_referencia, historyCases, caseId]);

  // ── Destello en la ficha del dato que cambió (al salir del campo) ──
  const [flashKey, setFlashKey] = useState<FlashKey | null>(null);
  const formRef = useRef(form);
  formRef.current = form;
  const focusValue = useRef<{ key: string; value: string } | null>(null);
  function fieldKeyOf(id: string): string | null {
    if (id.startsWith("case-integrante-")) return "integrantes";
    if (!id.startsWith("case-")) return null;
    const k = id.slice(5);
    return k in formRef.current && k !== "imeiOverride" && k !== "integrantes" ? k : null;
  }
  function valueOf(key: string): string {
    const f = formRef.current;
    return key === "integrantes"
      ? joinIntegrantes(cleanIntegrantes(f.integrantes.map(r => r.value)))
      : (f[key as TextFieldKey] ?? "");
  }
  function onFormFocus(e: React.FocusEvent<HTMLFormElement>) {
    const key = fieldKeyOf(e.target.id);
    focusValue.current = key ? { key, value: valueOf(key) } : null;
  }
  function onFormBlur(e: React.FocusEvent<HTMLFormElement>) {
    const key = fieldKeyOf(e.target.id);
    const prev = focusValue.current;
    if (!key || !prev || prev.key !== key) return;
    focusValue.current = null;
    if (valueOf(key) !== prev.value) setFlashKey(f => ({ key, n: (f?.n ?? 0) + 1 }));
  }

  // ── Catálogos de sugerencias (se cargan cada vez que se monta el paso) ──
  const cat = useCatalogs();
  const catalogStatus: CatalogStatus = cat.loading ? "loading" : cat.error ? "error" : "ready";
  // `manage` = diálogo abierto (con la fila pedida). El catálogo vive aparte y se
  // conserva al cerrar, para que el título no cambie durante la animación de salida.
  const [manage, setManage] = useState<{ target?: CatalogManageTarget } | null>(null);
  const [manageCatalog, setManageCatalog] = useState<CatalogId>("destinatarios");
  const openManage = useCallback((catalog: CatalogId, target?: CatalogManageTarget) => {
    setManageCatalog(catalog);
    setManage({ target });
  }, []);

  // Reintento de catálogos: si el foco estaba en "Reintentar" y la carga sale
  // bien, el botón se desmonta con el banner → el foco pasa al primer campo
  // con sugerencias ("Destinatario"). Si falla, el botón sigue y el foco no se mueve.
  const retryFocusRef = useRef(false);
  function retryCatalogs() {
    retryFocusRef.current = document.activeElement?.id === "case-catalogs-retry";
    void cat.reload();
  }
  const prevCatError = useRef(cat.error);
  useEffect(() => {
    const was = prevCatError.current;
    prevCatError.current = cat.error;
    if (was && !cat.error && retryFocusRef.current) {
      retryFocusRef.current = false;
      // Sin cambiar la sección abierta: al Destinatario si está abierto, si no al encabezado de la abierta.
      if (active === "destinatario") focusField("case-nombre_tribunal", "destinatario");
      else if (active) document.getElementById(`case-section-${active}-toggle`)?.focus();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cat.error]);

  // ── Envío: `<form>` real (Enter envía) sin envíos dobles ──
  const submitInFlight = useRef(false);
  function handleFormSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (loading || submitInFlight.current) return;
    // Con una sección abierta y otra con faltantes por delante, Enter / "Siguiente" avanza.
    if (active && nextSection) { goNext(active, nextSection); return; }
    submitInFlight.current = true;
    Promise.resolve(onSubmit()).finally(() => { submitInFlight.current = false; });
  }

  // Enter en "Fecha de intervención" con el selector abierto: Prime no lo
  // maneja en el input y enviaría el form → cierra el selector y no envía.
  const calendarRef = useRef<Calendar>(null);
  // M1 (DT9): con puntero fino el panel no se abre al enfocar (Tab llega al
  // input y se puede escribir). En táctil se mantiene: Prime usa
  // inputMode="none" y, sin abrir al tocar, el campo quedaría sin teclado ni panel.
  // Ojo: es `showOnFocus={coarsePointer}` (la SDD escribió `!coarsePointer`,
  // que invierte la intención de DT9).
  const coarsePointer = useMediaQuery("(pointer: coarse)");
  /** El panel lo abrió el teclado (↓ / Alt+↓): al mostrarse, el foco va al día. */
  const openedByKeyRef = useRef(false);
  function handleCalendarShown() {
    if (!openedByKeyRef.current) return;
    openedByKeyRef.current = false;
    const overlay = calendarRef.current?.getOverlay();
    if (!overlay) return;
    const cell =
      overlay.querySelector<HTMLElement>('span[data-p-highlight="true"]') ??
      overlay.querySelector<HTMLElement>('td[data-p-today="true"] span:not([data-p-disabled="true"])') ??
      overlay.querySelector<HTMLElement>('table td span:not([data-p-disabled="true"])');
    if (!cell) return;
    cell.tabIndex = 0;
    cell.focus();
  }
  function onCalendarKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown" && e.currentTarget.getAttribute("aria-expanded") !== "true") {
      openedByKeyRef.current = true;
    }
    if (e.key === "Enter" && !e.nativeEvent.isComposing && e.currentTarget.getAttribute("aria-expanded") === "true") {
      e.preventDefault();
      calendarRef.current?.hide();
    }
  }

  function renderField(f: FieldDef) {
    const id = caseFieldId(f.key);
    const error = errors[f.key];
    const value = form[f.key];
    const isDate = f.type === "date";
    // Fecha: la ayuda es la fecha en palabras; si es futura, advertencia no bloqueante (no es error).
    const hint = isDate ? fechaEnPalabras(value) ?? undefined : f.hint;
    const futureWarn = isDate && !error && isFutureIsoDate(value);
    const warnId = `${id}-warn`;
    const ariaDescribedBy = [describedBy(id, { hint, error }), futureWarn ? warnId : null].filter(Boolean).join(" ") || undefined;
    const done = f.required && value.trim().length > 0 && !error;
    const isNro = f.key === "nro_referencia";

    const labelAside = f.catalog && catalogStatus === "ready" ? (
      <CatalogManageButton
        catalogLabel={CATALOG_LABELS[f.catalog]}
        onClick={() => openManage(f.catalog!)}
      />
    ) : undefined;

    const field = (
      <div key={f.key} className={f.wide ? "col-span-full" : undefined}>
        <FormField
          id={id}
          icon={f.icon}
          label={f.label}
          optional={!f.required}
          // Fecha: la ayuda (fecha en palabras) va en la fila de los atajos, abajo.
          hint={isDate ? undefined : hint}
          error={error}
          required={f.required}
          labelAside={labelAside}
          labelBadge={isPrefillKey(f.key) && prefilled.has(f.key) ? PREFILL_BADGE : undefined}
        >
          {f.catalog ? (
            <CatalogAutoComplete
              id={id}
              name={f.key}
              value={value}
              onChange={v => set(f.key, v)}
              entries={cat.catalogs[f.catalog]}
              catalog={f.catalog}
              catalogLabel={CATALOG_LABELS[f.catalog]}
              status={catalogStatus}
              invalid={!!error}
              ariaDescribedBy={ariaDescribedBy}
              required={f.required}
              maxLength={f.maxLength ?? MAX_LEN_LINE}
              placeholder={f.placeholder}
              onManage={target => openManage(f.catalog!, target)}
            />
          ) : isDate ? (
            <>
            <Calendar
              ref={calendarRef}
              inputId={id}
              name={f.key}
              value={isoToLocalDate(value)}
              onChange={e => {
                if (e.value instanceof Date) { set(f.key, localDateToIso(e.value)); return; }
                // Escribiendo a mano: Prime manda null con cada parcial ("1", "13/1"…) y,
                // al cambiar `value`, reescribe el input aunque tenga el foco (borraba lo
                // tipeado sobre una fecha ya cargada). Mientras el texto no esté vacío se
                // conserva el valor; al salir, Prime repone la última fecha válida.
                const input = calendarRef.current?.getInput();
                if (input && document.activeElement === input && input.value.trim() !== "") return;
                set(f.key, "");
              }}
              dateFormat="dd/mm/yy"
              showIcon
              icon={<CalendarIcon className="h-4 w-4" aria-hidden="true" />}
              placeholder="dd/mm/aaaa"
              invalid={!!error}
              showOnFocus={coarsePointer}
              onShow={handleCalendarShown}
              pt={calendarPt({ describedBy: ariaDescribedBy, invalid: !!error }, onCalendarKeyDown)}
            />
            {/* Atajos Hoy / Ayer como chips del campo (no en la línea de la etiqueta) + fecha en palabras. */}
            <div className="mt-2 flex min-h-6 items-start gap-1.5">
              <p id={hint ? `${id}-hint` : undefined} className="m-0 min-w-0 flex-1 pt-1 text-xs leading-snug text-fx-text-3">
                {hint}
              </p>
              {([["Hoy", todayIso()], ["Ayer", yesterdayIso()]] as const).map(([label, iso]) => (
                <button
                  key={label}
                  type="button"
                  aria-label={`Fecha de intervención: ${label.toLowerCase()}`}
                  aria-pressed={value === iso}
                  onClick={() => set(f.key, iso)}
                  className={cn(
                    "inline-flex h-6 shrink-0 cursor-pointer items-center rounded-fx-pill border px-2.5 text-xs font-medium fx-focus-ring",
                    "transition-colors duration-fx-fast ease-fx [@media(pointer:coarse)]:min-h-11",
                    value === iso
                      ? "border-fx-accent bg-fx-accent-soft text-fx-accent-text"
                      : "border-fx-border bg-transparent text-fx-text-2 hover:border-fx-border-strong hover:text-fx-text",
                  )}
                >
                  {label}
                </button>
              ))}
            </div>
            </>
          ) : f.multiline ? (
            <>
              <div className="relative">
                <InputTextarea
                  id={id}
                  name={f.key}
                  autoResize
                  rows={2}
                  autoComplete="off"
                  aria-required={f.required || undefined}
                  invalid={!!error}
                  aria-invalid={!!error || undefined}
                  aria-describedby={ariaDescribedBy}
                  placeholder={f.placeholder}
                  maxLength={f.maxLength ?? MAX_LEN_LONG}
                  value={value}
                  // Viaja en una línea, como antes: un salto pegado se vuelve un espacio.
                  onChange={e => set(f.key, e.target.value.replace(/\s*\r?\n\s*/g, " "))}
                  onKeyDown={e => {
                    // Enter envía (como en el resto del form); Shift+Enter tampoco agrega saltos.
                    if (e.key !== "Enter" || e.nativeEvent.isComposing) return;
                    e.preventDefault();
                    if (!e.shiftKey) e.currentTarget.form?.requestSubmit();
                  }}
                  // El pt de inputtextarea pone `props.className` al final (tailwind-merge): pisa su min-h de 5rem.
                  className={cn("min-h-0", f.required && "pr-9")}
                />
                {done && (
                  <CheckCircle2 className="pointer-events-none absolute right-3 top-3 h-4 w-4 text-fx-success" aria-hidden="true" />
                )}
              </div>
              <p aria-hidden="true" className="m-0 mt-1 text-right text-xs tabular-nums text-fx-text-3">
                {value.length}/{f.maxLength ?? MAX_LEN_LONG}
              </p>
            </>
          ) : (
            <div className="relative">
              <InputText
                id={id}
                name={f.key}
                inputMode={f.inputMode}
                autoComplete="off"
                spellCheck={f.mono ? false : undefined}
                translate={f.mono ? "no" : undefined}
                aria-required={f.required || undefined}
                invalid={!!error}
                aria-invalid={!!error || undefined}
                aria-describedby={ariaDescribedBy}
                placeholder={f.placeholder}
                maxLength={f.maxLength ?? MAX_LEN_LINE}
                value={value}
                onChange={e => set(f.key, e.target.value)}
                onBlur={isNro ? () => setDupCheckedFor(form.nro_referencia) : undefined}
                // Por pt y no por className: el className de props se fusiona antes
                // que el pt global y tailwind-merge se quedaría con su `px-3`.
                pt={{ root: { className: cn(f.required && "pr-9", f.mono && "font-mono") } }}
              />
              {done && (
                <CheckCircle2
                  className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fx-success"
                  aria-hidden="true"
                />
              )}
            </div>
          )}
        </FormField>
        {futureWarn && (
          <p id={warnId} className="m-0 mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-warning">
            <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            La fecha es posterior a hoy. ¿Es correcta?
          </p>
        )}
      </div>
    );
    return field;
  }

  // ── Acordeón: pie de la sección abierta y props comunes de cada sección ──
  const submitLabel = mode === "edit" ? "Guardar y continuar" : "Crear caso y continuar";
  const footer = (
    <SectionFooter nextLabel={nextSection ? SECTION_LABEL[nextSection] : null} submitLabel={submitLabel} loading={loading} />
  );
  function sectionProps(id: Exclude<SectionId, "perfil">) {
    return {
      open: active === id,
      onToggle: () => toggleSection(id),
      missing: missingOf(id),
      summary: sectionSummaryLine(id, form),
      prefilled: [...prefilled].some(k => SECTION_OF[k] === id),
      // Un solo pie (un solo botón de envío) en el DOM: el de la sección abierta.
      footer: active === id ? footer : null,
    };
  }

  const summaryProps = {
    form, sections, showPerfil, onGoToSection, flashKey, activeSection: active,
    device, osLabel, isIOS, imeiDisplay, imeiManual, connection, shot, onRefreshShot, onZoomShot,
  };

  const dupNotice = dups.length > 0 && (
    // Expediente repetido: fila propia a todo el ancho, debajo de expediente + fecha.
    <div className="col-span-full">
      <DuplicateExpedienteNotice
        cases={dups}
        onUse={create && (dups[0].schema_version ?? 0) >= 1 ? cas => pickSource(cas) : undefined}
        onDismiss={() => setDupDismissedKey(normalizeExpediente(form.nro_referencia))}
      />
    </div>
  );

  return (
    <FormFieldStyleContext.Provider value="field">
    <div className="space-y-6">
      {/* < lg: estado del equipo arriba del formulario, sin desplegar nada (#52). Fila plana, sin tarjeta. */}
      {!isLg && (
        <div className={cn("space-y-3 border-b border-fx-border pb-4", FADE_IN)}>
          <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
            <img
              src={isIOS ? "/apple.svg" : "/android.svg"}
              alt=""
              width={14}
              height={14}
              className="h-3.5 w-3.5 shrink-0 dark:invert"
            />
            <p className="m-0 min-w-0 flex-1 truncate text-fx-body-sm font-medium text-fx-text" title={`${device.manufacturer} ${device.model}`}>
              {device.manufacturer} {device.model}
              <span className="ml-2 font-normal text-fx-text-3">{osLabel}</span>
            </p>
            <ConnectionIndicator connection={connection} className="ml-auto" />
          </div>
          <DisconnectedBanner connection={connection} />
          {imeiManual && <ImeiMissingNotice />}
        </div>
      )}

      <div className="grid grid-cols-1 gap-8 lg:grid-cols-[minmax(0,1fr)_17rem] xl:grid-cols-[minmax(0,1fr)_18.5rem] xl:gap-10">
        <form
          noValidate
          onSubmit={handleFormSubmit}
          onFocus={onFormFocus}
          onBlur={onFormBlur}
          aria-label="Datos de la causa"
          className={cn(
            // Una sola superficie (la tarjeta del paso): el formulario no lleva caja propia.
            "min-w-0 space-y-6",
            // < lg: el foco por teclado nunca queda tapado por la barra inferior sticky.
            !isLg && "[&_button]:scroll-mb-24 [&_input]:scroll-mb-24 [&_textarea]:scroll-mb-24",
            FADE_IN,
          )}
        >
          {/* Título y progreso primero; los avisos, debajo (reintento 1, punto 6). */}
          <div>
          <StepHeader
            title="Datos de la causa"
            aside={
              <div className="flex items-center gap-2.5 pb-1">
                <span className="text-xs tabular-nums text-fx-text-2">
                  <span className="font-semibold text-fx-text">{requiredFilled}</span> de {requiredTotal}
                  <span className="sr-only"> obligatorios completos</span>
                </span>
                <div aria-hidden="true" className="h-1.5 w-24 overflow-hidden rounded-full bg-fx-surface-3">
                  <div
                    className="h-full w-full origin-left rounded-full bg-fx-accent transition-transform duration-fx-slow ease-fx motion-reduce:transition-none"
                    style={{ transform: `scaleX(${requiredTotal ? requiredFilled / requiredTotal : 0})` }}
                  />
                </div>
              </div>
            }
          />
          <p className="m-0 mt-1.5 text-fx-body-sm text-fx-text-2">Así van a figurar en la carátula y el encabezado del informe.</p>
          </div>

          {isLegacy && (
            <FxBanner tone="warn" role="status">
              Este caso se creó antes del informe pericial. Completá los datos de la causa para continuar.
            </FxBanner>
          )}

          {showPrefillNotice && (
            <PrefillNotice
              source={source}
              canUndo={!!undo}
              canChoose={sources.length > 0}
              blank={undo?.blank ?? null}
              onChangeSource={() => setSourceDialogOpen(true)}
              onStartBlank={startBlank}
              onKeep={keepPrefill}
              onUndo={undoPrefill}
              onDismiss={() => setUndo(null)}
            />
          )}

          {cat.error && (
            <FxBanner tone="warn">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
                <p className="m-0 min-w-0 flex-1">No se pudieron cargar tus sugerencias. Podés escribir igual.</p>
                <Button
                  type="button"
                  id="case-catalogs-retry"
                  size="small"
                  outlined
                  severity="secondary"
                  label={cat.loading ? "Reintentando…" : "Reintentar"}
                  icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                  loading={cat.loading}
                  onClick={retryCatalogs}
                  className="shrink-0 [@media(pointer:coarse)]:min-h-11"
                />
              </div>
            </FxBanner>
          )}

          <div>
            <ExpertProfileCard
              profile={profile}
              loading={profileLoading}
              form={profileForm}
              errors={errors}
              expanded={active === "perfil"}
              onToggle={() => toggleSection("perfil")}
              onChange={onProfileChange}
              onClearError={key => { if (errors[key]) onClearError(key); }}
            />
            {active === "perfil" && <div className="-mt-1 pb-6">{footer}</div>}

            <FormSection id="causa" step={1} title="Causa" description="Identificación de la causa y fecha de la intervención."
              {...sectionProps("causa")}>
              <div className={FIELD_GRID}>
                {CAUSA.slice(0, 2).map(renderField)}
                {dupNotice}
                {CAUSA.slice(2).map(renderField)}
              </div>
            </FormSection>

            <FormSection id="destinatario" step={2} title="Destinatario" description="A quién se dirige el informe."
              {...sectionProps("destinatario")}>
              <div className={FIELD_GRID}>
                {DESTINATARIO.map(renderField)}
                <IntegrantesField
                  rows={form.integrantes}
                  onChange={setIntegrantes}
                  prefilled={prefilled.has("integrantes") ? PREFILL_BADGE : undefined}
                />
              </div>
            </FormSection>

            <FormSection id="partes" step={3} title="Partes" description="Denunciante, denunciada y quien propone al perito."
              {...sectionProps("partes")}>
              <div className={FIELD_GRID}>{PARTES.map(renderField)}</div>
            </FormSection>

            <FormSection id="equipo" step={4} title="Equipo" description="Titular y datos del dispositivo peritado."
              {...sectionProps("equipo")}>
              <div className={FIELD_GRID}>
                {EQUIPO.map(renderField)}
                {/* IMEI: dato de solo lectura cuando se detecta; campo real solo si hay que cargarlo a mano. */}
                <div className="col-span-full">
                  {!imeiManual ? (
                    <div className="flex items-center gap-3 rounded-fx-md bg-fx-surface-2 px-3.5 py-2.5">
                      <div className="min-w-0 flex-1">
                        <p className="m-0 text-xs text-fx-text-3">IMEI</p>
                        <p className="m-0 truncate font-mono text-fx-body-sm text-fx-text" translate="no">{imeiDisplay}</p>
                      </div>
                      <Tag severity="success" icon={<ScanLine className="h-3 w-3" aria-hidden="true" />} value="Detectado" />
                    </div>
                  ) : (
                    <FormField id="case-imei" icon={Hash} label="IMEI" hint={IMEI_HINT} error={errors.imei} required>
                      <InputText
                        id="case-imei"
                        name="imei"
                        inputMode="numeric"
                        autoComplete="off"
                        aria-required
                        invalid={!!errors.imei}
                        aria-invalid={!!errors.imei || undefined}
                        aria-describedby={describedBy("case-imei", { hint: IMEI_HINT, error: errors.imei })}
                        pt={{ root: { className: "font-mono" } }}
                        placeholder="15 dígitos…"
                        value={form.imeiOverride}
                        maxLength={17}
                        onChange={e => set("imeiOverride", e.target.value, "imei")}
                      />
                    </FormField>
                  )}
                </div>
              </div>
            </FormSection>
          </div>

          {!isLg && (
            <CaseSummaryBar
              nro={form.nro_referencia.trim()}
              missing={totalMissing}
              open={drawerOpen}
              onOpen={() => setDrawerOpen(true)}
            />
          )}

          {/* La acción principal vive al pie de la sección abierta; acá queda solo con todo plegado. */}
          <StepActions>
            {mode === "create" && (
              <Button
                type="button"
                severity="secondary"
                icon={<ArrowLeft className="h-4 w-4" aria-hidden="true" />}
                label="Atrás"
                onClick={onBack}
                className="w-full sm:w-auto min-h-11"
              />
            )}
            {mode === "edit" && !isLegacy && (
              <Button
                type="button"
                severity="secondary"
                label="Cancelar"
                onClick={onCancel}
                disabled={loading}
                className="w-full sm:w-auto min-h-11"
              />
            )}
            {!active && (
              <Button
                type="submit"
                label={loading ? "Guardando…" : submitLabel}
                icon={<ArrowRight className="h-4 w-4" aria-hidden="true" />}
                iconPos="right"
                loading={loading}
                className="w-full flex-row-reverse sm:flex-1 min-h-11"
              />
            )}
          </StepActions>
        </form>

        {/* lg+: Ficha viva de la causa, sticky debajo de la navbar flotante y sin scroll
            interno (en el DOM después del form: el foco va form → ficha). Separada del
            formulario por un divisor, no por otra tarjeta.
            Alto (wizard-pulido-ux M3/M4): el sticky es relativo al contenedor con
            scroll del wizard, que ya empieza debajo de la navbar (72 px), y la ficha
            arranca a 96 px de él: 100dvh − 72 − 96 − 40 (aire inferior) = 100dvh − 13rem.
            Con 8.5rem el borde inferior (índice) quedaba 32 px fuera de la pantalla. */}
        {isLg && (
          <aside
            aria-label="Resumen de la causa"
            className="min-w-0 border-l border-fx-border pl-6 xl:pl-8 lg:sticky lg:top-24 lg:h-[calc(100dvh-13rem)] lg:self-start"
          >
            <CaseSummaryCard variant="aside" {...summaryProps} />
          </aside>
        )}
      </div>

      {/* Fuera del <form> (D7 #52): diálogos y visor. */}
      {!isLg && (
        <Dialog
          visible={drawerOpen}
          onHide={() => setDrawerOpen(false)}
          position="right"
          header="Resumen de la causa"
          dismissableMask
          draggable={false}
          resizable={false}
        >
          <CaseSummaryCard variant="drawer" {...summaryProps} />
        </Dialog>
      )}

      <CatalogManageDialog
        visible={manage !== null}
        catalogLabel={CATALOG_LABELS[manageCatalog]}
        entries={cat.catalogs[manageCatalog]}
        initialTarget={manage?.target}
        onHide={() => setManage(null)}
        onUpdate={(id, value) => cat.update(manageCatalog, id, value)}
        onRemove={id => cat.remove(manageCatalog, id)}
      />

      {create && (
        <PrefillSourceDialog
          visible={sourceDialogOpen}
          sources={sources}
          currentId={prefill?.sourceId ?? null}
          returnFocusId={PREFILL_CHANGE_ID}
          onPick={pickSource}
          onHide={() => setSourceDialogOpen(false)}
        />
      )}

      <Lightbox src={zoomSrc} onClose={() => setZoomSrc(null)} alt="Captura de pantalla del dispositivo, ampliada" />
    </div>
    </FormFieldStyleContext.Provider>
  );
}
