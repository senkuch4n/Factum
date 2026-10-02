"use client";

import { useState, useRef, useCallback, useEffect } from "react";
import { agent } from "@/lib/agent";
import { api, UploadError } from "@/lib/api";
import { isRoleEligible } from "@/lib/pericial";
import { uploadErrorMessage, type UploadMessage } from "@/lib/upload-messages";
import type {
  Case, CapturedFile, CaptureRole, CaptureRoleValue, VideoVariant, FileUploadState, UploadProgress,
} from "@/types";

/** Intervalo mínimo entre actualizaciones de la barra (los `progress` del XHR llegan cada pocos ms). */
const PROGRESS_THROTTLE_MS = 120;

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
  }, []);

  const setLoad = useCallback((key: string, val: boolean) => {
    setLoading(l => ({ ...l, [key]: val }));
  }, []);

  const addFile = useCallback((name: string, sourcePath?: string) => {
    setFiles(prev => prev.some(f => f.name === name) ? prev : [...prev, { name, uploaded: false, sourcePath }]);
  }, []);

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
      setPending(prev => { const n = new Set(prev); n.delete(d.original_filename); return n; });
    }
  }, []);

  const markPendingVariant = useCallback((filename: string) => {
    setPending(prev => new Set([...prev, filename]));
  }, []);

  function removeFile(filename: string) {
    // Defensa: durante el envío la UI ya deshabilita quitar.
    if (loading.upload) return;
    agent.deleteFile(filename).catch(() => {});
    pendingBlobs.current.delete(filename);
    setFiles(prev => prev.filter(f => f.name !== filename));
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
   */
  async function handleUploadAndContinue(
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

  function clearFiles() {
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
    removeFile, setCaptureRole, handlePhotoBlob, handleUploadAndContinue, clearFiles,
    uploadProgress, uploadStates, uploadNotice, dismissUploadNotice, cancelUpload,
  };
}
