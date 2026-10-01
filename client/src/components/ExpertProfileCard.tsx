"use client";

import { AnimatePresence, motion } from "framer-motion";
import { BadgeCheck, Briefcase, ChevronDown, Pencil, Scale, User, UserRound } from "lucide-react";
import { cn } from "@/lib/utils";
import { FormField, describedBy } from "./FormField";
import {
  MAX_LEN_MATRICULA, MAX_LEN_PROFILE, profileErrorKey, profileFieldId,
} from "@/lib/pericial";
import type { ExpertProfile, ProfileFormData } from "@/types";

const PROFILE_FIELDS = [
  { key: "nombre",    label: "Nombre completo", icon: User,       placeholder: "Nombre y apellido…",              maxLength: MAX_LEN_PROFILE },
  { key: "matricula", label: "Matrícula",       icon: BadgeCheck, placeholder: "Ej.: 1234/A…",    maxLength: MAX_LEN_MATRICULA },
  { key: "profesion", label: "Profesión",       icon: Briefcase,  placeholder: "Ej.: Ingeniero/a en sistemas…",   maxLength: MAX_LEN_PROFILE },
  { key: "caracter",  label: "Carácter",        icon: Scale,      placeholder: "Ej.: perito informático de parte…", maxLength: MAX_LEN_PROFILE },
] as const;

/**
 * Los cinco campos del perfil del perito. Los usan la tarjeta del paso "Causa"
 * y el diálogo "Mi perfil de perito" del menú de usuario.
 */
export function ExpertProfileFields({
  form, errors, onChange, onClearError, idPrefix = "",
}: {
  form: ProfileFormData;
  /** Claves `perfil.<campo>` (ver `profileErrorKey`). */
  errors: Record<string, string>;
  onChange: (f: ProfileFormData) => void;
  onClearError?: (key: string) => void;
  /** Para no repetir ids si el diálogo y la tarjeta conviven en la página. */
  idPrefix?: string;
}) {
  const fid = (k: keyof ProfileFormData) => `${idPrefix}${profileFieldId(k)}`;
  return (
    <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
      {PROFILE_FIELDS.map(({ key, label, icon, placeholder, maxLength }) => {
        const id = fid(key);
        const error = errors[profileErrorKey(key)];
        return (
          <FormField key={key} id={id} icon={icon} label={label} error={error} required>
            <input
              id={id}
              name={`perfil-${key}`}
              type="text"
              autoComplete={key === "nombre" ? "name" : "off"}
              aria-invalid={!!error || undefined}
              aria-describedby={describedBy(id, { error })}
              className={cn("input", error && "input-error")}
              placeholder={placeholder}
              maxLength={maxLength}
              value={form[key]}
              onChange={e => { onChange({ ...form, [key]: e.target.value }); onClearError?.(profileErrorKey(key)); }}
            />
          </FormField>
        );
      })}
      <FormField id={fid("tratamiento")} icon={UserRound} label="Tratamiento en el informe">
        <select
          id={fid("tratamiento")}
          name="perfil-tratamiento"
          className="input"
          value={form.tratamiento}
          onChange={e => onChange({ ...form, tratamiento: e.target.value === "suscripta" ? "suscripta" : "suscripto" })}
        >
          <option value="suscripto">El suscripto</option>
          <option value="suscripta">La suscripta</option>
        </select>
      </FormField>
    </div>
  );
}

/**
 * Tarjeta "Tus datos de perito" del paso "Causa": plegada con un resumen si el
 * perfil está completo, desplegada si falta algo.
 */
export function ExpertProfileCard({
  profile, loading, form, errors, expanded, onToggle, onChange, onClearError,
}: {
  profile: ExpertProfile | null;
  loading: boolean;
  form: ProfileFormData;
  errors: Record<string, string>;
  expanded: boolean;
  onToggle: () => void;
  onChange: (f: ProfileFormData) => void;
  onClearError: (key: string) => void;
}) {
  const summary = [form.nombre.trim(), form.matricula.trim() && `M.P. ${form.matricula.trim()}`]
    .filter(Boolean).join(" · ");

  return (
    <div className="rounded-md" style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}>
      <div className="flex items-center gap-3 px-3.5 py-2.5">
        <User className="h-3.5 w-3.5 flex-shrink-0" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <p className="text-[0.62rem] font-semibold uppercase tracking-wider" style={{ color: "var(--text-secondary)" }}>
            Tus datos de perito
          </p>
          {!expanded && (
            <p className="truncate text-sm" style={{ color: "var(--text-primary)" }}>
              {loading ? "Cargando…" : summary || "Sin completar"}
            </p>
          )}
          {expanded && profile && !profile.is_complete && (
            <p className="text-[11px]" style={{ color: "var(--text-muted)" }}>
              Se guardan en tu perfil y se copian a cada caso nuevo.
            </p>
          )}
        </div>
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={expanded}
          aria-controls="expert-profile-fields"
          className="btn-ghost btn-sm flex flex-shrink-0 items-center gap-1.5"
        >
          {expanded
            ? <><ChevronDown className="h-3.5 w-3.5 rotate-180" aria-hidden="true" /> Plegar</>
            : <><Pencil className="h-3.5 w-3.5" aria-hidden="true" /> Editar</>}
        </button>
      </div>

      <AnimatePresence initial={false}>
        {expanded && (
          <motion.div
            id="expert-profile-fields"
            className="overflow-hidden"
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="border-t px-3.5 pb-3.5 pt-3" style={{ borderColor: "var(--border)" }}>
              <ExpertProfileFields form={form} errors={errors} onChange={onChange} onClearError={onClearError} />
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
