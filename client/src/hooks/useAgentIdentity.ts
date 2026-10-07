"use client";

import { useCallback, useEffect, useSyncExternalStore } from "react";
import { agent, localNetworkPermission, type AgentInfo } from "@/lib/agent";
import { hasRealVersion, meetsMinVersion } from "@/lib/agent-version";
import { loadPublicConfig } from "@/hooks/usePublicConfig";
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
  /**
   * Por qué está `outdated` (tatana-instalador-autoupdate, D-T20):
   * - `capability`: no tiene `case_evidence_v1` (no puede guardar evidencia en esta PC);
   * - `min_version`: está debajo de `tatana_min_version` del backend (D6). Bloquea
   *   capturar y generar el ZIP, pero la evidencia ya capturada se sigue viendo.
   */
  outdatedReason?: "capability" | "min_version";
  /** Versión mínima configurada en el backend (solo con `min_version`). */
  minVersion?: string;
  /** `true` si `info.version` es la real (capability `real_version_v1`, D-T2). */
  versionIsReal?: boolean;
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
    const [health, publicConfig] = await Promise.all([agent.health(), loadPublicConfig()]);
    const info = await agent.getInfo();
    const versionIsReal = hasRealVersion(health);
    const supports = health.capabilities?.includes("case_evidence_v1") ?? false;
    if (!supports) return { status: "outdated", info, outdatedReason: "capability", versionIsReal };
    const min = publicConfig.tatanaMinVersion;
    if (min && !meetsMinVersion(health, min)) {
      return { status: "outdated", info, outdatedReason: "min_version", minVersion: min, versionIsReal };
    }
    return { status: "online", info, versionIsReal };
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
  // Caído o desactualizado se revalida siempre: el perito pudo abrir o actualizar Tatana.
  if (loadedOnce && !force && snapshot.status !== "offline" && snapshot.status !== "outdated") {
    return Promise.resolve(snapshot);
  }
  inflight = fetchIdentity()
    .then(next => { loadedOnce = true; publish(next); return next; })
    .finally(() => { inflight = null; });
  return inflight;
}

/**
 * `true` si este Tatana puede servir la evidencia del caso: `online`, o
 * `outdated` solo por la versión mínima (tiene `case_evidence_v1`).
 */
export function canReadEvidence(identity: Pick<AgentIdentity, "status" | "outdatedReason">): boolean {
  return identity.status === "online" || (identity.status === "outdated" && identity.outdatedReason === "min_version");
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
  // Debajo de la versión mínima (D6) Tatana sigue sirviendo la evidencia ya
  // capturada: solo se bloquea capturar/generar, no verla.
  if (!canReadEvidence(identity) || !identity.info) return false;
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

  return {
    status: state.status,
    info: state.info,
    outdatedReason: state.outdatedReason,
    minVersion: state.minVersion,
    versionIsReal: state.versionIsReal,
    refresh,
    isSameHost,
  };
}
