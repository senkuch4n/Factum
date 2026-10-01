"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import { InputTextarea } from "primereact/inputtextarea";
import {
  AlertCircle, ArrowLeft, ArrowRight, CheckCircle2, ClipboardList, FileText, Loader2, Undo2,
} from "lucide-react";
import { api } from "@/lib/api";
import { EMPTY_REPORT_TEXTS, MAX_LEN_TEXT, reportFieldId } from "@/lib/pericial";
import type { ReportTexts, ReportTextsInput } from "@/types";
import { FormField } from "./FormField";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";
import { FxBanner } from "@/components/feedback/FxBanner";
import { StepHeader } from "@/components/wizard/StepHeader";
import { StepActions } from "@/components/wizard/StepActions";

type TextKey = keyof ReportTextsInput;

const SECTIONS: { key: TextKey; label: string; required: boolean; hasDefault: boolean; placeholder?: string }[] = [
  { key: "objeto_informe",          label: "Objeto del informe",            required: false, hasDefault: false },
  { key: "operaciones_realizadas",  label: "Operaciones realizadas",        required: true,  hasDefault: true },
  { key: "aseguramiento_evidencia", label: "Aseguramiento de la evidencia", required: true,  hasDefault: true },
  { key: "resultados",              label: "Resultados",                    required: true,  hasDefault: false, placeholder: "Qué se encontró en el dispositivo…" },
  { key: "valoracion_tecnica",      label: "Valoración técnica",            required: true,  hasDefault: false, placeholder: "Análisis técnico de lo encontrado…" },
  { key: "conclusiones",            label: "Conclusiones",                  required: true,  hasDefault: false },
  { key: "notas_tecnicas",          label: "Notas técnicas",                required: false, hasDefault: true },
  { key: "reserva",                 label: "Reserva",                       required: false, hasDefault: true },
];

const AUTOSAVE_MS = 1200;

type SaveState = "loading" | "load-error" | "idle" | "pending" | "saving" | "saved" | "error";

interface Props {
  caseId: string;
  /** Campo a enfocar al montar (enlaces del checklist de "Generar"). */
  focusFieldId?: string | null;
  onFocusConsumed?: () => void;
  onSaved: (texts: ReportTexts) => void;
  onBack: () => void;
  onContinue: () => void;
}

/**
 * Paso 4 "Informe": los textos largos del informe pericial, con autoguardado
 * en el servidor (`PUT /api/cases/{id}/report-texts`).
 */
export function ReportStep({ caseId, focusFieldId, onFocusConsumed, onSaved, onBack, onContinue }: Props) {
  const [texts, setTexts] = useState<ReportTextsInput>(EMPTY_REPORT_TEXTS);
  const [state, setState] = useState<SaveState>("loading");
  const [loadError, setLoadError] = useState("");
  const [restoreKey, setRestoreKey] = useState<TextKey | null>(null);
  const [restoring, setRestoring] = useState<TextKey | null>(null);

  const textsRef = useRef(texts);
  const dirtyRef = useRef(false);
  const timerRef = useRef<number | null>(null);
  const seqRef = useRef(0);
  const defaultsRef = useRef<ReportTextsInput | null>(null);
  const onSavedRef = useRef(onSaved);
  useEffect(() => { onSavedRef.current = onSaved; }, [onSaved]);

  /* ── Guardado ── */
  const save = useCallback(async (): Promise<boolean> => {
    if (timerRef.current) { window.clearTimeout(timerRef.current); timerRef.current = null; }
    const seq = ++seqRef.current;
    dirtyRef.current = false;
    setState("saving");
    try {
      const res = await api.saveReportTexts(caseId, textsRef.current);
      onSavedRef.current(res);
      // Solo el último guardado define el estado visible.
      if (seq === seqRef.current) setState(dirtyRef.current ? "pending" : "saved");
      return true;
    } catch {
      dirtyRef.current = true;
      if (seq === seqRef.current) setState("error");
      return false;
    }
  }, [caseId]);

  const schedule = useCallback(() => {
    dirtyRef.current = true;
    setState(s => (s === "saving" ? s : "pending"));
    if (timerRef.current) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => { void save(); }, AUTOSAVE_MS);
  }, [save]);

  const flush = useCallback(async () => {
    if (dirtyRef.current) return save();
    return true;
  }, [save]);

  function update(next: ReportTextsInput) {
    textsRef.current = next;
    setTexts(next);
    schedule();
  }

  /* ── Carga: el caso completo; si no hay textos, los defaults y un PUT inmediato ── */
  const load = useCallback(async () => {
    setState("loading");
    setLoadError("");
    try {
      const { cas } = await api.getCase(caseId);
      if (cas.report_texts) {
        const t = { ...EMPTY_REPORT_TEXTS };
        (Object.keys(EMPTY_REPORT_TEXTS) as TextKey[]).forEach(k => { t[k] = cas.report_texts?.[k] ?? ""; });
        textsRef.current = t;
        setTexts(t);
        setState("idle");
      } else {
        const defaults = await api.getReportTextDefaults(caseId);
        defaultsRef.current = { ...EMPTY_REPORT_TEXTS, ...defaults };
        textsRef.current = defaultsRef.current;
        setTexts(defaultsRef.current);
        await save();
      }
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : "No se pudieron cargar los textos del informe");
      setState("load-error");
    }
  }, [caseId, save]);

  useEffect(() => { void load(); }, [load]);

  // Al desmontar (Atrás, salto de paso) no se pierde lo último que se escribió.
  useEffect(() => () => {
    if (timerRef.current) window.clearTimeout(timerRef.current);
    if (dirtyRef.current) {
      api.saveReportTexts(caseId, textsRef.current).then(r => onSavedRef.current(r)).catch(() => {});
    }
  }, [caseId]);

  // Foco pedido desde el checklist, una vez cargado.
  useEffect(() => {
    if (!focusFieldId || state === "loading") return;
    const el = document.getElementById(focusFieldId);
    if (el) { el.focus(); el.scrollIntoView({ block: "center", behavior: "smooth" }); }
    onFocusConsumed?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusFieldId, state === "loading"]);

  /* ── Restaurar texto por defecto ── */
  async function getDefaults(): Promise<ReportTextsInput> {
    if (!defaultsRef.current) {
      const d = await api.getReportTextDefaults(caseId);
      defaultsRef.current = { ...EMPTY_REPORT_TEXTS, ...d };
    }
    return defaultsRef.current;
  }

  async function requestRestore(key: TextKey) {
    setRestoring(key);
    try {
      const d = await getDefaults();
      const cur = textsRef.current[key];
      if (cur.trim() && cur !== d[key]) setRestoreKey(key);
      else if (cur !== d[key]) update({ ...textsRef.current, [key]: d[key] });
    } catch {
      // Sin defaults (servidor caído): el texto actual queda como está.
    } finally {
      setRestoring(null);
    }
  }

  function confirmRestore() {
    const key = restoreKey;
    const d = defaultsRef.current;
    if (!key || !d) return;
    update({ ...textsRef.current, [key]: d[key] });
  }

  async function handleContinue() {
    if (await flush()) onContinue();
  }

  async function handleBack() {
    await flush();
    onBack();
  }

  const busy = state === "saving" || state === "loading";
  const blocked = busy || state === "error" || state === "load-error";

  return (
    <div className="mx-auto max-w-3xl space-y-5">
      <ConfirmDialog
        open={restoreKey !== null}
        onOpenChange={o => { if (!o) setRestoreKey(null); }}
        onConfirm={confirmRestore}
        title="¿Restaurar el texto por defecto?"
        description="Se reemplaza lo que escribiste en esta sección por el texto por defecto."
        confirmLabel="Restaurar"
        cancelLabel="Conservar mi texto"
        tone="neutral"
      />

      <StepHeader
        title="Redactá el informe"
        description="Cada sección va al informe pericial tal como la escribas. Los cambios se guardan solos."
        aside={<SaveIndicator state={state} onRetry={() => { void save(); }} />}
      />

      {state === "load-error" ? (
        <FxBanner tone="error">
          <p className="m-0">{loadError}</p>
          <Button
            type="button"
            severity="secondary"
            size="small"
            label="Reintentar"
            onClick={() => { void load(); }}
            className="mt-2"
          />
        </FxBanner>
      ) : state === "loading" ? (
        <div role="status" className="flex items-center justify-center gap-2 py-12 text-fx-body-sm text-fx-text-2">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> Cargando los textos del informe…
        </div>
      ) : (
        <div className="space-y-5 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
          {SECTIONS.map(({ key, label, required, hasDefault, placeholder }) => {
            const id = reportFieldId(key);
            return (
              <div key={key}>
                <FormField
                  id={id}
                  icon={key === "operaciones_realizadas" ? ClipboardList : FileText}
                  label={label}
                  sublabel={required ? undefined : "· opcional"}
                  required={required}
                >
                  <InputTextarea
                    id={id}
                    name={key}
                    autoResize
                    rows={4}
                    // Por pt: el pt global (`min-h-[5rem]`) pisaría un className de props.
                    pt={{ root: { className: "min-h-24 leading-relaxed" } }}
                    aria-required={required || undefined}
                    maxLength={MAX_LEN_TEXT}
                    placeholder={placeholder}
                    value={texts[key]}
                    onChange={e => update({ ...textsRef.current, [key]: e.target.value })}
                    onBlur={() => { void flush(); }}
                  />
                </FormField>
                {hasDefault && (
                  <Button
                    type="button"
                    text
                    severity="secondary"
                    size="small"
                    icon={<Undo2 className="h-3.5 w-3.5" aria-hidden="true" />}
                    label="Restaurar texto por defecto"
                    loading={restoring === key}
                    onClick={() => { void requestRestore(key); }}
                    className="mt-1.5"
                  />
                )}
              </div>
            );
          })}
        </div>
      )}

      <StepActions>
        <Button
          type="button"
          severity="secondary"
          icon={<ArrowLeft className="h-4 w-4" aria-hidden="true" />}
          label="Atrás"
          onClick={() => { void handleBack(); }}
          disabled={state === "saving"}
          className="w-full sm:w-auto min-h-11"
        />
        <Button
          type="button"
          label="Continuar"
          icon={<ArrowRight className="h-4 w-4" aria-hidden="true" />}
          iconPos="right"
          disabled={blocked}
          onClick={() => { void handleContinue(); }}
          className="w-full sm:flex-1 min-h-11"
        />
      </StepActions>
    </div>
  );
}

/* ── Estado del autoguardado (aria-live). ── */
function SaveIndicator({ state, onRetry }: { state: SaveState; onRetry: () => void }) {
  return (
    <div className="flex min-h-7 items-center gap-2 text-xs" role="status" aria-live="polite">
      {state === "saving" && (
        <span className="flex items-center gap-1.5 text-fx-text-2">
          <Loader2 className="h-3.5 w-3.5 animate-spin text-fx-text-3" aria-hidden="true" /> Guardando…
        </span>
      )}
      {state === "saved" && (
        <span className="flex items-center gap-1.5 text-fx-text-2">
          <CheckCircle2 className="h-3.5 w-3.5 text-fx-success" aria-hidden="true" /> Guardado hace un momento
        </span>
      )}
      {state === "error" && (
        <span className="flex items-center gap-1.5 font-medium text-fx-danger">
          <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" /> No se pudo guardar ·
          <Button type="button" link size="small" label="Reintentar" onClick={onRetry} pt={{ root: { className: "p-0" } }} />
        </span>
      )}
    </div>
  );
}
