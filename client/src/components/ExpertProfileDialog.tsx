"use client";

import { useEffect, useState } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { AlertCircle, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/lib/api";
import { PROFILE_REQUIRED_KEYS, profileErrorKey, profileFieldId, validateProfile } from "@/lib/pericial";
import { profileToForm, useExpertProfile } from "@/hooks/useExpertProfile";
import type { ProfileFormData } from "@/types";
import { ExpertProfileFields } from "./ExpertProfileCard";

const ID_PREFIX = "dlg-";

/**
 * "Mi perfil de perito" (menú de usuario). Editar el perfil no cambia los
 * casos ya creados: cada caso guarda su copia al crearse o editarse.
 */
export function ExpertProfileDialog({ visible, onHide }: { visible: boolean; onHide: () => void }) {
  const { profile, loading, error, save, reload } = useExpertProfile({ autoLoad: false });
  const [form, setForm] = useState<ProfileFormData>(() => profileToForm(null));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState("");

  // Cada apertura trae el perfil vigente.
  useEffect(() => {
    if (!visible) return;
    setErrors({}); setSaveError("");
    void reload().then(p => setForm(profileToForm(p)));
  }, [visible, reload]);

  function focusFirstError(err: Record<string, string>) {
    const k = PROFILE_REQUIRED_KEYS.find(key => err[profileErrorKey(key)]);
    if (k) document.getElementById(`${ID_PREFIX}${profileFieldId(k)}`)?.focus();
  }

  async function handleSave() {
    const err = validateProfile(form);
    setErrors(err);
    if (Object.keys(err).length) { focusFirstError(err); return; }
    setSaving(true); setSaveError("");
    try {
      await save(form);
      toast.success("Perfil de perito guardado", { description: "Se usa en los casos que crees o edites desde ahora." });
      onHide();
    } catch (e) {
      if (e instanceof ApiError && e.missing?.length) {
        const mapped = validateProfile({ ...form, ...Object.fromEntries(e.missing.map(k => [k, ""])) });
        setErrors(mapped);
        focusFirstError(mapped);
      }
      setSaveError(e instanceof Error ? e.message : "No se pudo guardar el perfil");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog
      header="Mi perfil de perito"
      visible={visible}
      onHide={() => { if (!saving) onHide(); }}
      className="w-[min(40rem,100%)]"
      footer={
        <>
          <Button label="Cancelar" severity="secondary" onClick={onHide} disabled={saving} />
          <Button label={saving ? "Guardando…" : "Guardar"} onClick={() => { void handleSave(); }} loading={saving} disabled={loading} />
        </>
      }
    >
      <p className="m-0 mb-4 text-fx-body-sm text-fx-text-2">
        Estos datos van al informe pericial. Se copian a cada caso al crearlo o editarlo; los casos ya generados no cambian.
      </p>
      {loading ? (
        <p className="flex items-center gap-2 text-fx-text-3" role="status">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> Cargando tu perfil…
        </p>
      ) : (
        <>
          {(error || saveError) && (
            <p role="alert" className="m-0 mb-3 flex items-start gap-2 text-fx-body-sm text-fx-danger">
              <AlertCircle className="mt-0.5 h-4 w-4 flex-shrink-0" aria-hidden="true" /> {saveError || error}
            </p>
          )}
          <ExpertProfileFields
            form={form}
            errors={errors}
            onChange={setForm}
            onClearError={k => setErrors(er => ({ ...er, [k]: "" }))}
            idPrefix={ID_PREFIX}
          />
          {profile && !profile.exists && (
            <p className="m-0 mt-3 text-[11px] text-fx-text-3">Todavía no guardaste tu perfil: los valores son sugeridos.</p>
          )}
        </>
      )}
    </Dialog>
  );
}
