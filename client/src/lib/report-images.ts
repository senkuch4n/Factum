/**
 * Capturas del caso dentro de los textos del informe (editor-imagenes-informe,
 * SDD §7.4). Sin Tiptap: lo usan `ReportStep`, el editor y `GenerateStep`.
 *
 * - `useReportImages(caseId)`: el listado de `GET …/report-images`.
 * - `ReportImagePreviewCache`: vistas previas como blob URLs, una sola por
 *   captura y por visita al paso 4, compartidas por las 8 secciones y el
 *   selector. `dispose()` aborta lo pendiente y revoca todo.
 */

import { useCallback, useEffect, useState, useSyncExternalStore } from "react";
import { ApiError, api, type ReportImage } from "@/lib/api";

/* ── Listado ─────────────────────────────────────────────────────────── */

export type ReportImagesStatus = "loading" | "ready" | "error";

export interface ReportImagesState {
  /** `null` mientras carga o si falló. */
  images: ReportImage[] | null;
  status: ReportImagesStatus;
  reload: () => void;
}

/** Listado de capturas insertables del caso (orden del servidor, sin reordenar). */
export function useReportImages(caseId: string | null | undefined): ReportImagesState {
  const [result, setResult] = useState<{ key: string; images: ReportImage[] | null; status: ReportImagesStatus } | null>(null);
  const [nonce, setNonce] = useState(0);
  const key = `${caseId ?? ""}#${nonce}`;

  useEffect(() => {
    if (!caseId) return;
    let alive = true;
    api.listReportImages(caseId)
      .then(r => { if (alive) setResult({ key, images: Array.isArray(r.images) ? r.images : [], status: "ready" }); })
      .catch(() => { if (alive) setResult({ key, images: null, status: "error" }); });
    return () => { alive = false; };
  }, [caseId, key]);

  const reload = useCallback(() => setNonce(n => n + 1), []);
  // Un resultado de otro caso (o de antes de "Reintentar") no se muestra: se ve "cargando".
  const current = result && result.key === key ? result : null;
  return { images: current?.images ?? null, status: current?.status ?? "loading", reload };
}

/* ── Vistas previas ──────────────────────────────────────────────────── */

export type PreviewStatus = "idle" | "loading" | "ready" | "unavailable" | "error";

export interface PreviewEntry {
  status: PreviewStatus;
  /** blob URL, solo con `ready`. */
  url?: string;
}

const IDLE: PreviewEntry = Object.freeze({ status: "idle" }) as PreviewEntry;

export class ReportImagePreviewCache {
  private readonly caseId: string;
  private entries = new Map<string, PreviewEntry>();
  private controllers = new Map<string, AbortController>();
  private listeners = new Set<() => void>();

  constructor(caseId: string) {
    this.caseId = caseId;
  }

  /** Para `useSyncExternalStore`. */
  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  };

  /** Estado de una captura. Devuelve el mismo objeto mientras no cambie. */
  get = (filename: string): PreviewEntry => this.entries.get(filename) ?? IDLE;

  /** Pide la vista previa si todavía no se pidió (deduplica). */
  load = (filename: string): void => {
    if (this.entries.has(filename)) return;
    const controller = new AbortController();
    this.controllers.set(filename, controller);
    this.set(filename, { status: "loading" });
    api.getReportImagePreview(this.caseId, filename, controller.signal)
      .then(blob => {
        if (controller.signal.aborted) return;
        this.set(filename, { status: "ready", url: URL.createObjectURL(blob) });
      })
      .catch(err => {
        if (controller.signal.aborted) return;
        // 400/404: no es una captura disponible. Red o 5xx: se puede reintentar.
        const unavailable = err instanceof ApiError && (err.status === 400 || err.status === 404);
        this.set(filename, { status: unavailable ? "unavailable" : "error" });
      })
      .finally(() => {
        if (this.controllers.get(filename) === controller) this.controllers.delete(filename);
      });
  };

  /** Vuelve a pedir una vista previa que falló. */
  retry = (filename: string): void => {
    const e = this.entries.get(filename);
    if (e && e.status !== "error" && e.status !== "unavailable") return;
    this.entries.delete(filename);
    this.load(filename);
  };

  /**
   * Aborta lo pendiente, revoca todos los blob URLs y vacía la caché. La
   * instancia sigue usable (el doble montaje de StrictMode la "dispone" y la
   * vuelve a usar): lo que siga en pantalla pasa a `idle` y se vuelve a pedir.
   */
  dispose = (): void => {
    this.controllers.forEach(c => c.abort());
    this.controllers.clear();
    this.entries.forEach(e => { if (e.url) URL.revokeObjectURL(e.url); });
    this.entries.clear();
    this.emit();
  };

  private set(filename: string, entry: PreviewEntry) {
    const prev = this.entries.get(filename);
    if (prev?.url && prev.url !== entry.url) URL.revokeObjectURL(prev.url);
    this.entries.set(filename, entry);
    this.emit();
  }

  private emit() {
    this.listeners.forEach(l => l());
  }
}

/** Estado de la vista previa de una captura (se re-renderiza solo cuando cambia esa). */
export function usePreviewEntry(cache: ReportImagePreviewCache | null | undefined, filename: string): PreviewEntry {
  return useSyncExternalStore(
    cache ? cache.subscribe : noopSubscribe,
    () => (cache ? cache.get(filename) : IDLE),
    () => IDLE,
  );
}

const noopSubscribe = () => () => {};

/**
 * `true` cuando el elemento entra (o está a 200 px de entrar) al viewport:
 * la carga es perezosa (SDD §7.4). Una vez visible, queda visible.
 */
export function useNearViewport<T extends Element>(): [(el: T | null) => void, boolean] {
  const [el, setEl] = useState<T | null>(null);
  const [visible, setVisible] = useState(false);
  useEffect(() => {
    if (!el || visible) return;
    if (typeof IntersectionObserver === "undefined") { setVisible(true); return; }
    const io = new IntersectionObserver(
      entries => { if (entries.some(e => e.isIntersecting)) setVisible(true); },
      { rootMargin: "200px" },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [el, visible]);
  return [setEl, visible];
}
