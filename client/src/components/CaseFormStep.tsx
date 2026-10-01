"use client";

import { useState, useEffect, useRef } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  Loader2,
  Hash, User, FileText, MessageSquare, RotateCcw,
  ArrowRight, CheckCircle2, ScanLine,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { agent, type Device } from "@/lib/agent";
import { PhoneFrame } from "./PhoneFrame";
import { SpecRow } from "./SpecRow";
import { FormField } from "./FormField";

const EASE = [0.22, 1, 0.36, 1] as const;

/* ── Types ─────────────────────────────────────────────────── */
interface CaseForm {
  NroReferencia: string;
  NombreDenunciante: string;
  DNIDenunciante: string;
  Observaciones: string;
  imeiOverride: string;
}

interface Props {
  device: Device;
  form: CaseForm;
  errors: Record<string, string>;
  loading: boolean;
  onChange: (f: CaseForm) => void;
  onClearError: (key: string) => void;
  onBack: () => void;
  onSubmit: () => void;
}

/* ════════════════════════════════════════════════════════════════
   MAIN COMPONENT
════════════════════════════════════════════════════════════════ */
export function CaseFormStep({ device, form, errors, loading, onChange, onClearError, onBack, onSubmit }: Props) {
  const [screenshotSrc, setScreenshotSrc] = useState<string | null>(null);
  const [loadingShot,   setLoadingShot]   = useState(false);
  const hasTakenFirst = useRef(false);
  const fieldRefs = useRef<Record<string, HTMLInputElement | null>>({});

  const isIOS = device.platform === "ios";
  const osLabel = isIOS
    ? `iOS ${device.ios_version ?? device.android_version}`
    : `Android ${device.android_version}`;

  const imeiDisplay = form.imeiOverride.trim() || device.imei;
  const imeiManual  = device.imei === "INGRESAR_MANUALMENTE";

  const agentBase = process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765";

  const requiredFilled = [form.NroReferencia, form.NombreDenunciante, form.DNIDenunciante]
    .filter(v => v.trim().length > 0).length;
  const formComplete = requiredFilled === 3 && (!imeiManual || form.imeiOverride.trim().length > 0);

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

  // Al fallar la validación (submit con campos vacíos), enfocamos el primer campo con
  // error — evita que el usuario tenga que escanear el formulario para encontrarlo.
  useEffect(() => {
    const firstErrorKey = ["NroReferencia", "NombreDenunciante", "DNIDenunciante"].find(k => errors[k]);
    if (firstErrorKey) fieldRefs.current[firstErrorKey]?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [errors]);

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
          {/* IMEI: dato de solo lectura cuando se detecta automáticamente (ya no un
              <input readOnly> atenuado que sugiere ser editable sin serlo); campo real
              únicamente cuando hace falta cargarlo a mano. */}
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.1, duration: 0.28, ease: EASE }}
          >
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
              <FormField
                id="case-imei"
                icon={Hash}
                label="IMEI"
                sublabel="· ingresalo manualmente"
                error={errors.imeiOverride}
                required
              >
                <input
                  id="case-imei"
                  name="imei"
                  type="text"
                  inputMode="numeric"
                  autoComplete="off"
                  aria-describedby={errors.imeiOverride ? "case-imei-error" : undefined}
                  className={cn("input text-sm font-mono", !form.imeiOverride && "input-error")}
                  placeholder="353799091234567 (15 dígitos)"
                  value={form.imeiOverride}
                  maxLength={17}
                  onChange={e => onChange({ ...form, imeiOverride: e.target.value })}
                />
              </FormField>
            )}
          </motion.div>

          {/* Section header: título + progreso — antes vivían separados (el progreso
              en un banner arriba del todo, desconectado de los campos que mide). */}
          <motion.div
            className="flex items-center justify-between pt-1"
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.15, duration: 0.28, ease: EASE }}
          >
            <span className="section-label">Datos del expediente</span>
            <div className="flex items-center gap-1.5">
              <div className="flex items-center gap-1">
                {[0, 1, 2].map(i => (
                  <motion.div
                    key={i}
                    className="h-1.5 rounded-full"
                    style={{ background: "var(--text-primary)" }}
                    animate={{
                      opacity: i < requiredFilled ? 1 : 0.15,
                      width: i < requiredFilled ? 16 : 10,
                    }}
                    transition={{ duration: 0.25, ease: EASE }}
                  />
                ))}
              </div>
              <span className="text-[9px] tabular-nums" style={{ color: "var(--text-muted)" }}>{requiredFilled}/3</span>
            </div>
          </motion.div>

          {/* Case fields */}
          <div className="space-y-3.5">
            {([
              {
                key: "NroReferencia",
                label: "Número de expediente",
                icon: FileText,
                placeholder: "Ej: MPF-001-2025",
                value: form.NroReferencia,
                error: errors.NroReferencia,
                inputMode: "text" as const,
                onChange: (v: string) => { onChange({ ...form, NroReferencia: v }); onClearError("NroReferencia"); },
              },
              {
                key: "NombreDenunciante",
                label: "Nombre del denunciante",
                icon: User,
                placeholder: "Apellido y nombre completo",
                value: form.NombreDenunciante,
                error: errors.NombreDenunciante,
                inputMode: "text" as const,
                onChange: (v: string) => { onChange({ ...form, NombreDenunciante: v }); onClearError("NombreDenunciante"); },
              },
              {
                key: "DNIDenunciante",
                label: "DNI del denunciante",
                icon: Hash,
                placeholder: "12345678",
                value: form.DNIDenunciante,
                error: errors.DNIDenunciante,
                maxLength: 8,
                inputMode: "numeric" as const,
                onChange: (v: string) => { onChange({ ...form, DNIDenunciante: v }); onClearError("DNIDenunciante"); },
              },
            ] as const).map(({ key, label, icon, placeholder, value, error, inputMode, onChange: handleChange, ...rest }, i) => (
              <motion.div
                key={key}
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.18 + i * 0.05, duration: 0.28, ease: EASE }}
              >
                <FormField id={`case-${key}`} icon={icon} label={label} error={error} required>
                  <div className="relative">
                    <input
                      ref={el => { fieldRefs.current[key] = el; }}
                      id={`case-${key}`}
                      name={key}
                      type="text"
                      inputMode={inputMode}
                      autoComplete="off"
                      aria-describedby={error ? `case-${key}-error` : undefined}
                      className={cn("input pr-9", error && "input-error")}
                      placeholder={placeholder}
                      value={value}
                      onChange={e => handleChange(e.target.value)}
                      {...("maxLength" in rest ? { maxLength: rest.maxLength } : {})}
                    />
                    <AnimatePresence>
                      {value.trim().length > 0 && !error && (
                        <motion.div
                          className="absolute right-3 top-1/2 -translate-y-1/2 pointer-events-none"
                          initial={{ opacity: 0, scale: 0.5 }}
                          animate={{ opacity: 1, scale: 1 }}
                          exit={{ opacity: 0, scale: 0.5 }}
                          transition={{ type: "spring", stiffness: 400, damping: 20 }}
                        >
                          <CheckCircle2 className="w-4 h-4" style={{ color: "var(--green)" }} aria-hidden="true" />
                        </motion.div>
                      )}
                    </AnimatePresence>
                  </div>
                </FormField>
              </motion.div>
            ))}

            <motion.div
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.33, duration: 0.28, ease: EASE }}
            >
              <FormField id="case-observaciones" icon={MessageSquare} label="Observaciones" sublabel="· opcional">
                <textarea
                  id="case-observaciones"
                  name="observaciones"
                  autoComplete="off"
                  className="input resize-none"
                  style={{ minHeight: 88 }}
                  placeholder="Describí brevemente el caso, estado del dispositivo, etc…"
                  value={form.Observaciones}
                  onChange={e => onChange({ ...form, Observaciones: e.target.value })}
                />
              </FormField>
            </motion.div>
          </div>

          {/* Actions */}
          <motion.div
            className="flex gap-3 pt-1"
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.38, duration: 0.28, ease: EASE }}
          >
            <motion.button
              className="btn-secondary flex-shrink-0"
              onClick={onBack}
              whileTap={{ scale: 0.98 }}
            >
              <RotateCcw className="w-3.5 h-3.5" aria-hidden="true" />
              Atrás
            </motion.button>

            <motion.button
              className="btn-primary flex-1"
              style={!formComplete ? { opacity: 0.5 } : undefined}
              onClick={onSubmit}
              disabled={loading}
              whileTap={{ scale: 0.98 }}
            >
              {loading ? (
                <><Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Creando caso…</>
              ) : (
                <>
                  Crear caso y continuar
                  <ArrowRight className="w-4 h-4" aria-hidden="true" />
                </>
              )}
            </motion.button>
          </motion.div>
        </motion.div>
      </div>
    </div>
  );
}
