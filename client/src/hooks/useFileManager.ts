"use client";

import { useState, useRef, useCallback, useEffect } from "react";
import { agent, AgentError, type AgentInfo, type EvidenceFileInfo } from "@/lib/agent";
import { api, ApiError, UploadError, type EvidenceResponse, type EvidenceStorage } from "@/lib/api";
import { agentErrorMessage, agentStatusMessage } from "@/lib/agent-messages";
import { isRoleEligible } from "@/lib/pericial";
import { uploadErrorMessage, type UploadMessage } from "@/lib/upload-messages";
import { ensureAgentIdentity, isSameHostFor } from "@/hooks/useAgentIdentity";
import type {
  Case, CapturedFile, CaptureRole, CaptureRoleValue, VideoVariant, FileUploadState, UploadProgress,
} from "@/types";

/** Intervalo mínimo entre actualizaciones de la barra (los `progress` del XHR llegan cada pocos ms). */
const PROGRESS_THROTTLE_MS = 120;

/** `file_busy` (Windows, ffmpeg generando variantes): reintento a los 5 s, hasta 3 veces (SDD §7.4). */
const BUSY_RETRY_MS = 5000;
const BUSY_MAX_RETRIES = 3;

/** Caso al que está atado el guardado automático (flujo agent). */
export interface BoundCase {
  id: string;
  storage: EvidenceStorage;
  /** Callbacks del dueño de `currentCase`: errores y manifiesto actualizado. */
  onError?: (msg: string) => void;
  onEvidence?: (res: EvidenceResponse) => void;
  /** Marcas guardadas por el guardado automático (`PUT capture-roles`). */
  onRolesSaved?: (roles: CaptureRole[]) => void;
}

/** `evidence_storage` efectivo: sin el campo (backend anterior) es el flujo de hoy. */
export const storageOf = (cas: Pick<Case, "evidence_storage"> | null | undefined): EvidenceStorage | null =>
  cas ? (cas.evidence_storage === "agent" ? "agent" : "server") : null;

const devWarn = (...args: unknown[]) => {
  if (process.env.NODE_ENV !== "production") console.warn(...args);
};

/**
 * Datos de vista de un archivo generado en el navegador (cámara externa, webcam
 * o adjunto de la PC). Indexado por el nombre con el que se sube; `files` sigue
 * siendo la única lista de lo que se sube.
 */
export interface LocalBlobInfo {
  url: string;           // URL.createObjectURL(blob), creada en el handler (nunca en render)
  size: number;          // blob.size, en bytes
  type: string;          // blob.type (MIME, puede venir vacío)
  originalName?: string; // nombre original del File de la PC; undefined para cámara/webcam
}

export function useFileManager() {
  const [files, setFilesState]              = useState<CapturedFile[]>([]);
  // Espejo de `files`: el bucle de envío lo relee para tomar lo que llega por
  // WebSocket durante el envío y los roles finales (DT12).
  const filesRef                            = useRef<CapturedFile[]>([]);
  const [loading, setLoading]               = useState<Record<string, boolean>>({});
  const [videoVariants, setVideoVariants]   = useState<Record<string, VideoVariant[]>>({});
  const [pendingVariantFiles, setPending]   = useState<Set<string>>(new Set());
  const pendingBlobs                        = useRef<Map<string, Blob>>(new Map());
  const [localBlobs, setLocalBlobsState]    = useState<Record<string, LocalBlobInfo>>({});
  // Espejo de `localBlobs` para revocar las URLs al desmontar (el cleanup no ve el estado).
  const localBlobsRef                       = useRef<Record<string, LocalBlobInfo>>({});
  // Envío en curso (subida-archivos-grandes, SDD §6.6).
  const [uploadProgress, setUploadProgress] = useState<UploadProgress | null>(null);
  const [uploadStates, setUploadStates]     = useState<Record<string, FileUploadState>>({});
  const [uploadNotice, setUploadNotice]     = useState<UploadMessage | null>(null);
  const uploadAbortRef                      = useRef<AbortController | null>(null);
  const lastProgressAtRef                   = useRef(0);

  // ── Flujo agent (zip-local-informe-servidor, SDD §7.4) ──
  const [storageMode, setStorageMode]       = useState<EvidenceStorage | null>(null);
  const boundRef                            = useRef<BoundCase | null>(null);
  // Cola secuencial del guardado automático (DP1 A): nombres por guardar, de a uno.
  const saveQueueRef                        = useRef<string[]>([]);
  const drainRef                            = useRef<Promise<void> | null>(null);
  const [saveBusy, setSaveBusy]             = useState(false);
  const saveAbortRef                        = useRef<AbortController | null>(null);
  // En "Guardar evidencia en esta PC y continuar" se muestra el panel para todo;
  // en el guardado automático, solo para las copias de Blobs del navegador.
  const explicitSaveRef                     = useRef(false);
  const busyRetriesRef                      = useRef<Map<string, number>>(new Map());
  const busyTimersRef                       = useRef<Set<ReturnType<typeof setTimeout>>>(new Set());
  const lastSaveErrorRef                    = useRef<string | null>(null);
  // PC del caso: `info` de Tatana + carpeta de trabajo, una vez por caso.
  const hostRef                             = useRef<{ caseId: string; info: AgentInfo; caseDirectory: string } | null>(null);
  const [agentHost, setAgentHost]           = useState<AgentInfo | null>(null);
  // Espejo de `pendingVariantFiles` para la cola (iOS esperando la variante MCI).
  const pendingVariantRef                   = useRef<Set<string>>(new Set());

  // Mismo patrón que `setLocalBlobs`: se calcula sobre el ref (fuera del updater,
  // así StrictMode no lo duplica) y se publica el resultado.
  const setFiles = useCallback((update: (prev: CapturedFile[]) => CapturedFile[]) => {
    const next = update(filesRef.current);
    if (next === filesRef.current) return;
    filesRef.current = next;
    setFilesState(next);
  }, []);

  // Los efectos (revocar) quedan fuera del updater: se calcula sobre el ref y se
  // publica el resultado, así StrictMode no los duplica.
  const setLocalBlobs = useCallback((update: (prev: Record<string, LocalBlobInfo>) => Record<string, LocalBlobInfo>) => {
    const next = update(localBlobsRef.current);
    localBlobsRef.current = next;
    setLocalBlobsState(next);
  }, []);

  // Al salir de /dashboard se liberan todas las blob URLs que queden vivas.
  useEffect(() => () => {
    Object.values(localBlobsRef.current).forEach(b => URL.revokeObjectURL(b.url));
    localBlobsRef.current = {};
    busyTimersRef.current.forEach(t => clearTimeout(t));
    busyTimersRef.current.clear();
    saveAbortRef.current?.abort();
  }, []);

  const setLoad = useCallback((key: string, val: boolean) => {
    setLoading(l => ({ ...l, [key]: val }));
  }, []);

  // La cola se llama desde callbacks estables (WS, `addFile`): siempre por ref, sin closures viejos.
  const enqueueSaveRef = useRef<(name: string) => void>(() => {});

  const addFile = useCallback((name: string, sourcePath?: string) => {
    let added = false;
    setFiles(prev => {
      if (prev.some(f => f.name === name)) return prev;
      added = true;
      return [...prev, { name, uploaded: false, sourcePath }];
    });
    // DP1 A: en el flujo agent, cada captura se guarda en la PC apenas existe.
    if (added && boundRef.current?.storage === "agent") enqueueSaveRef.current(name);
  }, [setFiles]);

  const addVideoVariant = useCallback((d: {
    original_filename: string;
    label: string;
    filename: string;
    url: string;
  }) => {
    setVideoVariants(prev => {
      const cur = prev[d.original_filename] ?? [];
      if (cur.some(v => v.filename === d.filename)) return prev;
      return { ...prev, [d.original_filename]: [...cur, { label: d.label, filename: d.filename, url: d.url }] };
    });
    if (d.label === "MCI") {
      pendingVariantRef.current.delete(d.original_filename);
      setPending(prev => { const n = new Set(prev); n.delete(d.original_filename); return n; });
      // La grabación iOS quedó lista: ahora sí se puede mover a la carpeta del caso.
      const f = filesRef.current.find(x => x.name === d.original_filename);
      if (f && !f.uploaded && boundRef.current?.storage === "agent") enqueueSaveRef.current(f.name);
    }
  }, []);

  const markPendingVariant = useCallback((filename: string) => {
    pendingVariantRef.current.add(filename);
    setPending(prev => new Set([...prev, filename]));
  }, []);

  /** Saca un archivo de la bandeja y del estado local (blob URL incluida). */
  function dropLocal(filename: string) {
    pendingBlobs.current.delete(filename);
    saveQueueRef.current = saveQueueRef.current.filter(n => n !== filename);
    setFiles(prev => prev.filter(f => f.name !== filename));
    setUploadStates(s => {
      if (!(filename in s)) return s;
      const rest = { ...s };
      delete rest[filename];
      return rest;
    });
    const gone = localBlobsRef.current[filename];
    if (gone) {
      URL.revokeObjectURL(gone.url);
      setLocalBlobs(prev => {
        const rest = { ...prev };
        delete rest[filename];
        return rest;
      });
    }
  }

  /**
   * Quita un archivo. Guardado en la carpeta del caso (flujo agent): primero sale
   * del manifiesto (`DELETE …/evidence`) y después se borra de la PC; si falla lo
   * primero, no se toca nada. Bloqueado mientras haya un guardado en curso.
   */
  async function removeFile(
    filename: string,
    opts: { caseId?: string; onError?: (msg: string) => void; onEvidence?: (res: EvidenceResponse) => void } = {},
  ) {
    // Defensa: durante el envío/guardado la UI ya deshabilita quitar.
    if (loading.upload || saveBusy) return;
    const file = filesRef.current.find(f => f.name === filename);
    if (file?.storedInCase && opts.caseId) {
      const caseId = opts.caseId;
      if (file.uploaded) {
        try {
          const res = await api.deleteEvidence(caseId, filename);
          opts.onEvidence?.(res);
        } catch (e) {
          // 404: ya no estaba en el manifiesto; se sigue con el borrado local.
          if (!(e instanceof ApiError && e.status === 404)) {
            opts.onError?.(e instanceof Error ? e.message : "No se pudo quitar el archivo del expediente");
            return;
          }
        }
      }
      try {
        await agent.deleteCaseFile(caseId, filename);
      } catch (e) {
        // Queda huérfano en la carpeta: fuera del manifiesto y del ZIP.
        devWarn("No se pudo borrar de la carpeta del caso", filename, e);
      }
      dropLocal(filename);
      return;
    }
    agent.deleteFile(filename).catch(() => {});
    dropLocal(filename);
  }

  function handlePhotoBlob(blob: Blob, filename: string) {
    pendingBlobs.current.set(filename, blob);
    const url = URL.createObjectURL(blob);
    const originalName = blob instanceof File && blob.name !== filename ? blob.name : undefined;
    const old = localBlobsRef.current[filename];
    if (old) URL.revokeObjectURL(old.url); // defensivo: con nombres únicos no debería pasar
    setLocalBlobs(prev => ({ ...prev, [filename]: { url, size: blob.size, type: blob.type, originalName } }));
    addFile(filename);
  }

  /**
   * Marca (o desmarca con `null`) una captura. Si el archivo ya está subido, la
   * marca se persiste al instante (`PUT capture-roles`); si no, queda local y se
   * manda después de subir todo (`handleUploadAndContinue`).
   */
  async function setCaptureRole(
    name: string,
    role: CaptureRoleValue | null,
    opts: { caseId?: string; onSaved?: (roles: CaptureRole[]) => void; onError?: (msg: string) => void } = {},
  ) {
    const file = files.find(f => f.name === name);
    setFiles(prev => prev.map(f => f.name === name ? { ...f, captureRole: role ?? undefined } : f));
    if (!file?.uploaded || !opts.caseId) return;
    try {
      const res = await api.saveCaptureRoles(opts.caseId, [{ filename: name, role }]);
      opts.onSaved?.(res.capture_roles);
    } catch (e) {
      opts.onError?.(e instanceof Error ? e.message : "No se pudo guardar la marca de la captura");
    }
  }

  /** Publica el progreso; `force` para cambios de archivo o fase (los bytes van con throttle). */
  const emitProgress = useCallback((p: UploadProgress, force = false) => {
    const now = performance.now();
    if (!force && now - lastProgressAtRef.current < PROGRESS_THROTTLE_MS) return;
    lastProgressAtRef.current = now;
    setUploadProgress(p);
  }, []);

  const cancelUpload = useCallback(() => { uploadAbortRef.current?.abort(); }, []);
  const dismissUploadNotice = useCallback(() => setUploadNotice(null), []);

  /**
   * Envía al caso los archivos pendientes, de a uno: prechequeo (`upload-check`)
   * → bajada desde Tatana si hace falta → subida con progreso. Relee
   * `filesRef` en cada vuelta (DT12). El `Blob` bajado de Tatana es local a la
   * vuelta del bucle: no se guarda en estado ni en refs (D10/DT15).
   *
   * Flujo `server` (borradores viejos, DP3 A): sin cambios respecto de antes de
   * zip-local-informe-servidor; solo cambió el nombre.
   */
  async function handleUploadAndContinueLegacy(
    currentCase: Case,
    onSuccess: () => void,
    onError: (msg: string) => void,
    onStatus: (msg: string) => void,
    onRolesSaved?: (roles: CaptureRole[]) => void,
  ) {
    if (uploadAbortRef.current) return; // ya hay un envío en curso
    const ac = new AbortController();
    uploadAbortRef.current = ac;
    const { signal } = ac;
    const caseId = currentCase.id;
    setLoad("upload", true);
    setUploadNotice(null);

    // Tamaños de lo que está en Tatana (para el prechequeo antes de bajar). Si falla, se
    // chequea con el tamaño del Blob ya bajado.
    const agentSizes = new Map<string, number>();
    try {
      (await agent.listFiles()).forEach(f => agentSizes.set(f.name, f.size));
    } catch { /* sin tamaños: prechequeo después de bajar */ }

    const attempted = new Set<string>();
    const pendingNow = () => filesRef.current.filter(f => !f.uploaded && !attempted.has(f.name));
    let current: { name: string; size: number | null } | null = null;

    try {
      for (let next = pendingNow()[0]; next; next = pendingNow()[0]) {
        const f = next;
        const name = f.name;
        attempted.add(name);
        const index = attempted.size;
        const total = attempted.size + pendingNow().length;
        current = { name, size: null };
        setUploadStates(s => ({ ...s, [name]: "uploading" }));

        const localBlob = pendingBlobs.current.get(name);
        let size: number | null = localBlob?.size ?? agentSizes.get(name) ?? null;
        current.size = size;

        // 1. Prechequeo antes de bajar nada de Tatana.
        emitProgress({ index, total, name, phase: "checking", loaded: 0, totalBytes: size }, true);
        if (size != null) await api.checkUpload(caseId, name, size, signal);

        // 2. Blob: el File/Blob local (respaldado en disco, sin copia) o la bajada desde Tatana.
        let blob: Blob;
        if (localBlob) {
          blob = localBlob;
        } else {
          const knownSize = size;
          emitProgress({ index, total, name, phase: "preparing", loaded: 0, totalBytes: knownSize }, true);
          blob = await agent.downloadFile(name, {
            signal,
            onProgress: (loaded, t) =>
              emitProgress({ index, total, name, phase: "preparing", loaded, totalBytes: t ?? knownSize }),
          });
          if (size == null) {
            size = blob.size;
            current.size = size;
            emitProgress({ index, total, name, phase: "checking", loaded: 0, totalBytes: size }, true);
            await api.checkUpload(caseId, name, size, signal);
          }
        }

        // 3. Subida. Con todo enviado y sin respuesta todavía: "Verificando en el servidor…".
        const blobSize = blob.size;
        emitProgress({ index, total, name, phase: "uploading", loaded: 0, totalBytes: blobSize }, true);
        await api.uploadFile(caseId, name, blob, {
          sourcePath: f.sourcePath,
          signal,
          onProgress: (loaded, t) => {
            const finished = t > 0 && loaded >= t;
            emitProgress(
              { index, total, name, phase: finished ? "finishing" : "uploading", loaded, totalBytes: t },
              finished,
            );
          },
        });

        setFiles(prev => prev.map(fi => fi.name === name ? { ...fi, uploaded: true } : fi));
        setUploadStates(s => {
          const rest = { ...s };
          delete rest[name];
          return rest;
        });
        current = null;
      }

      setUploadProgress(null);
      // Después de subir todo: el rol de cada captura local (o null), en un solo PUT.
      const roles = filesRef.current
        .filter(f => isRoleEligible(f.name))
        .map(f => ({ filename: f.name, role: f.captureRole ?? null }));
      if (roles.length > 0) {
        onStatus("Guardando marcas de las capturas...");
        const res = await api.saveCaptureRoles(caseId, roles);
        onRolesSaved?.(res.capture_roles);
      }
      onSuccess();
    } catch (e) {
      if (current) {
        // Cancelar no es un error: el archivo vuelve a pendiente.
        const failed = current;
        const aborted = signal.aborted;
        setUploadStates(s => {
          const next = { ...s };
          if (aborted) delete next[failed.name];
          else next[failed.name] = "error";
          return next;
        });
        // Un abort durante la bajada de Tatana llega como DOMException: se unifica.
        const m = uploadErrorMessage(aborted ? new UploadError("aborted") : e, failed.name, failed.size);
        if (m.tone === "info") setUploadNotice(m);
        else onError(m.text);
      } else {
        // Fuera de un archivo (ej. guardar las marcas).
        onError(e instanceof Error ? e.message : "Error al enviar archivos");
      }
    } finally {
      setLoad("upload", false);
      setUploadProgress(null);
      uploadAbortRef.current = null;
      onStatus("");
    }
  }

  /* ══ Flujo agent: guardado en la carpeta del caso de Tatana (SDD §7.4) ══ */

  const clearUploadState = (name: string) => setUploadStates(s => {
    if (!(name in s)) return s;
    const rest = { ...s };
    delete rest[name];
    return rest;
  });

  /** `host` del registro: `info` de Tatana (una vez por caso) + carpeta de trabajo. */
  async function ensureHost(caseId: string) {
    if (hostRef.current?.caseId === caseId && hostRef.current.caseDirectory) return hostRef.current;
    const info = await agent.getInfo().catch(() => { throw new AgentError("unreachable"); });
    const listing = await agent.listCaseFiles(caseId);
    hostRef.current = { caseId, info, caseDirectory: listing.directory };
    setAgentHost(info);
    return hostRef.current;
  }

  /**
   * Guarda un archivo en la carpeta del caso y lo registra en el expediente.
   * `false` si se salteó (sigue pendiente). Lanza `AgentError`/`ApiError`.
   */
  async function saveOne(caseId: string, name: string, signal: AbortSignal): Promise<boolean> {
    const file = filesRef.current.find(f => f.name === name);
    if (!file || file.uploaded) return false;
    // 1. iOS esperando la variante MCI: el original todavía se usa en la raíz.
    if (pendingVariantRef.current.has(name)) return false;

    setUploadStates(s => ({ ...s, [name]: "uploading" }));
    const blob = pendingBlobs.current.get(name);
    const showPanel = explicitSaveRef.current || !!blob;
    const progress = (p: Omit<UploadProgress, "index" | "total" | "name">, force = false) => {
      if (!showPanel) return;
      const pendingCount = saveQueueRef.current.length;
      emitProgress({ index: 1, total: 1 + pendingCount, name, ...p }, force);
    };

    // 2. Copia (Blob del navegador) o movimiento (archivo de Tatana) + SHA-256.
    let info: EvidenceFileInfo;
    if (blob) {
      progress({ phase: "checking", loaded: 0, totalBytes: blob.size }, true);
      await agent.checkEvidenceUpload(caseId, name, blob.size, signal);
      progress({ phase: "copying", loaded: 0, totalBytes: blob.size }, true);
      info = await agent.uploadEvidence(caseId, name, blob, {
        signal,
        onProgress: (loaded, total) => {
          const finished = total > 0 && loaded >= total;
          progress({ phase: finished ? "hashing" : "copying", loaded, totalBytes: total }, finished);
        },
      });
    } else {
      progress({ phase: "hashing", loaded: 0, totalBytes: null }, true);
      info = await agent.importEvidence(caseId, name);
    }

    // 3. Registro en el expediente.
    progress({ phase: "registering", loaded: 0, totalBytes: null }, true);
    const host = await ensureHost(caseId);
    const res = await api.registerEvidence(caseId, {
      host: {
        hostname: host.info.hostname,
        os_user: host.info.os_user,
        agent_version: host.info.version,
        case_directory: host.caseDirectory,
      },
      items: [{ filename: info.filename, size: info.size, sha256: info.sha256, source_path: file.sourcePath ?? null }],
    });
    boundRef.current?.onEvidence?.(res);

    // 4. Marcado. Un Blob ya guardado deja de ocupar memoria: la vista sale de Tatana.
    setFiles(prev => prev.map(f => f.name === name
      ? { ...f, uploaded: true, storedInCase: true, sha256: info.sha256, size: info.size, availability: "here" }
      : f));
    if (blob) {
      pendingBlobs.current.delete(name);
      const lb = localBlobsRef.current[name];
      if (lb) {
        URL.revokeObjectURL(lb.url);
        setLocalBlobs(prev => ({ ...prev, [name]: { ...lb, url: agent.caseFileURL(caseId, name) } }));
      }
    }
    clearUploadState(name);
    busyRetriesRef.current.delete(name);

    // 5. Marca de rol local hecha antes de guardar: se persiste ahora.
    const role = filesRef.current.find(f => f.name === name)?.captureRole;
    if (role && isRoleEligible(name)) {
      try {
        const r = await api.saveCaptureRoles(caseId, [{ filename: name, role }]);
        boundRef.current?.onRolesSaved?.(r.capture_roles);
      } catch { /* se reintenta con "Guardar evidencia en esta PC y continuar" */ }
    }
    return true;
  }

  /** Corre la cola de a uno. Un error deja el archivo en `error` y sigue con el resto. */
  async function runQueue() {
    setSaveBusy(true);
    try {
      for (let name = saveQueueRef.current.shift(); name; name = saveQueueRef.current.shift()) {
        const bound = boundRef.current;
        if (!bound || bound.storage !== "agent") { saveQueueRef.current = []; break; }
        const ac = new AbortController();
        saveAbortRef.current = ac;
        try {
          await saveOne(bound.id, name, ac.signal);
        } catch (e) {
          const current = name;
          if (e instanceof AgentError && e.kind === "aborted") {
            clearUploadState(current);
            setUploadNotice({ tone: "info", text: "Guardado cancelado. Lo que ya estaba en esta PC quedó guardado; lo demás sigue en la lista." });
            saveQueueRef.current = [];
            break;
          }
          if (e instanceof AgentError && e.code === "file_busy") {
            const n = (busyRetriesRef.current.get(current) ?? 0) + 1;
            if (n <= BUSY_MAX_RETRIES) {
              busyRetriesRef.current.set(current, n);
              const t = setTimeout(() => {
                busyTimersRef.current.delete(t);
                enqueueSaveRef.current(current);
              }, BUSY_RETRY_MS);
              busyTimersRef.current.add(t);
              continue; // queda "guardando" hasta el reintento
            }
          }
          setUploadStates(s => ({ ...s, [current]: "error" }));
          const msg = e instanceof AgentError
            ? agentErrorMessage(e, { name: current, action: "save" })
            : e instanceof ApiError ? `No se pudo registrar ${current} en el expediente: ${e.message}`
            : `No se pudo guardar ${current} en esta PC. Reintentá.`;
          lastSaveErrorRef.current = msg;
          bound.onError?.(msg);
        }
      }
    } finally {
      saveAbortRef.current = null;
      setUploadProgress(null);
      setSaveBusy(false);
    }
  }

  function enqueueSave(name: string) {
    if (!saveQueueRef.current.includes(name)) saveQueueRef.current.push(name);
    if (!drainRef.current) {
      drainRef.current = runQueue().finally(() => { drainRef.current = null; });
    }
  }
  enqueueSaveRef.current = enqueueSave;

  /** Espera a que la cola quede vacía (incluye lo que se encole mientras tanto). */
  async function drainQueue() {
    while (drainRef.current) await drainRef.current;
  }

  /**
   * Ata el guardado automático al caso abierto (o lo suelta con `null`). La
   * página lo llama cuando cambia `currentCase` (id o flujo).
   */
  const bindCase = useCallback((b: BoundCase | null) => {
    const prevId = boundRef.current?.id;
    boundRef.current = b;
    setStorageMode(b?.storage ?? null);
    if (!b || b.id !== prevId) {
      hostRef.current = null;
      setAgentHost(null);
    }
  }, []);

  /**
   * Botón "Guardar evidencia en esta PC y continuar": espera la cola, reintenta
   * lo que quedó en error mostrando el panel, guarda las marcas y sigue.
   */
  async function handleSaveAndContinue(
    currentCase: Case,
    onSuccess: () => void,
    onError: (msg: string) => void,
    onStatus: (msg: string) => void,
    onRolesSaved?: (roles: CaptureRole[]) => void,
  ) {
    if (loading.upload) return;
    setLoad("upload", true);
    setUploadNotice(null);
    explicitSaveRef.current = true;
    lastSaveErrorRef.current = null;
    try {
      const identity = await ensureAgentIdentity();
      const status = identity.status;
      if (status === "offline" || status === "outdated") { onError(agentStatusMessage(status, identity)); return; }

      await drainQueue();
      // Reintento de todo lo pendiente (errores, ocupados, lo que llegó sin guardar).
      busyTimersRef.current.forEach(t => clearTimeout(t));
      busyTimersRef.current.clear();
      busyRetriesRef.current.clear();
      const pending = filesRef.current.filter(f => !f.uploaded);
      const waitingVariant = pending.find(f => pendingVariantRef.current.has(f.name));
      pending.forEach(f => { if (!pendingVariantRef.current.has(f.name)) enqueueSave(f.name); });
      await drainQueue();

      if (waitingVariant) {
        onError(agentErrorMessage(new AgentError("http", 409, { code: "file_busy" }), { name: waitingVariant.name, action: "save" }));
        return;
      }
      if (filesRef.current.some(f => !f.uploaded)) {
        // Los mensajes de cada archivo ya salieron por `onError` (o fue una cancelación).
        if (lastSaveErrorRef.current) onError(lastSaveErrorRef.current);
        return;
      }

      const roles = filesRef.current
        .filter(f => isRoleEligible(f.name) && f.availability !== "missing")
        .map(f => ({ filename: f.name, role: f.captureRole ?? null }));
      if (roles.length > 0) {
        onStatus("Guardando marcas de las capturas…");
        const res = await api.saveCaptureRoles(currentCase.id, roles);
        onRolesSaved?.(res.capture_roles);
      }
      onSuccess();
    } catch (e) {
      onError(e instanceof ApiError && e.status > 0 ? e.message : "No se pudieron guardar las marcas de las capturas. Revisá la conexión con el servidor y reintentá.");
    } finally {
      explicitSaveRef.current = false;
      setLoad("upload", false);
      setUploadProgress(null);
      onStatus("");
    }
  }

  /**
   * Rearma la bandeja de un borrador `agent` después de una recarga (requisito 1
   * de D8): el manifiesto manda; la carpeta del caso de Tatana dice qué está acá.
   * Lo que se movió pero no se llegó a registrar se registra ahora.
   */
  async function restoreCaseEvidence(cas: Case) {
    const roleOf = new Map((cas.capture_roles ?? []).map(r => [r.filename, r.role] as const));
    const manifest = cas.evidence ?? [];
    const base: CapturedFile[] = manifest.map(it => ({
      name: it.filename,
      uploaded: true,
      storedInCase: true,
      sha256: it.sha256,
      size: it.size,
      sourcePath: it.source_path ?? undefined,
      captureRole: roleOf.get(it.filename),
      availability: "missing",
    }));

    const identity = await ensureAgentIdentity();
    let here: Set<string> | null = null;
    let extras: string[] = [];
    if (isSameHostFor(identity, cas)) {
      try {
        const listing = await agent.listCaseFiles(cas.id);
        here = new Set(listing.files.map(f => f.filename));
        const inManifest = new Set(manifest.map(m => m.filename));
        extras = listing.files.map(f => f.filename).filter(n => !inManifest.has(n));
      } catch { /* Tatana no responde: todo "missing" y el aviso del paso */ }
    }
    // Si el perito cambió de caso mientras se pedía la carpeta, no se pisa la bandeja.
    if (boundRef.current && boundRef.current.id !== cas.id) return;

    const restored: CapturedFile[] = [
      ...base.map(f => ({ ...f, availability: (here?.has(f.name) ? "here" : "missing") as CapturedFile["availability"] })),
      ...extras.map(n => ({ name: n, uploaded: false, storedInCase: true, availability: "here" as const })),
    ];
    setFiles(prev => {
      // Lo capturado mientras se restauraba queda al final.
      const names = new Set(restored.map(f => f.name));
      return [...restored, ...prev.filter(f => !names.has(f.name))];
    });
    if (boundRef.current?.storage === "agent") extras.forEach(n => enqueueSave(n));
  }

  /** Cancela el guardado del archivo actual (flujo agent). */
  const cancelSave = useCallback(() => { saveAbortRef.current?.abort(); }, []);

  function clearFiles() {
    saveQueueRef.current = [];
    saveAbortRef.current?.abort();
    busyTimersRef.current.forEach(t => clearTimeout(t));
    busyTimersRef.current.clear();
    busyRetriesRef.current.clear();
    pendingVariantRef.current.clear();
    pendingBlobs.current.clear();
    Object.values(localBlobsRef.current).forEach(b => URL.revokeObjectURL(b.url));
    setLocalBlobs(() => ({}));
    setFiles(() => []);
    setVideoVariants({});
    setPending(new Set());
    setUploadStates({});
    setUploadNotice(null);
  }

  return {
    files, loading, videoVariants, pendingVariantFiles: pendingVariantFiles, pendingBlobs, localBlobs,
    setLoad, addFile, addVideoVariant, markPendingVariant,
    removeFile, setCaptureRole, handlePhotoBlob, handleUploadAndContinueLegacy, clearFiles,
    uploadProgress, uploadStates, uploadNotice, dismissUploadNotice, cancelUpload,
    // Flujo agent (zip-local-informe-servidor)
    storageMode, agentHost, saveBusy, bindCase, handleSaveAndContinue, restoreCaseEvidence, cancelSave,
  };
}
