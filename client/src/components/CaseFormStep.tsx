"use client";

import { useState, useEffect, useRef } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  Loader2, AlertTriangle, ChevronRight,
  Hash, User, FileText, RotateCcw, Landmark, Building2, Scale, Users, Calendar,
  MapPin, UserCheck, Briefcase, BadgeCheck, Smartphone, Phone,
  ArrowRight, CheckCircle2, ScanLine,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { agent, type Device } from "@/lib/agent";
import {
  CASE_FORM_FOCUS_ORDER, CASE_REQUIRED_KEYS, PROFILE_REQUIRED_KEYS,
  MAX_LEN_LINE, MAX_LEN_LONG, caseFieldId, errorKeyToFieldId,
} from "@/lib/pericial";
import type { CaseFormData, ExpertProfile, ProfileFormData } from "@/types";
import { PhoneFrame } from "./PhoneFrame";
import { SpecRow } from "./SpecRow";
import { FormField, describedBy } from "./FormField";
import { ExpertProfileCard } from "./ExpertProfileCard";

const EASE = [0.22, 1, 0.36, 1] as const;

/* ── Types ─────────────────────────────────────────────────── */
type SectionId = "perfil" | "actuacion" | "partes" | "equipo";

interface FieldDef {
  key: Exclude<keyof CaseFormData, "imeiOverride">;
  label: string;
  icon: React.ElementType;
  required?: boolean;
  placeholder?: string;
  hint?: string;
  maxLength?: number;
  type?: "text" | "date";
  inputMode?: "text" | "numeric" | "tel";
  wide?: boolean;
}

const ACTUACION: FieldDef[] = [
  { key: "nombre_tribunal",      label: "Tribunal",                    icon: Landmark,  required: true, placeholder: "Nombre del tribunal…" },
  { key: "organismo_tribunal",   label: "Organismo",                   icon: Building2, placeholder: "Organismo del que depende…" },
  { key: "sala_tribunal",        label: "Sala",                        icon: Landmark,  hint: "Ej.: Sala II" },
  { key: "tipo_causa",           label: "Tipo de causa",               icon: Scale,     required: true, hint: "Ej.: disciplinaria, civil, penal" },
  { key: "integrantes_tribunal", label: "Integrantes",                 icon: Users,     hint: "Ej.: Dres. Nombre Apellido y Nombre Apellido", maxLength: MAX_LEN_LONG, wide: true },
  { key: "nro_referencia",       label: "Número de causa / expediente", icon: Hash,     required: true, placeholder: "Ej.: 1234/2026" },
  { key: "fecha_intervencion",   label: "Fecha de intervención",       icon: Calendar,  required: true, type: "date" },
  { key: "caratula",             label: "Carátula",                    icon: FileText,  required: true, placeholder: "Carátula completa de la causa…", maxLength: MAX_LEN_LONG, wide: true },
  { key: "objeto_causa",         label: "Objeto de la causa",          icon: FileText },
  { key: "ambito_causa",         label: "Ámbito",                      icon: MapPin },
];

const PARTES: FieldDef[] = [
  { key: "parte_denunciante",    label: "Parte denunciante",           icon: User,      required: true },
  { key: "parte_denunciada",     label: "Parte denunciada",            icon: User,      required: true, hint: "Incluí el artículo si corresponde: «el Sr. …», «las Dras. …»" },
  { key: "nombre_proponente",    label: "Nombre de quien propone",     icon: UserCheck, required: true, wide: true },
  { key: "profesion_proponente", label: "Profesión de quien propone",  icon: Briefcase },
  { key: "matricula_proponente", label: "Matrícula de quien propone",  icon: BadgeCheck },
];

const EQUIPO: FieldDef[] = [
  { key: "nombre_denunciante",   label: "Titular del dispositivo",     icon: User,       required: true, placeholder: "Apellido y nombre completo…" },
  { key: "dni_denunciante",      label: "DNI del titular",             icon: Hash,       inputMode: "numeric", maxLength: 20 },
  { key: "tipo_dispositivo",     label: "Tipo de dispositivo",         icon: Smartphone, required: true },
  { key: "linea_dispositivo",    label: "Línea",                       icon: Phone,      inputMode: "tel", placeholder: "Número de línea…" },
];

const SECTION_OF: Record<string, SectionId> = {
  ...Object.fromEntries(ACTUACION.map(f => [f.key, "actuacion"])),
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
    <section aria-labelledby={`case-section-${id}-title`} className="border-t pt-3" style={{ borderColor: "var(--border)" }}>
      <h3 id={`case-section-${id}-title`} className="m-0">
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={open}
          aria-controls={`case-section-${id}`}
          className="flex w-full items-center gap-1.5 rounded-sm section-label text-left fx-focus-ring"
        >
          <ChevronRight className={cn("h-3.5 w-3.5 transition-transform", open && "rotate-90")} aria-hidden="true" />
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
  const formComplete   = requiredFilled === requiredTotal;

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

  function set<K extends keyof CaseFormData>(key: K, value: string, errorKey: string = key) {
    onChange({ ...form, [key]: value });
    if (errors[errorKey]) onClearError(errorKey);
  }

  function renderField(f: FieldDef) {
    const id = caseFieldId(f.key);
    const error = errors[f.key];
    const value = form[f.key];
    return (
      <div key={f.key} className={f.wide ? "sm:col-span-2" : undefined}>
        <FormField
          id={id}
          icon={f.icon}
          label={f.label}
          sublabel={f.required ? undefined : "· opcional"}
          hint={f.hint}
          error={error}
          required={f.required}
        >
          <div className="relative">
            <input
              id={id}
              name={f.key}
              type={f.type ?? "text"}
              inputMode={f.inputMode}
              autoComplete="off"
              aria-required={f.required || undefined}
              aria-invalid={!!error || undefined}
              aria-describedby={describedBy(id, { hint: f.hint, error })}
              className={cn("input", f.required && f.type !== "date" && "pr-9", error && "input-error")}
              placeholder={f.placeholder}
              maxLength={f.type === "date" ? undefined : f.maxLength ?? MAX_LEN_LINE}
              value={value}
              onChange={e => set(f.key, e.target.value)}
            />
            {f.required && f.type !== "date" && value.trim().length > 0 && !error && (
              <CheckCircle2
                className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2"
                style={{ color: "var(--green)" }}
                aria-hidden="true"
              />
            )}
          </div>
        </FormField>
      </div>
    );
  }

  return (
    <div className="space-y-4">

      {/* ── Two-column layout ── */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_1.75fr]">

        {/* ── Left: Device identity — única fuente de verdad para los datos del
            dispositivo (antes se repetían nombre/modelo/OS en un banner superior,
            debajo del mockup y en la ficha de specs). ── */}
        <motion.div
          className="rounded-lg overflow-hidden flex flex-col"
          style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}
          initial={{ opacity: 0, y: -10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.3, ease: EASE }}
        >
          {/* Header: plataforma + estado de conexión */}
          <div className="flex items-center gap-2.5 px-4 py-3" style={{ borderBottom: "1px solid var(--border)" }}>
            <img
              src={isIOS ? "/apple.svg" : "/android.svg"}
              alt=""
              className="w-3.5 h-3.5 opacity-70 dark:invert dark:opacity-70 flex-shrink-0"
            />
            <span
              className="text-[9px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full flex-shrink-0"
              style={{ background: "var(--bg-elevated)", color: "var(--text-secondary)", border: "1px solid var(--border)" }}
            >
              {osLabel}
            </span>
            <div className="flex-1" />
            <span className="flex items-center gap-1.5 text-[10px] font-medium flex-shrink-0" style={{ color: "var(--green)" }}>
              <span className="w-1.5 h-1.5 rounded-full" style={{ background: "var(--green)" }} />
              USB conectado
            </span>
          </div>

          <div className="flex-1 p-5 flex flex-col items-center gap-5">
            {/* Screenshot status */}
            <div className="w-full flex items-center justify-end h-3.5">
              <AnimatePresence>
                {screenshotSrc && !loadingShot && (
                  <motion.div
                    initial={{ opacity: 0, scale: 0.8 }}
                    animate={{ opacity: 1, scale: 1 }}
                    exit={{ opacity: 0, scale: 0.8 }}
                    className="flex items-center gap-1 text-[9px]"
                    style={{ color: "var(--green)" }}
                  >
                    <CheckCircle2 className="w-3 h-3" aria-hidden="true" />
                    pantalla capturada
                  </motion.div>
                )}
              </AnimatePresence>
            </div>

            {/* Phone mockup */}
            <div className="pb-10">
              <PhoneFrame
                src={screenshotSrc}
                platform={device.platform ?? "android"}
                loading={loadingShot}
                onRefresh={takeShot}
              />
            </div>

            {/* Device identity — único lugar donde aparece el nombre completo */}
            <div className="w-full text-center">
              <p className="text-sm font-semibold" style={{ color: "var(--text-primary)" }}>{device.manufacturer} {device.model}</p>
            </div>
          </div>

          {/* Specs section */}
          <div className="px-5 pb-4" style={{ background: "var(--bg-elevated)", borderTop: "1px solid var(--border)" }}>
            <p className="section-label py-3">Especificaciones</p>
            <SpecRow icon={Hash}     label="IMEI"   value={imeiDisplay !== "INGRESAR_MANUALMENTE" ? imeiDisplay : "—"} mono accent={!imeiManual} />
            <SpecRow icon={FileText} label="Serial" value={device.serial.slice(0, 24)} mono />
            {device.name && device.name !== device.model && (
              <SpecRow icon={User} label="Nombre" value={device.name} />
            )}
          </div>

          {/* IMEI manual warning */}
          {imeiManual && (
            <div
              className="mx-4 mb-4 rounded-md p-3 text-[11px]"
              style={{ background: "rgba(217,119,6,0.07)", border: "1px solid rgba(217,119,6,0.22)" }}
            >
              <p className="font-semibold mb-0.5" style={{ color: "var(--amber)" }}>IMEI no detectado automáticamente</p>
              <p className="text-[10px]" style={{ color: "var(--text-muted)" }}>
                Completá el campo IMEI. Marcá <strong style={{ color: "var(--amber)" }}>*#06#</strong> en el celular para verlo.
              </p>
            </div>
          )}
        </motion.div>

        {/* ── Right: Form ── */}
        <motion.div
          className="rounded-lg p-5 space-y-4"
          style={{ background: "var(--bg-surface)", border: "1px solid var(--border)" }}
          initial={{ opacity: 0, x: 16 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.3, ease: EASE, delay: 0.05 }}
        >
          {isLegacy && (
            <div
              role="status"
              className="flex items-start gap-2.5 rounded-md px-3.5 py-3 text-xs leading-relaxed"
              style={{ background: "rgba(217,119,6,0.07)", border: "1px solid rgba(217,119,6,0.22)", color: "var(--text-secondary)" }}
            >
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" style={{ color: "var(--amber)" }} aria-hidden="true" />
              Este caso se creó antes del informe pericial. Completá los datos de la causa para continuar.
            </div>
          )}

          {/* Título + progreso de obligatorios de todo el paso */}
          <div className="flex items-center justify-between gap-3">
            <h2 className="section-label m-0">Datos de la causa</h2>
            <div className="flex items-center gap-2">
              <div className="h-1.5 w-20 overflow-hidden rounded-full" style={{ background: "var(--border)" }} aria-hidden="true">
                <div
                  className="h-full w-full origin-left rounded-full transition-transform duration-300"
                  style={{ background: "var(--text-primary)", transform: `scaleX(${requiredTotal ? requiredFilled / requiredTotal : 0})` }}
                />
              </div>
              <span className="text-[10px] tabular-nums" style={{ color: "var(--text-muted)" }}>
                {requiredFilled}/{requiredTotal}
                <span className="sr-only"> obligatorios completos</span>
              </span>
            </div>
          </div>

          {/* Tus datos de perito */}
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
            <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">{ACTUACION.map(renderField)}</div>
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
                  <div
                    className="flex items-center gap-3 rounded-md px-3.5 py-2.5"
                    style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
                  >
                    <Hash className="w-3.5 h-3.5 flex-shrink-0" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
                    <div className="flex-1 min-w-0">
                      <p className="text-[0.62rem] font-semibold uppercase tracking-wider" style={{ color: "var(--text-secondary)" }}>IMEI</p>
                      <p className="text-sm font-mono truncate" style={{ color: "var(--text-primary)" }}>{imeiDisplay}</p>
                    </div>
                    <span
                      className="flex items-center gap-1 text-[9px] font-semibold uppercase tracking-wide px-2 py-0.5 rounded-full flex-shrink-0"
                      style={{ background: "rgba(45,212,191,0.1)", color: "var(--blue-lg)" }}
                    >
                      <ScanLine className="w-2.5 h-2.5" aria-hidden="true" /> detectado
                    </span>
                  </div>
                ) : (
                  <FormField id="case-imei" icon={Hash} label="IMEI" sublabel="· ingresalo manualmente" error={errors.imei} required>
                    <input
                      id="case-imei"
                      name="imei"
                      type="text"
                      inputMode="numeric"
                      autoComplete="off"
                      aria-required
                      aria-invalid={!!errors.imei || undefined}
                      aria-describedby={describedBy("case-imei", { error: errors.imei })}
                      className={cn("input text-sm font-mono", errors.imei && "input-error")}
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

          {/* Actions */}
          <div className="flex gap-3 pt-1">
            {mode === "create" && (
              <motion.button type="button" className="btn-secondary flex-shrink-0" onClick={onBack} whileTap={{ scale: 0.98 }}>
                <RotateCcw className="w-3.5 h-3.5" aria-hidden="true" />
                Atrás
              </motion.button>
            )}
            {mode === "edit" && !isLegacy && (
              <motion.button type="button" className="btn-secondary flex-shrink-0" onClick={onCancel} disabled={loading} whileTap={{ scale: 0.98 }}>
                Cancelar
              </motion.button>
            )}

            <motion.button
              type="button"
              className="btn-primary flex-1"
              style={!formComplete ? { opacity: 0.5 } : undefined}
              onClick={onSubmit}
              disabled={loading}
              whileTap={{ scale: 0.98 }}
            >
              {loading ? (
                <><Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Guardando…</>
              ) : (
                <>
                  {mode === "edit" ? "Guardar y continuar" : "Crear caso y continuar"}
                  <ArrowRight className="w-4 h-4" aria-hidden="true" />
                </>
              )}
            </motion.button>
          </div>
        </motion.div>
      </div>
    </div>
  );
}
