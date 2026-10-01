"use client";

import { useState, useRef, useCallback } from "react";
import { agent } from "@/lib/agent";
import { api } from "@/lib/api";
import { isRoleEligible } from "@/lib/pericial";
import type { Case, CapturedFile, CaptureRole, CaptureRoleValue, VideoVariant } from "@/types";

export function useFileManager() {
  const [files, setFiles]                   = useState<CapturedFile[]>([]);
  const [loading, setLoading]               = useState<Record<string, boolean>>({});
  const [videoVariants, setVideoVariants]   = useState<Record<string, VideoVariant[]>>({});
  const [pendingVariantFiles, setPending]   = useState<Set<string>>(new Set());
  const pendingBlobs                        = useRef<Map<string, Blob>>(new Map());

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
    agent.deleteFile(filename).catch(() => {});
    pendingBlobs.current.delete(filename);
    setFiles(prev => prev.filter(f => f.name !== filename));
  }

  function handlePhotoBlob(blob: Blob, filename: string) {
    pendingBlobs.current.set(filename, blob);
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

  async function handleUploadAndContinue(
    currentCase: Case,
    onSuccess: () => void,
    onError: (msg: string) => void,
    onStatus: (msg: string) => void,
    onRolesSaved?: (roles: CaptureRole[]) => void,
  ) {
    setLoad("upload", true);
    onStatus("Enviando archivos al servidor...");
    try {
      for (const f of files) {
        if (f.uploaded) continue;
        const blob = pendingBlobs.current.has(f.name)
          ? pendingBlobs.current.get(f.name)!
          : await agent.downloadFile(f.name);
        await api.uploadFile(currentCase.id, f.name, blob, f.sourcePath);
        setFiles(prev => prev.map(fi => fi.name === f.name ? { ...fi, uploaded: true } : fi));
      }
      // Después de subir todo: el rol de cada captura local (o null), en un solo PUT.
      const roles = files
        .filter(f => isRoleEligible(f.name))
        .map(f => ({ filename: f.name, role: f.captureRole ?? null }));
      if (roles.length > 0) {
        onStatus("Guardando marcas de las capturas...");
        const res = await api.saveCaptureRoles(currentCase.id, roles);
        onRolesSaved?.(res.capture_roles);
      }
      onSuccess();
    } catch (e) {
      onError(e instanceof Error ? e.message : "Error al enviar archivos");
    } finally {
      setLoad("upload", false);
      onStatus("");
    }
  }

  function clearFiles() {
    pendingBlobs.current.clear();
    setFiles([]);
    setVideoVariants({});
    setPending(new Set());
  }

  return {
    files, loading, videoVariants, pendingVariantFiles: pendingVariantFiles, pendingBlobs,
    setLoad, addFile, addVideoVariant, markPendingVariant,
    removeFile, setCaptureRole, handlePhotoBlob, handleUploadAndContinue, clearFiles,
  };
}
