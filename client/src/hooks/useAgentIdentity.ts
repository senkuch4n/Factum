"use client";

import { useCallback, useEffect, useSyncExternalStore } from "react";
import { agent, localNetworkPermission, type AgentInfo } from "@/lib/agent";
import type { Case } from "@/types";

/**
 * Identidad de la PC (Tatana) para el flujo de evidencia local
 * (zip-local-informe-servidor, SDD §7.6). Caché a nivel de módulo: todas las
 * tarjetas del historial y los pasos comparten un solo `health` + `info`.
 */
export type AgentIdentityStatus = "loading" | "online" | "offline" | "outdated";

export interface AgentIdentity {
  status: AgentIdentityStatus;
  info: AgentInfo | null;
}

let snapshot: AgentIdentity = { status: "loading", info: null };
let inflight: Promise<AgentIdentity> | null = null;
let loadedOnce = false;
const listeners = new Set<() => void>();

const SERVER_SNAPSHOT: AgentIdentity = { status: "loading", info: null };

function publish(next: AgentIdentity) {
  snapshot = next;
  listeners.forEach(l => l());
}

async function fetchIdentity(): Promise<AgentIdentity> {
  try {
    const health = await agent.health();
    const info = await agent.getInfo();
    const supports = health.capabilities?.includes("case_evidence_v1") ?? false;
    return { status: supports ? "online" : "outdated", info };
  } catch {
    // Para que `agentErrorMessage` distinga "permiso de red local denegado" (§9.3).
    await localNetworkPermission().catch(() => "unknown");
    return { status: "offline", info: null };
  }
}

/**
 * Pide (o reusa) la identidad. `force` revalida aunque ya haya un resultado.
 * Fuera de React: lo usan `useFileManager` y el paso de generación.
 */
export function ensureAgentIdentity(force = false): Promise<AgentIdentity> {
  if (inflight) return inflight;
  if (loadedOnce && !force && snapshot.status !== "offline") return Promise.resolve(snapshot);
  inflight = fetchIdentity()
    .then(next => { loadedOnce = true; publish(next); return next; })
    .finally(() => { inflight = null; });
  return inflight;
}

/** Hostname de la PC del caso: el de la evidencia o, si no hay, el del ZIP. */
function caseHostname(cas: Pick<Case, "evidence_host" | "zip_location">): string | null {
  return cas.evidence_host?.hostname || cas.zip_location?.hostname || null;
}

/**
 * `true` si este Tatana es la PC del caso. Sin PC registrada (manifiesto
 * vacío), cualquier PC con Tatana sirve. Sin Tatana nunca es "la misma"
 * (D-T10: no se infiere "otra PC" sin Tatana; la UI dice "no está corriendo").
 */
export function isSameHostFor(identity: AgentIdentity, cas: Pick<Case, "evidence_host" | "zip_location">): boolean {
  if (identity.status !== "online" || !identity.info) return false;
  const host = caseHostname(cas);
  if (!host) return true;
  return identity.info.hostname.toLowerCase() === host.toLowerCase();
}

export function useAgentIdentity() {
  const state = useSyncExternalStore(
    useCallback((l: () => void) => { listeners.add(l); return () => { listeners.delete(l); }; }, []),
    () => snapshot,
    () => SERVER_SNAPSHOT,
  );

  // Una vez por montaje (deduplicado por la caché); si Tatana estaba caído, se reintenta.
  useEffect(() => { void ensureAgentIdentity(); }, []);

  const refresh = useCallback(() => ensureAgentIdentity(true), []);
  const isSameHost = useCallback(
    (cas: Pick<Case, "evidence_host" | "zip_location">) => isSameHostFor(state, cas),
    [state],
  );

  return { status: state.status, info: state.info, refresh, isSameHost };
}
