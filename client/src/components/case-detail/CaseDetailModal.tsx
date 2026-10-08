"use client";

import {
  useCallback, useEffect, useId, useLayoutEffect, useRef, useState, useSyncExternalStore,
} from "react";
import { createPortal } from "react-dom";
import {
  animate, motion, useMotionTemplate, useMotionValue, useReducedMotion, useTransform,
  type AnimationPlaybackControls, type MotionValue,
} from "framer-motion";
import { X, FolderOpen, Smartphone, Calendar, Clock } from "lucide-react";
import type { Case } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate, formatTime } from "@/lib/format";
import { CLOSE_BUTTON } from "@/lib/prime/pt/shared";
import { StatusBadge } from "@/components/StatusBadge";
import { CaseDetailContent, CaseDetailFooter, hasFooterDownloads } from "./CaseDetailContent";
import { EvidenceHostChip } from "./EvidenceHostChip";
import { useCaseAgentFlow } from "./useCaseAgentFlow";
import { lockScroll, trapTab } from "./dialog-a11y";
import {
  CONTENT_DELAY, CONTENT_SPRING, FADE, OVERLAY_FADE, POWER2_OUT, POWER3_IN, SHELL_SPRING,
  clearTrigger, dissolveTrigger, layoutRect, naturalRect, restoreTrigger,
} from "./morph";

/* < sm: hoja inferior. Store externo para que el server render sea estable. */
const SHEET_QUERY = "(max-width: 639.98px)";
function subscribeSheet(cb: () => void) {
  const mq = window.matchMedia(SHEET_QUERY);
  mq.addEventListener("change", cb);
  return () => mq.removeEventListener("change", cb);
}
const useIsSheet = () =>
  useSyncExternalStore(subscribeSheet, () => window.matchMedia(SHEET_QUERY).matches, () => false);

const noopSubscribe = () => () => {};
const useIsClient = () => useSyncExternalStore(noopSubscribe, () => true, () => false);

const RADIUS = 16;
const MIN_SCALE = 0.01;

interface Props {
  /** Caso abierto; `null` = cerrado. Uno solo a la vez. */
  cas: Case | null;
  /** Cambia en cada apertura: reabrir (aunque sea el mismo caso a mitad del cierre) remonta limpio. */
  openKey: number;
  /** Elemento visual de donde sale y a donde vuelve la superficie (fila, `<tr>`, tarjeta). */
  origin: HTMLElement | null;
  /** Disparador enfocable: recibe el foco al cerrar. */
  returnFocusTo: HTMLElement | null;
  onClose: () => void;
  onResume: (c: Case) => void;
}

/**
 * Detalle de una inspección, compartido por las vistas lista, tabla y
 * cuadrícula del historial. Shell propio (no el `Dialog` de Prime) por la
 * coreografía: FLIP explícito desde el rect del disparador, desenfoque del
 * contenido solo durante las transiciones y cierre en dos fases. Se cierra
 * con la X, Escape o clic en la máscara (oscurecimiento plano, sin blur).
 * Cubre lo mismo que Prime: `role="dialog"`, `aria-modal`, foco
 * inicial adentro, foco atrapado, Escape, clic en la máscara, retorno del
 * foco y scroll bloqueado. Con `prefers-reduced-motion`, fade simple.
 */
export function CaseDetailModal({ cas, openKey, ...rest }: Props) {
  const isClient = useIsClient();
  if (!isClient || !cas) return null;
  return createPortal(<DetailDialog key={openKey} cas={cas} openKey={openKey} {...rest} />, document.body);
}

type Phase = "opening" | "open" | "closing";
function DetailDialog({ cas, origin, returnFocusTo, onClose, onResume }: Props & { cas: Case }) {
  const reduce = useReducedMotion() ?? false;
  const isSheet = useIsSheet();
  const titleId = useId();
  const shellRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const backdropRef = useRef<HTMLDivElement>(null);
  const bodyRef = useRef<HTMLDivElement>(null);
  const { draftHost } = useCaseAgentFlow(cas);
  const isDone = cas.status === "completed";
  const hasFooter = hasFooterDownloads(cas);

  const [closing, setClosing] = useState(false);
  const phase = useRef<Phase>("opening");
  const gen = useRef(0);
  const running = useRef<AnimationPlaybackControls[]>([]);
  const focusReturned = useRef(false);
  const [initial] = useState(() => ({ origin, returnFocusTo }));

  // ── Valores animados ───────────────────────────────────────────────
  // Superficie: FLIP con origen arriba-izquierda; el radio se corrige por eje
  // para que se vea siempre de 16px aunque la superficie esté escalada.
  const x = useMotionValue(0);
  const y = useMotionValue(0);
  const sx = useMotionValue(1);
  const sy = useMotionValue(1);
  const shellOpacity = useMotionValue(0);
  const invSx = useTransform(sx, v => 1 / Math.max(v, MIN_SCALE));
  const invSy = useTransform(sy, v => 1 / Math.max(v, MIN_SCALE));
  const rx = useTransform(sx, v => RADIUS / Math.max(v, MIN_SCALE));
  const ry = useTransform(sy, v => RADIUS / Math.max(v, MIN_SCALE));
  const radiusAll = useMotionTemplate`${rx}px / ${ry}px`;
  const radiusTop = useMotionTemplate`${rx}px ${rx}px 0px 0px / ${ry}px ${ry}px 0px 0px`;
  // Contenido: entra desenfocado, sale con un "desenfoque recortado".
  const cOpacity = useMotionValue(reduce ? 1 : 0);
  const cY = useMotionValue(reduce ? 0 : 24);
  const cScale = useMotionValue(reduce ? 1 : 0.92);
  const cBlur = useMotionValue(reduce ? 0 : 12);
  const cFilter = useTransform(cBlur, v => (v > 0.01 ? `blur(${v}px)` : "none"));
  // Máscara: solo opacidad (oscurece la página con `--fx-overlay`).
  const bOpacity = useMotionValue(0);

  /** `will-change` solo mientras hay animación (blur y transform son caros en superficies grandes). */
  const setWillChange = useCallback((on: boolean) => {
    if (shellRef.current) shellRef.current.style.willChange = on ? "transform, opacity" : "";
    if (contentRef.current) contentRef.current.style.willChange = on ? "transform, opacity, filter" : "";
    if (backdropRef.current) backdropRef.current.style.willChange = on ? "opacity" : "";
  }, []);

  const stopAll = useCallback(() => {
    for (const a of running.current) a.stop();
    running.current = [];
  }, []);

  /** Corre un grupo de animaciones; `done` solo si nadie las interrumpió. */
  const run = useCallback((anims: AnimationPlaybackControls[], done: () => void) => {
    const g = ++gen.current;
    running.current = anims;
    Promise.all(anims).then(() => { if (g === gen.current) done(); });
  }, []);

  const returnFocus = useCallback(() => {
    if (focusReturned.current) return;
    focusReturned.current = true;
    const el = initial.returnFocusTo;
    if (el?.isConnected) el.focus({ preventScroll: true });
  }, [initial]);

  // ── Apertura ───────────────────────────────────────────────────────
  // Layout effect: se mide y se posiciona antes de pintar; el cleanup (también
  // de layout) corre antes que el montaje de un detalle nuevo, así reabrir
  // rápido no deja el disparador ni el scroll en un estado intermedio.
  useLayoutEffect(() => {
    const shell = shellRef.current!;
    const from = initial.origin;
    const unlock = lockScroll(from ?? initial.returnFocusTo);
    bodyRef.current?.focus({ preventScroll: true });
    setWillChange(true);

    if (reduce) {
      run(
        [animate(shellOpacity, 1, FADE), animate(bOpacity, 1, FADE)],
        () => { phase.current = "open"; setWillChange(false); },
      );
    } else {
      const F = layoutRect(shell);
      const T = from?.isConnected ? naturalRect(from) : null;
      if (T && F.width > 0 && F.height > 0) {
        x.set(T.left - F.left);
        y.set(T.top - F.top);
        sx.set(T.width / F.width);
        sy.set(T.height / F.height);
      }
      const anims: AnimationPlaybackControls[] = [];
      if (from) anims.push(dissolveTrigger(from));
      anims.push(
        animate(bOpacity, 1, OVERLAY_FADE),
        animate(shellOpacity, 1, { duration: 0.11, ease: "easeOut" }),
        ...([[x, 0], [y, 0], [sx, 1], [sy, 1]] as [MotionValue<number>, number][])
          .map(([mv, v]) => animate(mv, v, { ...SHELL_SPRING, delay: 0.01 })),
        animate(cOpacity, 1, { duration: 0.2, ease: POWER2_OUT, delay: CONTENT_DELAY }),
        animate(cBlur, 0, { duration: 0.25, ease: POWER2_OUT, delay: CONTENT_DELAY }),
        animate(cY, 0, { ...CONTENT_SPRING, delay: CONTENT_DELAY }),
        animate(cScale, 1, { ...CONTENT_SPRING, delay: CONTENT_DELAY }),
      );
      run(anims, () => { phase.current = "open"; setWillChange(false); });
    }

    return () => {
      gen.current++;
      stopAll();
      clearTrigger(from);
      unlock();
      returnFocus();
    };
    // Solo al montar: cada apertura es un montaje nuevo (`key={openKey}`).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Cierre ─────────────────────────────────────────────────────────
  /**
   * Cierre (X, Escape o máscara). Dos fases sin rebote
   * visible: compresión hacia el disparador (.26s, power3.in) y asentamiento
   * (.14s, power2.out), con keyframes por valor; el contenido se va antes
   * con un desenfoque recortado y el disparador reaparece al final.
   */
  const close = useCallback(() => {
    if (phase.current === "closing") return;
    phase.current = "closing";
    setClosing(true);
    returnFocus();
    stopAll();
    setWillChange(true);

    const from = initial.origin;
    const finish = () => { clearTrigger(from); onClose(); };

    if (reduce) {
      run([animate(shellOpacity, 0, FADE), animate(bOpacity, 0, FADE)], finish);
      return;
    }

    const shell = shellRef.current!;
    const anims: AnimationPlaybackControls[] = [];

    // Contenido: sale primero ("desenfoque recortado").
    const t = { duration: 0.16, ease: POWER3_IN };
    anims.push(
      animate(cOpacity, 0, t),
      animate(cY, -4, t),
      animate(cScale, 0.985, t),
      animate(cBlur, 5, t),
    );
    anims.push(animate(bOpacity, 0, { duration: 0.22, ease: POWER2_OUT }));

    const T = from?.isConnected ? naturalRect(from) : null;
    if (T) {
      const F = layoutRect(shell);
      const settleW = Math.max(T.width - 6, T.width * 0.96);
      const settleH = Math.max(T.height - 4, T.height * 0.96);
      const two = (mv: MotionValue<number>, mid: number, end: number) =>
        animate(mv, [mv.get(), mid, end], {
          duration: 0.4, delay: 0.02, times: [0, 0.65, 1], ease: [POWER3_IN, POWER2_OUT],
        });
      anims.push(
        two(x, T.left + (T.width - settleW) / 2 - F.left, T.left - F.left),
        two(y, T.top + 1 - F.top, T.top - F.top),
        two(sx, settleW / F.width, T.width / F.width),
        two(sy, settleH / F.height, T.height / F.height),
        // La superficie le cede el lugar al disparador, que reaparece.
        animate(shellOpacity, 0, { duration: 0.16, ease: "easeOut", delay: 0.26 }),
      );
      if (from) anims.push(restoreTrigger(from, 0.26));
    } else {
      anims.push(animate(shellOpacity, 0, { duration: 0.2, ease: "easeOut" }));
    }
    run(anims, finish);
  }, [initial, onClose, reduce, returnFocus, run, setWillChange, shellOpacity, bOpacity, cOpacity, cY, cScale, cBlur, x, y, sx, sy, stopAll]);

  const closeRef = useRef(close);
  useEffect(() => { closeRef.current = close; }, [close]);

  // Escape y Tab atrapado (no durante el cierre: el foco ya volvió afuera).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (phase.current === "closing") return;
      if (e.key === "Escape" && !e.defaultPrevented) {
        e.preventDefault();
        closeRef.current();
      } else if (e.key === "Tab" && shellRef.current) {
        trapTab(e, shellRef.current);
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, []);

  return (
    <div
      className={cn(
        "fixed inset-0 z-[1050] flex justify-center",
        isSheet ? "items-end pt-6" : "items-center p-6",
        closing && "pointer-events-none",
      )}
    >
      <motion.div
        ref={backdropRef}
        aria-hidden="true"
        className="absolute inset-0 bg-fx-overlay"
        style={{ opacity: bOpacity }}
        onClick={() => close()}
      />

      <motion.div
        ref={shellRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        style={{
          x, y, scaleX: sx, scaleY: sy, originX: 0, originY: 0,
          opacity: shellOpacity,
          borderRadius: isSheet ? radiusTop : radiusAll,
        }}
        className={cn(
          "relative flex w-full flex-col overflow-hidden outline-none",
          "border border-fx-border bg-fx-surface-1 text-fx-text shadow-fx-3",
          isSheet ? "max-h-[92dvh] border-b-0" : "max-w-[42.5rem] max-h-[85dvh]",
        )}
      >
        {/* Contra-escala: el contenido no se deforma mientras la superficie
            escala; queda recortado por ella ("clipped"). */}
        <motion.div
          className="flex min-h-0 flex-1 flex-col"
          style={{ scaleX: invSx, scaleY: invSy, originX: 0, originY: 0 }}
        >
          <motion.div
            ref={contentRef}
            className="flex min-h-0 flex-1 flex-col"
            style={{ opacity: cOpacity, y: cY, scale: cScale, filter: cFilter }}
          >
            <div className="flex shrink-0 items-start gap-3 border-b border-fx-border px-5 py-4 sm:px-6">
              <span
                className={cn(
                  "mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-fx-md border",
                  isDone
                    ? "bg-fx-success-soft border-fx-success text-fx-success"
                    : "bg-fx-surface-2 border-fx-border text-fx-text-3",
                )}
                aria-hidden="true"
              >
                <FolderOpen className="h-5 w-5" strokeWidth={1.5} />
              </span>

              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <h2 id={titleId} className="text-fx-h3 text-fx-text break-words">
                    <span className="sr-only">Causa </span>{cas.nro_referencia}
                  </h2>
                  <StatusBadge status={cas.status} />
                  {draftHost && <EvidenceHostChip hostname={draftHost} />}
                </div>
                {(cas.caratula || cas.nombre_denunciante) && (
                  <p className="mt-0.5 text-fx-body-sm text-fx-text-2 break-words">
                    {cas.caratula || cas.nombre_denunciante}
                    {cas.dni_denunciante && ` · DNI ${cas.dni_denunciante}`}
                  </p>
                )}
                <p className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-fx-text-3">
                  <span className="flex items-center gap-1">
                    <Smartphone className="h-3 w-3" aria-hidden="true" /> {cas.device.manufacturer} {cas.device.model}
                  </span>
                  <span className="flex items-center gap-1">
                    <Calendar className="h-3 w-3" aria-hidden="true" /> {formatDate(cas.created_at)}
                  </span>
                  <span className="flex items-center gap-1">
                    <Clock className="h-3 w-3" aria-hidden="true" /> {formatTime(cas.created_at)}
                  </span>
                </p>
              </div>

              <button
                type="button"
                onClick={() => close()}
                aria-label="Cerrar detalle"
                className={cn(CLOSE_BUTTON, "-mr-2 -mt-1 h-10 w-10")}
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>

            {/* Cuerpo con scroll propio: recibe el foco inicial (tabindex=-1)
                para que las flechas/AvPág scrolleen sin tabular primero. Fuera
                del asa: conserva la selección de texto (hashes) y el scroll. */}
            <div
              ref={bodyRef}
              tabIndex={-1}
              className={cn(
                "min-h-0 flex-1 overflow-y-auto overscroll-contain px-5 pt-5 outline-none sm:px-6",
                hasFooter ? "pb-6" : "pb-[max(1.5rem,env(safe-area-inset-bottom))]",
              )}
            >
              <CaseDetailContent
                cas={cas}
                onResume={(c) => { onClose(); onResume(c); }}
              />
            </div>

            {/* Pie fijo con las descargas del servidor (ZIP / Informe Word),
                siempre a la vista sin scrollear. Sin ninguna de las dos, no hay pie.
                Encabezado y pie son `div` y no header/footer: en un portal
                directo en body serían landmarks banner/contentinfo duplicados. */}
            {hasFooter && (
              <div className="shrink-0 border-t border-fx-border bg-fx-surface-1 px-5 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] sm:px-6">
                <CaseDetailFooter cas={cas} />
              </div>
            )}
          </motion.div>
        </motion.div>
      </motion.div>
    </div>
  );
}
