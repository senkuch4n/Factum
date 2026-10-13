/**
 * Mensajes en castellano para los errores de Tatana en el flujo de evidencia
 * local (zip-local-informe-servidor, SDD §7.2). Puro: no depende de React.
 * Nunca devuelve "Failed to fetch" ni un código HTTP solo.
 */

import {
  AgentError, cachedLocalNetworkPermission,
  type AirplayUnavailableReason, type IosDeveloperModeStatus,
} from "./agent";
import { formatBytes } from "./format";
import type { AgentIdentity } from "@/hooks/useAgentIdentity";

export type AgentAction = "save" | "preview" | "generate" | "reveal" | "copy" | "record";

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

/**
 * Tatana debajo de la versión mínima del backend (tatana-instalador-autoupdate,
 * D6). Con versión real (`real_version_v1`) el instalado se actualiza solo al
 * reiniciarlo; un Tatana anterior a esa HU no, y hay que instalar el nuevo.
 */
export function agentMinVersionMessage(identity?: Pick<AgentIdentity, "info" | "versionIsReal">): string {
  const url = process.env.NEXT_PUBLIC_TATANA_DOWNLOAD_URL;
  const version = identity?.versionIsReal && identity.info?.version ? identity.info.version : null;
  if (version) {
    return url
      ? `Tu Tatana (v${version}) necesita actualizarse para esta versión de Factum. Se actualiza solo al reiniciarlo; si no, descargalo de ${url}.`
      : `Tu Tatana (v${version}) necesita actualizarse para esta versión de Factum. Se actualiza solo al reiniciarlo; si no, pedile a tu administrador el instalador nuevo.`;
  }
  return url
    ? `Tu Tatana es de una versión anterior y necesita actualizarse para esta versión de Factum. Descargá el instalador nuevo de ${url} e instalalo.`
    : "Tu Tatana es de una versión anterior y necesita actualizarse para esta versión de Factum. Pedile a tu administrador el instalador nuevo.";
}

/**
 * Texto según el estado de `useAgentIdentity` (`offline` / `outdated`). Con la
 * identidad, distingue "debajo de la versión mínima" (D6) de "sin capability".
 */
export function agentStatusMessage(
  status: "offline" | "outdated",
  identity?: Pick<AgentIdentity, "info" | "outdatedReason" | "versionIsReal">,
): string {
  if (status === "offline") return agentUnreachableMessage();
  return identity?.outdatedReason === "min_version" ? agentMinVersionMessage(identity) : AGENT_OUTDATED_MESSAGE;
}

/** Textos por defecto de los errores de grabación, si Tatana no manda `error` (SDD §7.2). */
export const RECORDING_ERROR_TEXT = {
  already_recording: "Ya hay una grabación en curso en esta PC. Detenela antes de empezar otra.",
  recording_start_timeout:
    "El celular no empezó a grabar a tiempo. Desbloquealo, dejalo con la pantalla encendida y volvé a intentar.",
  not_recording: "La grabación ya se había detenido.",
  recording_empty: "La grabación no se guardó: el archivo quedó vacío. Volvé a grabar.",
  recording_unreadable: "La grabación no se guardó: el archivo no se puede leer como video. Volvé a grabar.",
} as const;

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
  // Grabación (grabacion-android-robusta §7.2): el `error` de Tatana ya viene en castellano.
  const tatanaText = typeof b?.error === "string" && b.error.trim() ? b.error.trim() : null;
  switch (err.code) {
    case "already_recording":
    case "recording_start_timeout":
    case "not_recording":
    case "recording_empty":
    case "recording_unreadable":
      return tatanaText ?? RECORDING_ERROR_TEXT[err.code];
    case "empty_evidence":
      return `${ctx.name ?? "El archivo"} está vacío (0 bytes) y no se guarda como evidencia.`;
    case "origin_not_allowed": {
      // Build arg opcional (despliegue-nube, DT17): vacío en la instalación local.
      const url = process.env.NEXT_PUBLIC_TATANA_DOWNLOAD_URL;
      return url
        ? `El Tatana de esta PC no está habilitado para esta dirección. Descargá e instalá la versión actual desde ${url} y reintentá.`
        : "Tatana no acepta pedidos desde esta página. Pedí que agreguen esta dirección a la configuración de Tatana (Agent:AllowedOrigins).";
    }
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
    case "agent_updating":
      return "Tatana se está actualizando. Esperá unos segundos y reintentá.";
  }
  const detail = tatanaText;
  // Grabación: los errores de iPhone y de scrcpy ya vienen en castellano y se muestran tal cual.
  if (ctx.action === "record" && detail) return detail;
  return detail ? `Tatana respondió con un error: ${detail}` : `Tatana respondió con un error (${err.status}).`;
}

/* ── iPhone en Tatana (ios-herramientas-windows, SDD §9.2) ──
   Los errores de captura/grabación de iPhone los manda Tatana ya en castellano
   (`{ error, code }`) y se muestran tal cual; acá van solo los textos de la web. */

export const IOS_APPLE_SERVICE_MISSING_TITLE = "Falta el servicio de Apple en esta PC";
export const IOS_APPLE_SERVICE_MISSING_MESSAGE =
  'Para que Factum vea el iPhone, instalá la app "Apple Devices" desde Microsoft Store (o iTunes) y volvé a conectar el iPhone.';

export const AIRPLAY_UNAVAILABLE_BADGE = "No disponible en esta PC";

/** Motivo de AirPlay no disponible; sin motivo, el genérico de uxplay. */
export function airplayUnavailableText(reason: AirplayUnavailableReason | null | undefined): string {
  return reason === "not_supported_on_windows"
    ? "AirPlay no está disponible en Tatana para Windows."
    : "Falta el receptor AirPlay en esta PC.";
}

/** Resultado de "Activar Modo Desarrollador" (§4.3). */
export function iosDeveloperModeMessage(status: IosDeveloperModeStatus, deviceName: string): string {
  switch (status) {
    case "enabled":
      return `El Modo Desarrollador ya está activo en ${deviceName}.`;
    case "restarting":
      return `Listo: ${deviceName} se va a reiniciar. Cuando encienda, tocá "Encender" en el aviso de Modo Desarrollador y volvé a conectarlo.`;
    case "manual_required":
      return `${deviceName} tiene código de bloqueo, así que el Modo Desarrollador se prende a mano: en el iPhone andá a Ajustes → Privacidad y seguridad → Modo Desarrollador (ya quedó visible) y activalo. El iPhone se reinicia.`;
  }
}
