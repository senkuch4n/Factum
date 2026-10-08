"use client";

import type { Case } from "@/lib/api";
import { useAgentIdentity } from "@/hooks/useAgentIdentity";

/**
 * Flujo agent (zip-local-informe-servidor §7.7): el ZIP no está en el servidor
 * sino en la PC donde corrió Tatana. Lo comparten las filas del historial y el
 * detalle de la inspección.
 *
 * - `sameHost`: esta PC es la que tiene la evidencia.
 * - `draftHost`: hostname a mostrar en el chip "Evidencia en …" de un borrador
 *   cuya evidencia quedó en otra PC (solo si Tatana responde, para no afirmar
 *   algo que no se pudo comparar).
 */
export function useCaseAgentFlow(cas: Case) {
  const identity = useAgentIdentity();
  const agentFlow = cas.evidence_storage === "agent";
  const sameHost = agentFlow && identity.isSameHost(cas);
  const draftHost =
    agentFlow && cas.status === "draft" && cas.evidence_host?.hostname && identity.status === "online" && !sameHost
      ? cas.evidence_host.hostname
      : null;
  return { agentFlow, sameHost, draftHost };
}
