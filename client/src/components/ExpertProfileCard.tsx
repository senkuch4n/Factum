"use client";

import { Button } from "primereact/button";
import { Dropdown } from "primereact/dropdown";
import { InputText } from "primereact/inputtext";
import { BadgeCheck, Briefcase, ChevronUp, Pencil, Scale, User, UserRound } from "lucide-react";
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

const TRATAMIENTO_OPTIONS = [
  { label: "El suscripto", value: "suscripto" },
  { label: "La suscripta", value: "suscripta" },
];

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
    <div className="grid grid-cols-1 items-start gap-x-4 gap-y-6 sm:grid-cols-2">
      {PROFILE_FIELDS.map(({ key, label, icon, placeholder, maxLength }) => {
        const id = fid(key);
        const error = errors[profileErrorKey(key)];
        return (
          <FormField key={key} id={id} icon={icon} label={label} error={error} required>
            <InputText
              id={id}
              name={`perfil-${key}`}
              autoComplete={key === "nombre" ? "name" : "off"}
              invalid={!!error}
              aria-invalid={!!error || undefined}
              aria-describedby={describedBy(id, { error })}
              placeholder={placeholder}
              maxLength={maxLength}
              value={form[key]}
              onChange={e => { onChange({ ...form, [key]: e.target.value }); onClearError?.(profileErrorKey(key)); }}
            />
          </FormField>
        );
      })}
      <FormField id={fid("tratamiento")} icon={UserRound} label="Tratamiento en el informe">
        <Dropdown
          inputId={fid("tratamiento")}
          name="perfil-tratamiento"
          value={form.tratamiento}
          options={TRATAMIENTO_OPTIONS}
          optionLabel="label"
          optionValue="value"
          onChange={e => onChange({ ...form, tratamiento: e.value === "suscripta" ? "suscripta" : "suscripto" })}
          className="w-full"
        />
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

  // Fila plana (sin tarjeta propia): el formulario es una sola superficie y las
  // secciones se separan con divisores (formulario-causa-rediseno, reintento 1).
  return (
    <div className="pb-5">
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-fx-surface-2 text-fx-text-2">
          <UserRound className="h-4 w-4" />
        </span>
        <div className="min-w-0 flex-1">
          <h3 className="m-0 text-xs font-normal text-fx-text-3">Tus datos de perito</h3>
          {!expanded && (
            <p className="m-0 truncate text-fx-body-sm font-medium text-fx-text">
              {loading ? "Cargando…" : summary || "Sin completar"}
            </p>
          )}
          {expanded && profile && !profile.is_complete && (
            <p className="m-0 text-fx-body-sm text-fx-text-2">
              Se guardan en tu perfil y se copian a cada caso nuevo.
            </p>
          )}
        </div>
        <Button
          type="button"
          text
          severity="secondary"
          size="small"
          icon={expanded
            ? <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" />
            : <Pencil className="h-3.5 w-3.5" aria-hidden="true" />}
          label={expanded ? "Plegar" : "Editar"}
          aria-expanded={expanded}
          aria-controls="expert-profile-fields"
          onClick={onToggle}
          className="shrink-0"
        />
      </div>

      {expanded && (
        <div
          id="expert-profile-fields"
          className="pt-5 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
        >
          <ExpertProfileFields form={form} errors={errors} onChange={onChange} onClearError={onClearError} />
        </div>
      )}
    </div>
  );
}
