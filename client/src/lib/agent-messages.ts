/**
 * Mensajes en castellano para los errores de Tatana en el flujo de evidencia
 * local (zip-local-informe-servidor, SDD §7.2). Puro: no depende de React.
 * Nunca devuelve "Failed to fetch" ni un código HTTP solo.
 */

import { AgentError, cachedLocalNetworkPermission } from "./agent";
import { formatBytes } from "./format";

export type AgentAction = "save" | "preview" | "generate" | "reveal" | "copy";

const fmt = (n: number | null | undefined) => (typeof n === "number" && Number.isFinite(n) ? formatBytes(n) : null);

export const AGENT_OFFLINE_MESSAGE = "Tatana no está corriendo en esta PC: abrilo y reintentá.";
export const AGENT_LNA_DENIED_MESSAGE =
  "El navegador bloqueó el acceso a Tatana en esta PC. Permitilo en el candado de la barra de direcciones (Configuración del sitio → Acceso a la red local o a apps del dispositivo) y reintentá.";
export const AGENT_OUTDATED_MESSAGE =
  "Tu Tatana es de una versión anterior y no puede guardar la evidencia en esta PC. Actualizalo y reintentá.";

/** Texto para Tatana caído (distingue el permiso de red local denegado, §9.3). */
export function agentUnreachableMessage(): string {
  return cachedLocalNetworkPermission() === "denied" ? AGENT_LNA_DENIED_MESSAGE : AGENT_OFFLINE_MESSAGE;
}

/** Texto según el estado de `useAgentIdentity` (`offline` / `outdated`). */
export function agentStatusMessage(status: "offline" | "outdated"): string {
  return status === "outdated" ? AGENT_OUTDATED_MESSAGE : agentUnreachableMessage();
}

export function agentErrorMessage(err: unknown, ctx: { name?: string; action: AgentAction }): string {
  const name = ctx.name ?? "el archivo";
  if (!(err instanceof AgentError)) {
    // Error inesperado (no debería pasar: todas las funciones nuevas lanzan AgentError).
    return ctx.action === "save"
      ? `No se pudo guardar ${name} en esta PC. Reintentá.`
      : "Tatana no pudo completar la operación. Reintentá.";
  }
  if (err.kind === "unreachable") return agentUnreachableMessage();
  if (err.kind === "aborted") return "Se canceló la operación.";

  const b = err.body;
  switch (err.code) {
    case "origin_not_allowed":
      return "Tatana no acepta pedidos desde esta página. Pedí que agreguen esta dirección a la configuración de Tatana (Agent:AllowedOrigins).";
    case "file_busy":
      return `${name} todavía se está procesando en Tatana. Esperá unos segundos y volvé a intentar.`;
    case "insufficient_storage": {
      const required = fmt(b?.required_bytes);
      const available = fmt(b?.available_bytes);
      return required && available
        ? `No hay espacio en el disco de esta PC para guardar ${name} (hace falta ${required}, quedan ${available}). Liberá espacio y reintentá.`
        : `No hay espacio en el disco de esta PC para guardar ${name}. Liberá espacio y reintentá.`;
    }
    case "file_too_large": {
      const size = fmt(b?.size);
      const max = fmt(b?.max_upload_bytes);
      if (size && max) return `El archivo ${name} pesa ${size} y el máximo que acepta Tatana es ${max}.`;
      return `El archivo ${name} supera el máximo que acepta Tatana.`;
    }
    case "incomplete_upload":
      return `Se cortó la copia de ${name} a la carpeta del caso; no se guardó nada. Reintentá.`;
    case "evidence_changed": {
      const list = (b?.files ?? []).map(f => f.filename).join(", ");
      return `Algunos archivos de la evidencia cambiaron o faltan en esta PC${list ? ` (${list})` : ""}. No se generó nada; revisá la carpeta del caso.`;
    }
    case "zip_in_progress":
      return "Ya se está armando el ZIP de este caso en esta PC.";
    case "zip_failed":
      return "No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta. Reintentá.";
    case "zip_not_found":
      return "El ZIP no está en esta PC.";
  }
  const detail = typeof b?.error === "string" && b.error.trim() ? b.error.trim() : null;
  return detail ? `Tatana respondió con un error: ${detail}` : `Tatana respondió con un error (${err.status}).`;
}
