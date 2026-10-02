"use client";

import { useState, useEffect, useRef, useCallback } from "react";
import { Button } from "primereact/button";
import { Calendar } from "primereact/calendar";
import type { CalendarPassThroughOptions } from "primereact/calendar";
import { InputText } from "primereact/inputtext";
import { Tag } from "primereact/tag";
import {
  ChevronRight, ArrowLeft,
  Hash, User, FileText, Landmark, Building2, Scale, Calendar as CalendarIcon,
  MapPin, UserCheck, Briefcase, BadgeCheck, Smartphone, Phone,
  ArrowRight, CheckCircle2, ScanLine, Usb,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { agent, type Device } from "@/lib/agent";
import {
  CASE_FORM_FOCUS_ORDER, CASE_REQUIRED_KEYS, PROFILE_REQUIRED_KEYS,
  MAX_LEN_LINE, MAX_LEN_LONG, caseFieldId, errorKeyToFieldId,
} from "@/lib/pericial";
import { CATALOG_LABELS } from "@/lib/catalogs";
import { useCatalogs } from "@/hooks/useCatalogs";
import type { CaseFormData, CatalogId, ExpertProfile, IntegranteRow, ProfileFormData } from "@/types";
import { PhoneFrame } from "./PhoneFrame";
import { SpecRow } from "./SpecRow";
import { FormField, describedBy } from "./FormField";
import { ExpertProfileCard } from "./ExpertProfileCard";
import { FxBanner } from "@/components/feedback/FxBanner";
import { StepHeader } from "@/components/wizard/StepHeader";
import { StepActions } from "@/components/wizard/StepActions";
import {
  CatalogAutoComplete, CatalogManageButton, type CatalogManageTarget, type CatalogStatus,
} from "@/components/form/CatalogAutoComplete";
import { CatalogManageDialog } from "@/components/form/CatalogManageDialog";
import { IntegrantesField } from "@/components/form/IntegrantesField";

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
 */
function calendarPt(aria: { describedBy?: string; invalid: boolean }): CalendarPassThroughOptions {
  return {
    root: { className: "w-full sm:w-full" },
    input: {
      root: {
        className: "pr-10",
        "aria-required": true,
        "aria-invalid": aria.invalid || undefined,
        "aria-describedby": aria.describedBy,
      },
    },
  } as unknown as CalendarPassThroughOptions; // `input` anidado (InputText hijo): los tipos lo declaran plano
}

/* ── Types ─────────────────────────────────────────────────── */
type SectionId = "perfil" | "actuacion" | "partes" | "equipo";

/** Claves de texto del formulario que se renderizan con `renderField`. */
type TextFieldKey = Exclude<keyof CaseFormData, "imeiOverride" | "integrantes">;

interface FieldDef {
  key: TextFieldKey;
  label: string;
  /** Texto chico junto a la etiqueta (por defecto "· opcional" en los no obligatorios). */
  sublabel?: string;
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
}

/** Actuación hasta los integrantes (que van con `IntegrantesField`, entre las dos mitades). */
const ACTUACION_A: FieldDef[] = [
  // Etiqueta "Destinatario (tribunal, fiscalía, persona…)" (D3): el paréntesis va como
  // sublabel (minúscula, más compacto) para que en media columna no ocupe tres líneas.
  { key: "nombre_tribunal",      label: "Destinatario", sublabel: "(tribunal, fiscalía, persona…)", icon: Landmark, required: true, placeholder: "Tribunal, fiscalía o persona…", catalog: "destinatarios" },
  { key: "organismo_tribunal",   label: "Organismo",                   icon: Building2, placeholder: "Organismo del que depende…" },
  { key: "sala_tribunal",        label: "Sala",                        icon: Landmark,  hint: "Ej.: Sala II" },
  { key: "tipo_causa",           label: "Tipo de causa",               icon: Scale,     required: true, hint: "Ej.: disciplinaria, civil, penal" },
];

const ACTUACION_B: FieldDef[] = [
  { key: "nro_referencia",       label: "Número de causa / expediente", icon: Hash,     required: true, placeholder: "Ej.: 1234/2026" },
  { key: "fecha_intervencion",   label: "Fecha de intervención",       icon: CalendarIcon, required: true, type: "date" },
  { key: "caratula",             label: "Carátula",                    icon: FileText,  required: true, placeholder: "Carátula completa de la causa…", maxLength: MAX_LEN_LONG, wide: true },
  { key: "objeto_causa",         label: "Objeto de la causa",          icon: FileText },
  { key: "ambito_causa",         label: "Ámbito",                      icon: MapPin },
];

const ACTUACION: FieldDef[] = [...ACTUACION_A, ...ACTUACION_B];

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
  ...Object.fromEntries(ACTUACION.map(f => [f.key, "actuacion"])),
  integrantes: "actuacion",
  ...Object.fromEntries(PARTES.map(f => [f.key, "partes"])),
  ...Object.fromEntries(EQUIPO.map(f => [f.key, "equipo"])),
  imei: "equipo",
};

function sectionOfErrorKey(key: string): SectionId {
  return key.startsWith("perfil.") ? "perfil" : SECTION_OF[key] ?? "actuacion";
}

function sectionOfFieldId(id: string): SectionId {
  if (id.startsWith("profile-")) return "perfil";
  if (id === "case-imei") return "equipo";
  if (id.startsWith("case-integrante")) return "actuacion";
  return SECTION_OF[id.replace(/^case-/, "")] ?? "actuacion";
}

interface Props {
  device: Device;
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
  onBack: () => void;
  onCancel: () => void;
  onSubmit: () => void;
}

/* ── Sección plegable ── */
function FormSection({
  id, title, open, onToggle, children,
}: { id: SectionId; title: string; open: boolean; onToggle: () => void; children: React.ReactNode }) {
  return (
    <section aria-labelledby={`case-section-${id}-title`} className="border-t border-fx-border pt-3">
      <h3 id={`case-section-${id}-title`} className="m-0">
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={open}
          aria-controls={`case-section-${id}`}
          className="flex w-full min-h-11 items-center gap-2 rounded-fx-sm border-0 bg-transparent p-0 text-left text-fx-label uppercase text-fx-text-2 hover:text-fx-text transition-colors duration-fx-fast ease-fx fx-focus-ring cursor-pointer"
        >
          <ChevronRight
            className={cn("h-4 w-4 transition-transform duration-fx-fast motion-reduce:transition-none", open && "rotate-90")}
            aria-hidden="true"
          />
          {title}
        </button>
      </h3>
      <div id={`case-section-${id}`} hidden={!open} className="pt-3">
        {children}
      </div>
    </section>
  );
}

/* ════════════════════════════════════════════════════════════════
   MAIN COMPONENT
════════════════════════════════════════════════════════════════ */
export function CaseFormStep({
  device, form, errors, loading, mode, isLegacy,
  profile, profileLoading, profileForm, focusErrorsTick, focusFieldId, onFocusConsumed,
  onChange, onProfileChange, onClearError, onBack, onCancel, onSubmit,
}: Props) {
  const [screenshotSrc, setScreenshotSrc] = useState<string | null>(null);
  const [loadingShot,   setLoadingShot]   = useState(false);
  const hasTakenFirst = useRef(false);

  const isIOS = device.platform === "ios";
  const osLabel = isIOS
    ? `iOS ${device.ios_version ?? device.android_version}`
    : `Android ${device.android_version}`;

  const imeiDisplay = form.imeiOverride.trim() || device.imei;
  const imeiManual  = device.imei === "INGRESAR_MANUALMENTE";

  const agentBase = process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765";

  // ── Secciones plegables ──
  const profileIncomplete = !profileLoading && !profile?.is_complete;
  const [open, setOpen] = useState<Record<SectionId, boolean>>({
    perfil: false, actuacion: true, partes: true, equipo: true,
  });
  // La tarjeta del perfil arranca desplegada si el perfil está incompleto (una vez cargado).
  const profileInit = useRef(false);
  useEffect(() => {
    if (profileLoading || profileInit.current) return;
    profileInit.current = true;
    if (!profile?.is_complete) setOpen(o => ({ ...o, perfil: true }));
  }, [profileLoading, profile]);

  function focusField(id: string, section: SectionId) {
    setOpen(o => (o[section] ? o : { ...o, [section]: true }));
    // Después del commit (la sección puede estar recién desplegada).
    window.setTimeout(() => {
      const el = document.getElementById(id);
      if (el) { el.focus(); el.scrollIntoView({ block: "center", behavior: "smooth" }); }
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

  async function takeShot() {
    setLoadingShot(true);
    try {
      const { filename } = await agent.takeScreenshot(device.serial, device.platform ?? "android");
      setScreenshotSrc(`${agentBase}/files/${filename}`);
    } catch { /* silently ignore — device may be locked */ }
    finally { setLoadingShot(false); }
  }

  useEffect(() => {
    if (hasTakenFirst.current) return;
    hasTakenFirst.current = true;
    takeShot();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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

  function renderField(f: FieldDef) {
    const id = caseFieldId(f.key);
    const error = errors[f.key];
    const value = form[f.key];
    const ariaDescribedBy = describedBy(id, { hint: f.hint, error });
    return (
      <div key={f.key} className={f.wide ? "sm:col-span-2" : undefined}>
        <FormField
          id={id}
          icon={f.icon}
          label={f.label}
          sublabel={f.sublabel ?? (f.required ? undefined : "· opcional")}
          hint={f.hint}
          error={error}
          required={f.required}
          labelAside={f.catalog && catalogStatus === "ready" ? (
            <CatalogManageButton
              catalogLabel={CATALOG_LABELS[f.catalog]}
              onClick={() => openManage(f.catalog!)}
            />
          ) : undefined}
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
          ) : f.type === "date" ? (
            <Calendar
              inputId={id}
              name={f.key}
              value={isoToLocalDate(value)}
              onChange={e => set(f.key, e.value instanceof Date ? localDateToIso(e.value) : "")}
              dateFormat="dd/mm/yy"
              showIcon
              icon={<CalendarIcon className="h-4 w-4" aria-hidden="true" />}
              placeholder="dd/mm/aaaa"
              invalid={!!error}
              pt={calendarPt({ describedBy: ariaDescribedBy, invalid: !!error })}
            />
          ) : (
            <div className="relative">
              <InputText
                id={id}
                name={f.key}
                inputMode={f.inputMode}
                autoComplete="off"
                aria-required={f.required || undefined}
                invalid={!!error}
                aria-invalid={!!error || undefined}
                aria-describedby={ariaDescribedBy}
                placeholder={f.placeholder}
                maxLength={f.maxLength ?? MAX_LEN_LINE}
                value={value}
                onChange={e => set(f.key, e.target.value)}
                // Por pt y no por className: el className de props se fusiona antes
                // que el pt global y tailwind-merge se quedaría con su `px-3`.
                pt={{ root: { className: cn(f.required && "pr-9") } }}
              />
              {f.required && value.trim().length > 0 && !error && (
                <CheckCircle2
                  className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fx-success"
                  aria-hidden="true"
                />
              )}
            </div>
          )}
        </FormField>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_1.75fr]">

        {/* ── Izquierda: identidad del equipo (única fuente de verdad de sus datos). ── */}
        <div className={cn("flex flex-col overflow-hidden rounded-fx-lg border border-fx-border bg-fx-surface-1", FADE_IN)}>
          <div className="flex items-center gap-2.5 border-b border-fx-border px-4 py-3">
            {/* contenido de imagen */}
            <img
              src={isIOS ? "/apple.svg" : "/android.svg"}
              alt=""
              width={14}
              height={14}
              className="h-3.5 w-3.5 shrink-0 dark:invert"
            />
            <Tag value={osLabel} />
            <span className="ml-auto inline-flex shrink-0 items-center gap-1.5 text-xs font-medium text-fx-success">
              <Usb className="h-3.5 w-3.5" aria-hidden="true" /> USB conectado
            </span>
          </div>

          <div className="flex flex-1 flex-col items-center gap-4 p-5">
            <div role="status" className="flex h-4 w-full items-center justify-end">
              {screenshotSrc && !loadingShot && (
                <span className={cn("inline-flex items-center gap-1 text-xs text-fx-success", FADE_IN)}>
                  <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                  Pantalla capturada
                </span>
              )}
            </div>

            <PhoneFrame
              src={screenshotSrc}
              platform={device.platform ?? "android"}
              loading={loadingShot}
              onRefresh={takeShot}
            />

            <p className="m-0 w-full text-center text-fx-body-sm font-semibold text-fx-text">
              {device.manufacturer} {device.model}
            </p>
          </div>

          <div className="border-t border-fx-border bg-fx-surface-2 px-5 pb-4">
            <h3 className="m-0 py-3 text-fx-label uppercase text-fx-text-2">Especificaciones</h3>
            <dl className="m-0">
              <SpecRow icon={Hash}     label="IMEI"   value={imeiDisplay !== "INGRESAR_MANUALMENTE" ? imeiDisplay : "—"} mono accent={!imeiManual} />
              <SpecRow icon={FileText} label="Serial" value={device.serial.slice(0, 24)} mono />
              {device.name && device.name !== device.model && (
                <SpecRow icon={User} label="Nombre" value={device.name} />
              )}
            </dl>
          </div>

          {imeiManual && (
            <FxBanner tone="warn" role="status" className="mx-4 mb-4 w-auto">
              <p className="m-0 font-semibold">IMEI no detectado automáticamente</p>
              <p className="m-0 mt-0.5 text-xs font-normal text-fx-text-2">
                Completá el campo IMEI. Marcá <kbd className="font-mono font-bold text-fx-text">*#06#</kbd> en el celular para verlo.
              </p>
            </FxBanner>
          )}
        </div>

        {/* ── Derecha: formulario ── */}
        <div className={cn("space-y-4 rounded-fx-lg border border-fx-border bg-fx-surface-1 p-5", FADE_IN)}>
          {isLegacy && (
            <FxBanner tone="warn" role="status">
              Este caso se creó antes del informe pericial. Completá los datos de la causa para continuar.
            </FxBanner>
          )}

          <StepHeader
            title="Datos de la causa"
            aside={
              <div className="flex items-center gap-2">
                <div aria-hidden="true" className="h-1.5 w-20 overflow-hidden rounded-full bg-fx-surface-3">
                  <div
                    className="h-full w-full origin-left rounded-full bg-fx-accent transition-transform duration-fx-slow ease-fx motion-reduce:transition-none"
                    style={{ transform: `scaleX(${requiredTotal ? requiredFilled / requiredTotal : 0})` }}
                  />
                </div>
                <span className="text-xs tabular-nums text-fx-text-2">
                  {requiredFilled}/{requiredTotal}
                  <span className="sr-only"> obligatorios completos</span>
                </span>
              </div>
            }
          />

          {cat.error && (
            <p role="status" className="m-0 text-xs text-fx-text-3">No se pudieron cargar tus sugerencias</p>
          )}

          <ExpertProfileCard
            profile={profile}
            loading={profileLoading}
            form={profileForm}
            errors={errors}
            expanded={open.perfil}
            onToggle={() => setOpen(o => ({ ...o, perfil: !o.perfil }))}
            onChange={onProfileChange}
            onClearError={key => { if (errors[key]) onClearError(key); }}
          />

          <FormSection id="actuacion" title="Actuación" open={open.actuacion}
            onToggle={() => setOpen(o => ({ ...o, actuacion: !o.actuacion }))}>
            <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
              {ACTUACION_A.map(renderField)}
              <IntegrantesField rows={form.integrantes} onChange={setIntegrantes} />
              {ACTUACION_B.map(renderField)}
            </div>
          </FormSection>

          <FormSection id="partes" title="Partes" open={open.partes}
            onToggle={() => setOpen(o => ({ ...o, partes: !o.partes }))}>
            <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">{PARTES.map(renderField)}</div>
          </FormSection>

          <FormSection id="equipo" title="Equipo" open={open.equipo}
            onToggle={() => setOpen(o => ({ ...o, equipo: !o.equipo }))}>
            <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
              {EQUIPO.map(renderField)}
              {/* IMEI: dato de solo lectura cuando se detecta; campo real solo si hay que cargarlo a mano. */}
              <div className="sm:col-span-2">
                {!imeiManual ? (
                  <div className="flex items-center gap-3 rounded-fx-md border border-fx-border bg-fx-surface-2 px-3.5 py-2.5">
                    <Hash className="h-3.5 w-3.5 shrink-0 text-fx-text-2" aria-hidden="true" />
                    <div className="min-w-0 flex-1">
                      <p className="m-0 text-fx-label uppercase text-fx-text-2">IMEI</p>
                      <p className="m-0 truncate font-mono text-fx-body-sm text-fx-text" translate="no">{imeiDisplay}</p>
                    </div>
                    <Tag severity="success" icon={<ScanLine className="h-3 w-3" aria-hidden="true" />} value="Detectado" />
                  </div>
                ) : (
                  <FormField id="case-imei" icon={Hash} label="IMEI" sublabel="· ingresalo manualmente" error={errors.imei} required>
                    <InputText
                      id="case-imei"
                      name="imei"
                      inputMode="numeric"
                      autoComplete="off"
                      aria-required
                      invalid={!!errors.imei}
                      aria-invalid={!!errors.imei || undefined}
                      aria-describedby={describedBy("case-imei", { error: errors.imei })}
                      pt={{ root: { className: "font-mono" } }}
                      placeholder="15 dígitos (marcá *#06#)…"
                      value={form.imeiOverride}
                      maxLength={17}
                      onChange={e => set("imeiOverride", e.target.value, "imei")}
                    />
                  </FormField>
                )}
              </div>
            </div>
          </FormSection>

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
            <Button
              type="button"
              label={loading ? "Guardando…" : mode === "edit" ? "Guardar y continuar" : "Crear caso y continuar"}
              icon={<ArrowRight className="h-4 w-4" aria-hidden="true" />}
              iconPos="right"
              loading={loading}
              onClick={onSubmit}
              className="w-full sm:flex-1 min-h-11"
            />
          </StepActions>
        </div>
      </div>

      <CatalogManageDialog
        visible={manage !== null}
        catalogLabel={CATALOG_LABELS[manageCatalog]}
        entries={cat.catalogs[manageCatalog]}
        initialTarget={manage?.target}
        onHide={() => setManage(null)}
        onUpdate={(id, value) => cat.update(manageCatalog, id, value)}
        onRemove={id => cat.remove(manageCatalog, id)}
      />
    </div>
  );
}
