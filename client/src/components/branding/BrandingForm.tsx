"use client";

import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { Building2, RotateCcw, Sparkles } from "lucide-react";
import { FormField, describedBy } from "@/components/FormField";
import {
  BRANDING_LIMITS, effectiveColor, type BrandingErrorKey, type BrandingErrors, type BrandingFormValues,
  type BrandingImageValue,
} from "@/lib/branding";
import type { AccountBranding, BrandingImageKind } from "@/types";
import { BrandingImageField, useBrandingImageSrc } from "./BrandingImageField";
import { BrandingPreview } from "./BrandingPreview";
import { ColorField } from "./ColorField";
import { ContactLinesField } from "./ContactLinesField";

/** Ids de los controles: el diálogo los usa para llevar el foco al primer error. */
export function brandingFieldId(prefix: string, key: BrandingErrorKey): string {
  if (key === "contact_lines") return `${prefix}contact_lines-0`;
  if (key.startsWith("contact_lines.")) return `${prefix}contact_lines-${key.slice("contact_lines.".length)}`;
  return `${prefix}${key}`;
}

const IMAGE_HINT =
  "PNG o JPEG, hasta 1 MB, entre 16 y 4096 px por lado. Para el isotipo conviene PNG con fondo transparente.";

interface Props {
  idPrefix: string;
  /** Marca guardada (imágenes actuales). */
  base: AccountBranding;
  form: BrandingFormValues;
  onChange: (next: BrandingFormValues) => void;
  errors: BrandingErrors;
  onFocusField?: (key: BrandingErrorKey | null) => void;
  onBlurField?: (key: BrandingErrorKey) => void;
  onImageError: (kind: BrandingImageKind, message: string | null) => void;
  /** Sugerencia de D10 (solo si el nombre está vacío). */
  suggestion: string | null;
  disabled?: boolean;
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <h3 className="m-0 text-fx-body-sm font-semibold text-fx-text">{children}</h3>;
}

/**
 * Formulario de la marca del informe (marca-por-cliente §9.4): dos columnas
 * desde `lg` (formulario | vista previa `sticky`), una en mobile con la vista
 * previa debajo. Presentacional: el estado y el guardado son del diálogo.
 */
export function BrandingForm({
  idPrefix, base, form, onChange, errors, onFocusField, onBlurField, onImageError, suggestion, disabled,
}: Props) {
  const logo = useBrandingImageSrc(base.logo);
  const isotype = useBrandingImageSrc(base.isotype);

  const set = <K extends keyof BrandingFormValues>(k: K, v: BrandingFormValues[K]) => onChange({ ...form, [k]: v });

  function imageSrc(v: BrandingImageValue, currentSrc: string | null): string | null {
    if (v.action === "replace") return v.previewUrl;
    if (v.action === "remove") return null;
    return currentSrc;
  }

  const nameId = brandingFieldId(idPrefix, "organization_name");
  const nameLen = form.organization_name.trim().length;
  const nameHint = "Va en el membrete cuando no hay logo.";
  const showSuggestion = !!suggestion && form.organization_name.trim() === "";
  const usingDefaults = form.primary_color === "" && form.accent_color === "";

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,20rem)] lg:gap-8">
      <div className="flex min-w-0 flex-col gap-6">
        <section className="flex flex-col gap-3.5" aria-labelledby={`${idPrefix}sec-id`}>
          <SectionTitle><span id={`${idPrefix}sec-id`}>Identidad</span></SectionTitle>
          <FormField id={nameId} icon={Building2} label="Nombre de la organización" sublabel="(opcional)"
            error={errors.organization_name} hint={nameHint}>
            <InputText
              id={nameId}
              name="marca-nombre"
              value={form.organization_name}
              autoComplete="organization"
              placeholder="Estudio, fuerza o razón social…"
              disabled={disabled}
              invalid={!!errors.organization_name}
              aria-invalid={!!errors.organization_name || undefined}
              aria-describedby={[describedBy(nameId, { hint: nameHint, error: errors.organization_name }), `${nameId}-count`].filter(Boolean).join(" ")}
              onChange={e => set("organization_name", e.target.value)}
              onFocus={() => onFocusField?.("organization_name")}
              onBlur={() => onBlurField?.("organization_name")}
            />
            <div className="mt-1 flex flex-wrap items-center justify-between gap-2">
              {showSuggestion ? (
                <button
                  type="button"
                  onClick={() => set("organization_name", suggestion ?? "")}
                  disabled={disabled}
                  className="inline-flex min-h-8 max-w-full items-center gap-1.5 rounded-fx-pill border border-fx-border-strong bg-fx-surface-2 px-3 text-xs font-medium text-fx-text-2 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring max-md:min-h-11"
                >
                  <Sparkles className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <span className="truncate">Usar «{suggestion}»</span>
                </button>
              ) : <span />}
              <p
                id={`${nameId}-count`}
                className={`m-0 text-xs tabular-nums ${nameLen > BRANDING_LIMITS.name ? "text-fx-danger" : "text-fx-text-3"}`}
              >
                {nameLen}/{BRANDING_LIMITS.name}
              </p>
            </div>
          </FormField>
        </section>

        <section className="flex flex-col gap-3.5" aria-labelledby={`${idPrefix}sec-img`}>
          <SectionTitle><span id={`${idPrefix}sec-img`}>Imágenes</span></SectionTitle>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <BrandingImageField
              id={brandingFieldId(idPrefix, "logo")}
              label="Logo"
              noun="Logo"
              current={base.logo}
              currentSrc={logo.src}
              currentLoading={!!base.logo && !logo.src && !logo.failed}
              value={form.logo}
              onChange={v => set("logo", v)}
              error={errors.logo}
              onError={m => onImageError("logo", m)}
              hint={IMAGE_HINT}
              disabled={disabled}
            />
            <BrandingImageField
              id={brandingFieldId(idPrefix, "isotype")}
              label="Isotipo"
              noun="Isotipo"
              current={base.isotype}
              currentSrc={isotype.src}
              currentLoading={!!base.isotype && !isotype.src && !isotype.failed}
              value={form.isotype}
              onChange={v => set("isotype", v)}
              error={errors.isotype}
              onError={m => onImageError("isotype", m)}
              hint={IMAGE_HINT}
              disabled={disabled}
            />
          </div>
        </section>

        <section className="flex flex-col gap-3.5" aria-label="Contacto">
          <ContactLinesField
            idPrefix={`${idPrefix}contact_lines`}
            lines={form.contact_lines}
            onChange={lines => set("contact_lines", lines)}
            onBlurLine={i => onBlurField?.(`contact_lines.${i}`)}
            lineErrors={form.contact_lines.map((_, i) => errors[`contact_lines.${i}`])}
            error={errors.contact_lines}
            disabled={disabled}
          />
        </section>

        <section className="flex flex-col gap-3.5" aria-labelledby={`${idPrefix}sec-colors`}>
          <SectionTitle><span id={`${idPrefix}sec-colors`}>Colores</span></SectionTitle>
          <ColorField
            id={brandingFieldId(idPrefix, "primary_color")}
            label="Color primario"
            role="primary"
            value={form.primary_color}
            onChange={v => set("primary_color", v)}
            onFocus={() => onFocusField?.("primary_color")}
            onBlur={() => onBlurField?.("primary_color")}
            error={errors.primary_color}
            hint="Títulos, números de sección y filetes. Tiene que leerse sobre blanco."
            disabled={disabled}
          />
          <ColorField
            id={brandingFieldId(idPrefix, "accent_color")}
            label="Color de acento"
            role="accent"
            value={form.accent_color}
            onChange={v => set("accent_color", v)}
            onFocus={() => onFocusField?.("accent_color")}
            onBlur={() => onBlurField?.("accent_color")}
            error={errors.accent_color}
            hint="Fondo de las filas destacadas. El texto va encima, así que conviene un tono claro."
            disabled={disabled}
          />
          <div>
            <Button
              type="button"
              size="small"
              severity="secondary"
              text
              label="Usar colores de Factum"
              icon={<RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
              disabled={disabled || usingDefaults}
              onClick={() => onChange({ ...form, primary_color: "", accent_color: "" })}
              className="max-md:min-h-11"
            />
          </div>
        </section>
      </div>

      <aside aria-label="Vista previa" className="min-w-0 lg:sticky lg:top-0 lg:self-start">
        <BrandingPreview
          organizationName={form.organization_name}
          contactLines={form.contact_lines}
          primary={effectiveColor(form.primary_color, "primary")}
          accent={effectiveColor(form.accent_color, "accent")}
          logoSrc={imageSrc(form.logo, logo.src)}
          isotypeSrc={imageSrc(form.isotype, isotype.src)}
        />
      </aside>
    </div>
  );
}
