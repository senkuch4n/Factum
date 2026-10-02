"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { Editor } from "@tiptap/core";
import dynamic from "next/dynamic";
import { Button } from "primereact/button";
import {
  AlertCircle, ArrowLeft, ArrowRight, CheckCircle2, ClipboardList, FileText, Loader2, Undo2,
} from "lucide-react";
import { api } from "@/lib/api";
import { EMPTY_REPORT_TEXTS, MAX_LEN_TEXT, REPORT_SECTION_LABELS, reportFieldId } from "@/lib/pericial";
import { ReportImagePreviewCache, useReportImages } from "@/lib/report-images";
import { REPORT_TEXT_FORMAT, plainToMarkdown } from "@/lib/report-markdown";
import type { ReportTexts, ReportTextsInput } from "@/types";
import { FormField } from "./FormField";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";
import { FxBanner } from "@/components/feedback/FxBanner";
import { StepHeader } from "@/components/wizard/StepHeader";
import { StepActions } from "@/components/wizard/StepActions";
import { ReportImagesProvider, type ImagePickerRequest, type ReportImagesContextValue } from "@/components/editor/ReportImagesContext";
import { CapturePickerDialog } from "@/components/editor/CapturePickerDialog";

type TextKey = keyof ReportTextsInput;

const SECTION_KEYS = Object.keys(EMPTY_REPORT_TEXTS) as TextKey[];

/** Solo las ocho secciones: `formato`, `updated_at` y lo que venga de más no entran al estado. */
function pickSections(src: Partial<Record<TextKey, string | null | undefined>>, map: (v: string) => string = v => v): ReportTextsInput {
  const t = { ...EMPTY_REPORT_TEXTS };
  SECTION_KEYS.forEach(k => { t[k] = map(src[k] ?? ""); });
  return t;
}

/** Secciones que no entran en el tope (p. ej. un texto viejo que creció al escaparse, D16). */
const overLimitKeys = (t: ReportTextsInput) => SECTION_KEYS.filter(k => t[k].length > MAX_LEN_TEXT);

const OVER_LIMIT_MESSAGE =
  `Este texto supera el máximo de ${new Intl.NumberFormat("es-AR").format(MAX_LEN_TEXT)} caracteres al guardarse con formato; acortalo para poder guardar.`;

/**
 * Editor de texto enriquecido (Tiptap): solo en el cliente y en un chunk aparte,
 * así no entra al bundle del resto del dashboard (D17).
 */
const RichTextEditor = dynamic(() => import("@/components/editor/RichTextEditor"), {
  ssr: false,
  loading: () => (
    <div
      aria-hidden="true"
      className="h-[8.5rem] rounded-fx-md border border-fx-border-strong bg-fx-surface-2 motion-safe:animate-pulse"
    />
  ),
});

const SECTIONS: { key: TextKey; label: string; required: boolean; hasDefault: boolean; placeholder?: string }[] = [
  { key: "objeto_informe",          label: REPORT_SECTION_LABELS.objeto_informe,          required: false, hasDefault: false },
  { key: "operaciones_realizadas",  label: REPORT_SECTION_LABELS.operaciones_realizadas,  required: true,  hasDefault: true },
  { key: "aseguramiento_evidencia", label: REPORT_SECTION_LABELS.aseguramiento_evidencia, required: true,  hasDefault: true },
  { key: "resultados",              label: REPORT_SECTION_LABELS.resultados,              required: true,  hasDefault: false, placeholder: "Qué se encontró en el dispositivo…" },
  { key: "valoracion_tecnica",      label: REPORT_SECTION_LABELS.valoracion_tecnica,      required: true,  hasDefault: false, placeholder: "Análisis técnico de lo encontrado…" },
  { key: "conclusiones",            label: REPORT_SECTION_LABELS.conclusiones,            required: true,  hasDefault: false },
  { key: "notas_tecnicas",          label: REPORT_SECTION_LABELS.notas_tecnicas,          required: false, hasDefault: true },
  { key: "reserva",                 label: REPORT_SECTION_LABELS.reserva,                 required: false, hasDefault: true },
];

/** Selector abierto: en qué editor y, si se edita, sobre qué imagen. */
interface PickerState {
  editor: Editor;
  pos?: number;
  filename?: string;
  alt?: string;
}

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

  /* ── Capturas del caso para insertar en los textos (editor-imagenes-informe, SDD §7.7) ── */
  const reportImages = useReportImages(caseId);
  // Una caché de vistas previas por visita al paso, compartida por las 8 secciones y el selector.
  const previewCache = useMemo(() => new ReportImagePreviewCache(caseId), [caseId]);
  useEffect(() => () => previewCache.dispose(), [previewCache]);
  const [picker, setPicker] = useState<PickerState | null>(null);

  const openPicker = useCallback(({ editor, pos }: ImagePickerRequest) => {
    if (typeof pos === "number") {
      const node = editor.state.doc.nodeAt(pos);
      if (node?.type.name !== "reportImage") return;
      setPicker({ editor, pos, filename: String(node.attrs.filename), alt: String(node.attrs.alt ?? "") });
    } else {
      setPicker({ editor });
    }
  }, []);

  const imagesContext = useMemo<ReportImagesContextValue>(() => ({
    caseId,
    images: reportImages.images,
    imagesStatus: reportImages.status,
    reloadImages: reportImages.reload,
    cache: previewCache,
    openPicker,
  }), [caseId, reportImages.images, reportImages.status, reportImages.reload, previewCache, openPicker]);

  function closePicker(value?: { filename: string; alt: string }) {
    const p = picker;
    setPicker(null);
    if (!p) return;
    if (!value) { p.editor.commands.focus(); return; }
    if (typeof p.pos === "number") p.editor.chain().focus().updateReportImageAlt(p.pos, value.alt).run();
    else p.editor.chain().focus().insertReportImage(value).run();
  }

  /* ── Guardado ── */
  const save = useCallback(async (): Promise<boolean> => {
    if (timerRef.current) { window.clearTimeout(timerRef.current); timerRef.current = null; }
    // Un texto por encima del tope no se manda: el servidor lo rechazaría (D16).
    if (overLimitKeys(textsRef.current).length) {
      dirtyRef.current = true;
      setState("error");
      return false;
    }
    const seq = ++seqRef.current;
    dirtyRef.current = false;
    setState("saving");
    try {
      const res = await api.saveReportTexts(caseId, { ...textsRef.current, formato: REPORT_TEXT_FORMAT });
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
        // Caso con formato: tal cual. Caso viejo (texto plano): se convierte a Markdown
        // escapando lo especial, SIN marcarlo como editado; se guarda solo si el perito edita (D2 A).
        const markdown = cas.report_texts.formato === REPORT_TEXT_FORMAT;
        const t = pickSections(cas.report_texts, markdown ? undefined : plainToMarkdown);
        textsRef.current = t;
        setTexts(t);
        setState("idle");
      } else {
        const defaults = await api.getReportTextDefaults(caseId);
        defaultsRef.current = pickSections(defaults);
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
    if (dirtyRef.current && !overLimitKeys(textsRef.current).length) {
      api.saveReportTexts(caseId, { ...textsRef.current, formato: REPORT_TEXT_FORMAT })
        .then(r => onSavedRef.current(r))
        .catch(() => {});
    }
  }, [caseId]);

  // Foco pedido desde el checklist, una vez cargado. El editor se carga aparte
  // (next/dynamic), así que se espera a que su elemento editable exista.
  useEffect(() => {
    if (!focusFieldId || state === "loading") return;
    let tries = 0;
    let timer: number | null = null;
    const attempt = () => {
      const el = document.getElementById(focusFieldId);
      if (el) {
        el.focus();
        el.scrollIntoView({ block: "center", behavior: "smooth" });
      } else if (++tries < 60) {
        timer = window.setTimeout(attempt, 50);
        return;
      }
      onFocusConsumed?.();
    };
    attempt();
    return () => { if (timer) window.clearTimeout(timer); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusFieldId, state === "loading"]);

  /* ── Restaurar texto por defecto ── */
  async function getDefaults(): Promise<ReportTextsInput> {
    if (!defaultsRef.current) {
      const d = await api.getReportTextDefaults(caseId);
      defaultsRef.current = pickSections(d);
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
        <ReportImagesProvider value={imagesContext}>
        <div className="space-y-5 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
          {SECTIONS.map(({ key, label, required, hasDefault, placeholder }) => {
            const id = reportFieldId(key);
            return (
              <div key={key}>
                <FormField
                  id={id}
                  labelId={`${id}-label`}
                  icon={key === "operaciones_realizadas" ? ClipboardList : FileText}
                  label={label}
                  sublabel={required ? undefined : "· opcional"}
                  required={required}
                >
                  <RichTextEditor
                    id={id}
                    labelId={`${id}-label`}
                    label={label}
                    required={required}
                    placeholder={placeholder}
                    maxLength={MAX_LEN_TEXT}
                    value={texts[key]}
                    error={texts[key].length > MAX_LEN_TEXT ? OVER_LIMIT_MESSAGE : undefined}
                    onChange={md => update({ ...textsRef.current, [key]: md })}
                    onBlur={() => { void flush(); }}
                    onRequestImage={openPicker}
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
        </ReportImagesProvider>
      )}

      <CapturePickerDialog
        open={picker !== null}
        mode={typeof picker?.pos === "number" ? "edit" : "insert"}
        initialFilename={picker?.filename}
        initialAlt={picker?.alt}
        images={reportImages.images}
        imagesStatus={reportImages.status}
        onReload={reportImages.reload}
        cache={previewCache}
        onConfirm={v => closePicker(v)}
        onCancel={() => closePicker()}
      />

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
