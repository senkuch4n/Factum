"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { agent, agentFileURL, type RecordingValidation } from "@/lib/agent";
import { isMostlyBlack } from "@/lib/black-image";
import { fileType } from "@/components/capture/gallery";
import type { CapturedFile } from "@/types";

/**
 * Avisos no bloqueantes de la evidencia del caso (grabacion-android-robusta §7.4):
 * grabaciones incompletas (de la validación de Tatana) y capturas negras
 * (detectadas en el navegador). Solo visuales: no van al backend ni al informe.
 */
export interface EvidenceNotices {
  /** Validación por filename (solo `ok`/`incomplete`). Se persiste en `sessionStorage` (DA8). */
  recordings: Record<string, RecordingValidation>;
  /** Resultado de `isMostlyBlack` por filename. En memoria: se recalcula al recargar. */
  black: Record<string, boolean>;
  /** Capturas negras que el perito decidió conservar ("Conservar"). */
  keptBlack: string[];
}

export interface EvidenceReviewSummary {
  incompleteRecordings: string[];
  blackScreenshots: string[];
}

interface Persisted {
  recordings: Record<string, RecordingValidation>;
  keptBlack: string[];
}

const EMPTY: Persisted = { recordings: {}, keptBlack: [] };
const STORAGE_PREFIX = "factum:evidence-notices:";
/** Capturas que se analizan a la vez. */
const MAX_PARALLEL = 2;

function readPersisted(caseId: string | null): Persisted {
  if (!caseId || typeof window === "undefined") return EMPTY;
  try {
    const raw = window.sessionStorage.getItem(STORAGE_PREFIX + caseId);
    if (!raw) return EMPTY;
    const p = JSON.parse(raw) as Partial<Persisted> | null;
    return {
      recordings: p?.recordings && typeof p.recordings === "object" ? p.recordings : {},
      keptBlack: Array.isArray(p?.keptBlack) ? p.keptBlack.filter((n): n is string => typeof n === "string") : [],
    };
  } catch {
    return EMPTY;
  }
}

function writePersisted(caseId: string, p: Persisted) {
  try {
    const key = STORAGE_PREFIX + caseId;
    if (Object.keys(p.recordings).length === 0 && p.keptBlack.length === 0) window.sessionStorage.removeItem(key);
    else window.sessionStorage.setItem(key, JSON.stringify(p));
  } catch { /* sessionStorage lleno o deshabilitado: el aviso queda solo en memoria */ }
}

function withoutKey<T>(rec: Record<string, T>, key: string): Record<string, T> {
  if (!(key in rec)) return rec;
  const next = { ...rec };
  delete next[key];
  return next;
}

export interface EvidenceNoticesOptions {
  /**
   * Flujo agent (pulido-post-qa-final §8.5): la captura recién agregada está en
   * la raíz de Tatana y el guardado la *mueve* a la carpeta del caso. El chequeo
   * espera a que esté en el caso (`storedInCase`) para no pedir a la raíz un
   * archivo que ya no está (404).
   */
  agentStorage?: boolean;
}

export function useEvidenceNotices(caseId: string | null, files: CapturedFile[], opts?: EvidenceNoticesOptions) {
  const agentStorage = opts?.agentStorage ?? false;
  const [persisted, setPersisted] = useState<Persisted>(() => readPersisted(caseId));
  const [black, setBlack] = useState<Record<string, boolean>>({});

  // Cambio de caso: se recarga lo persistido y se olvidan las capturas negras
  // (ajuste de estado durante el render, sin un render intermedio con datos viejos).
  const [loadedFor, setLoadedFor] = useState(caseId);
  if (loadedFor !== caseId) {
    setLoadedFor(caseId);
    setPersisted(readPersisted(caseId));
    setBlack({});
  }

  useEffect(() => {
    if (caseId && loadedFor === caseId) writePersisted(caseId, persisted);
  }, [caseId, loadedFor, persisted]);

  // ── Detección de capturas negras: una vez por nombre, de a 2, cancelable por caso ──
  const ctrlRef = useRef<AbortController | null>(null);
  const seenRef = useRef<Set<string>>(new Set());
  // Solo el nombre: la URL se resuelve al correr el chequeo, no al encolar.
  const queueRef = useRef<string[]>([]);
  const activeRef = useRef(0);
  const filesRef = useRef(files);
  useEffect(() => { filesRef.current = files; }, [files]);

  useEffect(() => {
    const ctrl = new AbortController();
    ctrlRef.current = ctrl;
    seenRef.current = new Set();
    queueRef.current = [];
    activeRef.current = 0;
    return () => ctrl.abort();
  }, [caseId]);

  useEffect(() => {
    const ctrl = ctrlRef.current;
    if (!caseId || !ctrl || ctrl.signal.aborted) return;

    const pump = () => {
      while (activeRef.current < MAX_PARALLEL && queueRef.current.length > 0) {
        const name = queueRef.current.shift()!;
        // Se quitó de la bandeja mientras esperaba: nada que chequear.
        const f = filesRef.current.find(x => x.name === name);
        if (!f) continue;
        const url = f.storedInCase ? agent.caseFileURL(caseId, name) : agentFileURL(name);
        activeRef.current++;
        void isMostlyBlack(url, ctrl.signal)
          .then(r => {
            if (!ctrl.signal.aborted && r !== null) setBlack(b => ({ ...b, [name]: r }));
          })
          .finally(() => {
            if (ctrl.signal.aborted) return;
            activeRef.current--;
            pump();
          });
      }
    };

    for (const f of files) {
      if (fileType(f.name) !== "screenshot" || f.availability === "missing" || seenRef.current.has(f.name)) continue;
      // Todavía en la raíz (el guardado la está moviendo): sin marcarla como vista,
      // se encola cuando pase a `storedInCase` (cambia `files` y el efecto vuelve a correr).
      if (agentStorage && !f.storedInCase) continue;
      seenRef.current.add(f.name);
      queueRef.current.push(f.name);
    }
    pump();
  }, [caseId, files, agentStorage]);

  const setRecordingValidation = useCallback((filename: string, v: RecordingValidation) => {
    if (v.status !== "ok" && v.status !== "incomplete") return;
    setPersisted(p => ({ ...p, recordings: { ...p.recordings, [filename]: v } }));
  }, []);

  const keepBlack = useCallback((filename: string) => {
    setPersisted(p => (p.keptBlack.includes(filename) ? p : { ...p, keptBlack: [...p.keptBlack, filename] }));
  }, []);

  /** Al quitar un archivo de la bandeja: se olvidan sus avisos. */
  const forget = useCallback((filename: string) => {
    setPersisted(p => {
      if (!(filename in p.recordings) && !p.keptBlack.includes(filename)) return p;
      return { recordings: withoutKey(p.recordings, filename), keptBlack: p.keptBlack.filter(n => n !== filename) };
    });
    setBlack(b => withoutKey(b, filename));
    seenRef.current.delete(filename);
  }, []);

  const notices = useMemo<EvidenceNotices>(
    () => ({ recordings: persisted.recordings, black, keptBlack: persisted.keptBlack }),
    [persisted, black],
  );

  /** Para el paso 5: solo archivos que siguen en la bandeja; sin las capturas "Conservar" (DA9). */
  const summary = useMemo<EvidenceReviewSummary>(() => {
    const names = new Set(files.map(f => f.name));
    return {
      incompleteRecordings: Object.entries(persisted.recordings)
        .filter(([n, v]) => v.status === "incomplete" && names.has(n))
        .map(([n]) => n),
      blackScreenshots: Object.entries(black)
        .filter(([n, isBlack]) => isBlack && names.has(n) && !persisted.keptBlack.includes(n))
        .map(([n]) => n),
    };
  }, [files, persisted, black]);

  return { notices, setRecordingValidation, keepBlack, forget, summary };
}
