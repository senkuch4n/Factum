"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { RefreshCw } from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";
import { useFxToast } from "@/components/shell/FxToastProvider";
import { setMyBranding } from "@/hooks/useMyBranding";
import { usePublicConfig } from "@/hooks/usePublicConfig";
import { ApiError, api } from "@/lib/api";
import {
  brandingErrorMessage, brandingToForm, buildBrandingFormData, firstErrorKey, sameBrandingForm, sentLineIndexes,
  validateBrandingForm, type BrandingErrorKey, type BrandingErrors, type BrandingFormValues,
} from "@/lib/branding";
import type { AccountBranding, BrandingField } from "@/types";
import { BrandingForm, brandingFieldId } from "./BrandingForm";

export type BrandingTarget =
  | { kind: "self" }
  | { kind: "account"; userId: string; name: string; dni: string };

interface Props {
  visible: boolean;
  onHide: () => void;
  target: BrandingTarget;
  /** Guardado exitoso (con o sin cambios). */
  onSaved?: (b: AccountBranding) => void;
}

const ID_PREFIX = "brand-";
const FORM_ID = "branding-form";

type Banner = { text: string; stale?: boolean } | null;

/** Skeleton del formulario mientras llega la marca. */
function FormSkeleton() {
  const bar = "rounded-fx-md bg-fx-surface-3 motion-safe:animate-pulse";
  return (
    <div role="status" aria-label="Cargando la marca" className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,20rem)] lg:gap-8">
      <div className="flex flex-col gap-5">
        <div className={`h-3 w-40 ${bar}`} />
        <div className={`h-10 w-full ${bar}`} />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div className={`h-24 ${bar}`} />
          <div className={`h-24 ${bar}`} />
        </div>
        <div className={`h-10 w-full ${bar}`} />
        <div className={`h-10 w-2/3 ${bar}`} />
      </div>
      <div className={`aspect-[210/297] w-full max-w-[22rem] justify-self-center ${bar}`} />
      <span className="sr-only">Cargando…</span>
    </div>
  );
}

/** Libera los object URLs de las imágenes nuevas del formulario. */
function revokeFormUrls(f: BrandingFormValues | null) {
  if (!f) return;
  if (f.logo.previewUrl) URL.revokeObjectURL(f.logo.previewUrl);
  if (f.isotype.previewUrl) URL.revokeObjectURL(f.isotype.previewUrl);
}

/**
 * Marca del informe (marca-por-cliente §9.4). El mismo diálogo sirve para la
 * propia ("Marca del informe" del menú) y para la de otra cuenta desde el
 * panel ("Editar marca", texto en tercera persona). Cada apertura trae la
 * marca vigente; guarda con control optimista (`expected_updated_at`).
 */
export function BrandingDialog({ visible, onHide, target, onSaved }: Props) {
  const toast = useFxToast();
  const { organizationName: installName, organizationLogoSrc: installLogo } = usePublicConfig();
  const self = target.kind === "self";
  const targetUserId = target.kind === "account" ? target.userId : null;

  const [base, setBase] = useState<AccountBranding | null>(null);
  const [form, setForm] = useState<BrandingFormValues>(() => brandingToForm(null));
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  /** Errores que vinieron del backend (400), hasta que se edita ese campo. */
  const [serverErrors, setServerErrors] = useState<BrandingErrors>({});
  /** Errores locales de imagen (archivo rechazado al elegirlo). */
  const [imageErrors, setImageErrors] = useState<BrandingErrors>({});
  const [focused, setFocused] = useState<BrandingErrorKey | null>(null);
  const [saving, setSaving] = useState(false);
  const [banner, setBanner] = useState<Banner>(null);
  const [reloading, setReloading] = useState(false);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const inFlight = useRef(false);
  const loadSeq = useRef(0);
  const formRef = useRef<BrandingFormValues | null>(null);
  formRef.current = form;

  const initial = useMemo(() => brandingToForm(base), [base]);
  const dirty = !!base && !sameBrandingForm(form, initial);
  const liveErrors = useMemo(() => validateBrandingForm(form), [form]);
  const hasLiveErrors = Object.keys(liveErrors).length > 0;

  /**
   * Errores visibles: los locales en vivo (salvo el formato del color que se
   * está escribiendo), los de imagen y los del backend.
   */
  const visibleErrors = useMemo<BrandingErrors>(() => {
    const out: BrandingErrors = {};
    for (const [k, v] of Object.entries(liveErrors) as [BrandingErrorKey, string][]) {
      const typingColor = k === focused && (k === "primary_color" || k === "accent_color");
      if (!typingColor) out[k] = v;
    }
    return { ...serverErrors, ...out, ...imageErrors };
  }, [liveErrors, serverErrors, imageErrors, focused]);

  const fetchBranding = useCallback(
    () => (targetUserId ? api.adminGetBranding(targetUserId) : api.getMyBranding()),
    [targetUserId],
  );

  const resetFor = useCallback((b: AccountBranding | null) => {
    revokeFormUrls(formRef.current);
    setBase(b);
    setForm(brandingToForm(b));
    setServerErrors({});
    setImageErrors({});
    setBanner(null);
  }, []);

  const load = useCallback(async () => {
    const mine = ++loadSeq.current;
    setLoading(true);
    setLoadError(null);
    try {
      const b = await fetchBranding();
      if (mine !== loadSeq.current) return null;
      resetFor(b);
      return b;
    } catch (e) {
      if (mine === loadSeq.current) setLoadError(brandingErrorMessage(e));
      return null;
    } finally {
      if (mine === loadSeq.current) setLoading(false);
    }
  }, [fetchBranding, resetFor]);

  // Cada apertura trae la marca vigente; al cerrar se liberan las imágenes nuevas.
  useEffect(() => {
    if (visible) {
      resetFor(null);
      void load();
      return;
    }
    loadSeq.current++;
    revokeFormUrls(formRef.current);
    setForm(brandingToForm(null));
    setBase(null);
  }, [visible, load, resetFor]);

  // Desmontaje con el diálogo abierto.
  useEffect(() => () => revokeFormUrls(formRef.current), []);

  function focusKey(key: BrandingErrorKey | null) {
    if (!key) return;
    requestAnimationFrame(() => document.getElementById(brandingFieldId(ID_PREFIX, key))?.focus());
  }

  function handleChange(next: BrandingFormValues) {
    // Un error del backend se va cuando se edita ese campo.
    const changed = (k: BrandingErrorKey): boolean => {
      if (k === "metadata") return false;
      if (k.startsWith("contact_lines")) return next.contact_lines.join("\n") !== form.contact_lines.join("\n");
      const f = k as Exclude<keyof BrandingFormValues, "contact_lines">;
      return next[f] !== form[f];
    };
    setServerErrors(prev => Object.fromEntries(
      Object.entries(prev).filter(([k]) => !changed(k as BrandingErrorKey)),
    ) as BrandingErrors);
    setForm(next);
  }

  function handleBlur(key: BrandingErrorKey) {
    setFocused(f => (f === key ? null : f));
  }

  function requestClose() {
    if (saving) return;
    if (dirty) setConfirmDiscard(true);
    else onHide();
  }

  async function handleReload() {
    setReloading(true);
    const b = await load();
    setReloading(false);
    if (b) toast.info("Marca recargada", "Ves la marca actual. Volvé a hacer tus cambios si hace falta.");
  }

  async function handleSubmit() {
    if (inFlight.current || !base) return;
    setBanner(null);
    // Un archivo rechazado al elegirlo no cambia la acción: no frena el guardado.
    const first = firstErrorKey(validateBrandingForm(form), form.contact_lines.length);
    if (first) { focusKey(first); return; }

    if (base.exists && sameBrandingForm(form, initial)) {
      toast.info("No había cambios para guardar.");
      onHide();
      return;
    }

    inFlight.current = true;
    setSaving(true);
    const lineMap = sentLineIndexes(form.contact_lines);
    try {
      const fd = buildBrandingFormData(form, base);
      const res = targetUserId ? await api.adminSaveBranding(targetUserId, fd) : await api.saveMyBranding(fd);
      if (res.changed) toast.success("Marca guardada. Se aplica a los próximos informes.");
      else toast.info("No había cambios para guardar.");
      if (self) setMyBranding(res.branding);
      onSaved?.(res.branding);
      onHide();
    } catch (e) {
      handleError(e, lineMap);
    } finally {
      inFlight.current = false;
      setSaving(false);
    }
  }

  function handleError(e: unknown, lineMap: number[]) {
    const code = e instanceof ApiError ? e.code : null;
    const body = e instanceof ApiError ? e.body : null;
    if (code === "validation_failed" && body?.field && body.field !== "metadata") {
      const field = body.field as BrandingField;
      const msg = (e instanceof ApiError && e.serverMessage) || brandingErrorMessage(e);
      let key: BrandingErrorKey = field;
      if (field === "contact_lines" && typeof body.index === "number") {
        key = `contact_lines.${lineMap[body.index] ?? body.index}`;
      }
      setServerErrors(s => ({ ...s, [key]: msg }));
      focusKey(key);
      return;
    }
    if (code === "stale_update") {
      setBanner({ text: brandingErrorMessage(e), stale: true });
      return;
    }
    setBanner({ text: brandingErrorMessage(e) });
  }

  const installHasBrand = !!installName || !!installLogo;
  const notice = !base || base.exists ? null : self
    ? (installHasBrand
        ? "Todavía no cargaste tu marca. Tus informes salen con la marca predeterminada de la instalación. Cuando guardes, tus informes van a usar solo lo que cargues acá, en lugar de la marca predeterminada de la instalación."
        : "Todavía no cargaste tu marca. Tus informes salen sin membrete.")
    : (installHasBrand
        ? "Esta cuenta todavía no cargó su marca. Sus informes salen con la marca predeterminada de la instalación. Cuando guardes, sus informes van a usar solo lo que cargues acá, en lugar de la marca predeterminada de la instalación."
        : "Esta cuenta todavía no cargó su marca. Sus informes salen sin membrete.");

  const header = target.kind === "self" ? "Marca del informe" : `Marca de ${target.name}`;
  const busy = saving || reloading;

  return (
    <>
      <Dialog
        header={header}
        visible={visible}
        onHide={requestClose}
        closeOnEscape={!confirmDiscard}
        draggable={false}
        resizable={false}
        pt={{
          root: { className: "w-[min(64rem,100%)] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0" },
          mask: { className: "max-sm:p-0" },
        }}
        footer={
          <>
            <Button label="Cancelar" severity="secondary" onClick={requestClose} disabled={saving} />
            <Button
              type="submit"
              form={FORM_ID}
              label={saving ? "Guardando…" : "Guardar marca"}
              loading={saving}
              disabled={loading || reloading || !base || hasLiveErrors}
            />
          </>
        }
      >
        <p className="m-0 mb-4 text-fx-body-sm text-fx-text-2">
          Se aplica a los informes que se generen desde ahora. Los informes ya generados no cambian.
        </p>

        {loading && !base ? (
          <FormSkeleton />
        ) : loadError && !base ? (
          <FxBanner tone="error">
            <span className="flex flex-wrap items-center gap-x-3 gap-y-2">
              <span>{loadError}</span>
              <Button
                type="button"
                size="small"
                severity="secondary"
                label="Reintentar"
                icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                onClick={() => { void load(); }}
              />
            </span>
          </FxBanner>
        ) : base ? (
          <>
            {banner && (
              <FxBanner tone="error" className="mb-4">
                <span className="flex flex-wrap items-center gap-x-3 gap-y-2">
                  <span>{banner.text}</span>
                  {banner.stale && (
                    <Button
                      type="button"
                      size="small"
                      severity="secondary"
                      label={reloading ? "Recargando…" : "Recargar"}
                      icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                      loading={reloading}
                      onClick={() => { void handleReload(); }}
                    />
                  )}
                </span>
              </FxBanner>
            )}
            {notice && <FxBanner tone="info" className="mb-4">{notice}</FxBanner>}
            <form
              id={FORM_ID}
              noValidate
              onSubmit={(e) => { e.preventDefault(); void handleSubmit(); }}
            >
              <BrandingForm
                idPrefix={ID_PREFIX}
                base={base}
                form={form}
                onChange={handleChange}
                errors={visibleErrors}
                onFocusField={setFocused}
                onBlurField={handleBlur}
                onImageError={(kind, message) => setImageErrors(e => ({ ...e, [kind]: message ?? undefined }))}
                suggestion={base.exists ? null : base.suggested_organization_name}
                disabled={busy}
              />
            </form>
          </>
        ) : null}
      </Dialog>
      <ConfirmDialog
        open={confirmDiscard}
        onOpenChange={setConfirmDiscard}
        onConfirm={onHide}
        tone="neutral"
        title="¿Descartar los cambios?"
        description="Lo que cargaste en la marca no se va a guardar."
        confirmLabel="Descartar"
        cancelLabel="Seguir editando"
      />
    </>
  );
}
