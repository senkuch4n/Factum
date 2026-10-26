"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { agent } from "@/lib/agent";
import { api } from "@/lib/api";
import { probeServerClock } from "@/lib/device-details";
import { isTerminal } from "@/lib/extraction";

/**
 * Registro diferido de extracciones en el backend (extraccion-logica-android
 * §10.2, D11). Al montar cualquier página de `/extracciones` (y al reconectar
 * Tatana, vía `refresh`): por cada entrada terminal del índice local con
 * `registered = false`, reenvía el `record` al backend, copia los sellos si ya
 * están `granted` y la marca registrada. Por cada entrada registrada pero sin
 * sellos, copia los sellos si el DTO los trae `granted`.
 *
 * Es best-effort: los fallos no rompen nada, quedan como `pending` para el
 * próximo montaje o reconexión. El registro **inicial** (`running`) lo hace el
 * wizard justo después del `202`.
 */
export function useExtractionRegistration(enabled = true) {
  const [pending, setPending] = useState(0);
  const [running, setRunning] = useState(false);
  const inFlight = useRef(false);

  const reconcile = useCallback(async () => {
    if (!enabled || inFlight.current) return;
    inFlight.current = true;
    setRunning(true);
    let stillPending = 0;
    try {
      const items = await agent.listExtractions();
      for (const item of items) {
        try {
          if (isTerminal(item.status) && !item.registered) {
            await registerTerminal(item.id);
          } else if (item.registered && !item.seals_written) {
            await copySealsIfGranted(item.id);
          }
        } catch {
          // Best-effort: se reintenta en el próximo montaje/reconexión.
          if (isTerminal(item.status) && !item.registered) stillPending += 1;
        }
      }
    } catch {
      // Tatana caído o sin índice: nada pendiente que podamos contar ahora.
    } finally {
      setPending(stillPending);
      setRunning(false);
      inFlight.current = false;
    }
  }, [enabled]);

  useEffect(() => { void reconcile(); }, [reconcile]);

  const refresh = useCallback(() => { void reconcile(); }, [reconcile]);
  return { pending, running, refresh };
}

/** Reenvía el `record` al backend, copia los sellos si corresponde y marca registrada. */
async function registerTerminal(id: string): Promise<void> {
  const snap = await agent.getExtraction(id);
  const record = snap.record;
  if (!record || !record.manifest_sha256) return; // terminal sin cerrar del todo: se reintenta
  const probe = await probeServerClock();
  const dto = await api.upsertExtraction(id, { ...record, ...(probe ? { pc_server_probe: probe } : {}) });
  if (dto.manifest_timestamp?.status === "granted") {
    await copySeals(id);
  }
  await agent.markExtractionRegistered(id, record.manifest_sha256);
}

/** Copia los sellos si el DTO del backend ya los tiene `granted` (registrada, sin sellos). */
async function copySealsIfGranted(id: string): Promise<void> {
  const dto = await api.getExtraction(id);
  if (dto.manifest_timestamp?.status === "granted") await copySeals(id);
}

/** Baja los `.tsr` + LEEME del backend y se los pasa a Tatana para `sellos/`. */
async function copySeals(id: string): Promise<void> {
  const files = await api.getExtractionTimestampFiles(id);
  if (files.files.length === 0) return;
  await agent.writeExtractionSeals(id, { files: files.files, readme: files.readme });
}
