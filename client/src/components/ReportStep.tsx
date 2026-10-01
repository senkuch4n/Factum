"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { motion } from "framer-motion";
import {
  AlertCircle, ArrowRight, Check, ClipboardList, FileText, Loader2, RotateCcw, Undo2,
} from "lucide-react";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { EMPTY_REPORT_TEXTS, MAX_LEN_TEXT, reportFieldId } from "@/lib/pericial";
import type { ReportTexts, ReportTextsInput } from "@/types";
import { FormField } from "./FormField";
import { ConfirmDialog } from "@/components/ui/alert-dialog";

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

/** Ajusta el alto del textarea a su contenido. */
function autosize(el: HTMLTextAreaElement | null) {
  if (!el) return;
  el.style.height = "auto";
  el.style.height = `${el.scrollHeight + 2}px`;
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
  const areaRefs = useRef<Partial<Record<TextKey, HTMLTextAreaElement | null>>>({});
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

  // Alto de los textarea según contenido.
  useEffect(() => {
    (Object.keys(areaRefs.current) as TextKey[]).forEach(k => autosize(areaRefs.current[k] ?? null));
  }, [texts, state]);

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

      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="step-title">Redactá el informe</h2>
          <p className="mt-2 text-sm" style={{ color: "var(--text-secondary)" }}>
            Cada sección va al informe pericial tal como la escribas. Los cambios se guardan solos.
          </p>
        </div>
        <SaveIndicator state={state} onRetry={() => { void save(); }} />
      </div>

      {state === "load-error" ? (
        <div
          role="alert"
          className="flex items-start gap-3 rounded-md px-4 py-3 text-sm border border-red-500/20 bg-red-500/[0.07] text-red-600 dark:text-red-400"
        >
          <AlertCircle className="mt-0.5 h-4 w-4 flex-shrink-0" aria-hidden="true" />
          <span className="flex-1">{loadError}</span>
          <button type="button" className="btn-secondary btn-sm" onClick={() => { void load(); }}>
            Reintentar
          </button>
        </div>
      ) : state === "loading" ? (
        <div className="flex items-center justify-center gap-2 py-12 text-sm" style={{ color: "var(--text-muted)" }}>
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> Cargando los textos del informe…
        </div>
      ) : (
        <div className="space-y-5">
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
                  <textarea
                    id={id}
                    name={key}
                    ref={el => { areaRefs.current[key] = el; autosize(el); }}
                    className="input text-sm leading-relaxed"
                    style={{ minHeight: 96, overflow: "hidden" }}
                    aria-required={required || undefined}
                    maxLength={MAX_LEN_TEXT}
                    placeholder={placeholder}
                    value={texts[key]}
                    onChange={e => update({ ...textsRef.current, [key]: e.target.value })}
                    onBlur={() => { void flush(); }}
                  />
                </FormField>
                {hasDefault && (
                  <button
                    type="button"
                    onClick={() => { void requestRestore(key); }}
                    disabled={restoring === key}
                    className="mt-1.5 inline-flex items-center gap-1 rounded text-[11px] font-medium underline-offset-2 hover:underline disabled:opacity-50 fx-focus-ring"
                    style={{ color: "var(--text-muted)" }}
                  >
                    {restoring === key
                      ? <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
                      : <Undo2 className="h-3 w-3" aria-hidden="true" />}
                    Restaurar texto por defecto
                  </button>
                )}
              </div>
            );
          })}
        </div>
      )}

      <div className="flex gap-3">
        <motion.button type="button" className="btn-secondary" onClick={() => { void handleBack(); }} disabled={state === "saving"} whileTap={{ scale: 0.98 }}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" /> Atrás
        </motion.button>
        <motion.button
          type="button"
          className={cn("btn-primary flex flex-1 items-center justify-center gap-2")}
          onClick={() => { void handleContinue(); }}
          disabled={blocked}
          whileTap={{ scale: blocked ? 1 : 0.98 }}
        >
          Continuar <ArrowRight className="h-4 w-4" aria-hidden="true" />
        </motion.button>
      </div>
    </div>
  );
}

/* ── Estado del autoguardado (aria-live). ── */
function SaveIndicator({ state, onRetry }: { state: SaveState; onRetry: () => void }) {
  return (
    <div className="flex min-h-[28px] items-center gap-2 text-xs" role="status" aria-live="polite">
      {state === "saving" && (
        <span className="flex items-center gap-1.5" style={{ color: "var(--text-muted)" }}>
          <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> Guardando…
        </span>
      )}
      {state === "saved" && (
        <span className="flex items-center gap-1.5" style={{ color: "var(--text-muted)" }}>
          <Check className="h-3.5 w-3.5" style={{ color: "#10b981" }} aria-hidden="true" /> Guardado hace un momento
        </span>
      )}
      {state === "error" && (
        <span className="flex items-center gap-1.5 text-red-600 dark:text-red-400">
          <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" /> No se pudo guardar ·
          <button type="button" onClick={onRetry} className="rounded font-semibold underline underline-offset-2 fx-focus-ring">
            Reintentar
          </button>
        </span>
      )}
    </div>
  );
}
