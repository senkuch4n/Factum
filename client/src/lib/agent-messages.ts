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

export type AgentAction = "save" | "preview" | "generate" | "reveal" | "copy" | "record" | "session";

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

/** Mismo caso generándose en otra pestaña o ventana (DT9). Se muestra como advertencia. */
export const GENERATION_BUSY_MESSAGE = "Este caso ya se está generando en otra pestaña o ventana. Esperá a que termine.";

/** Textos por defecto de los errores de grabación, si Tatana no manda `error` (SDD §7.2). */
export const RECORDING_ERROR_TEXT = {
  already_recording: "Ya hay una grabación en curso en esta PC. Detenela antes de empezar otra.",
  recording_start_timeout:
    "El celular no empezó a grabar a tiempo. Desbloquealo, dejalo con la pantalla encendida y volvé a intentar.",
  not_recording: "La grabación ya se había detenido.",
  recording_empty: "La grabación no se guardó: el archivo quedó vacío. Volvé a grabar.",
  recording_unreadable: "La grabación no se guardó: el archivo no se puede leer como video. Volvé a grabar.",
} as const;

/**
 * Aviso de pantalla del celular apagada o bloqueada antes de capturar o grabar
 * (pulido-post-qa-final §8.7). Mismo texto para los dos estados: el perito
 * resuelve igual (encender y desbloquear). `session`: inicio de la grabación de
 * la sesión (grabacion-sesion-completa §9.3).
 */
export function screenBlockMessage(action: "screenshot" | "record" | "session"): string {
  if (action === "session") return "Para empezar a grabar la recolección, encendé y desbloqueá la pantalla del celular.";
  return `La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para ${action === "record" ? "grabar" : "capturar"}.`;
}

/**
 * Textos por defecto de los errores de la grabación de la sesión (grabacion-sesion-completa
 * §7.7), si Tatana no manda `error`. `session_active` del start es el aviso de §9.3.
 */
export const SESSION_ERROR_TEXT = {
  session_active: "Tatana está grabando la sesión de otro caso. Cerrala desde ese caso para grabar esta.",
  session_not_active: "No hay una grabación de la sesión en curso para este caso.",
  session_not_found: "Este caso no tiene grabación de la sesión en esta PC.",
  session_file_protected:
    "Los tramos de la grabación de la sesión no se pueden borrar: son parte del registro de la recolección.",
  session_platform_unsupported: "Esta versión de Tatana no graba la sesión en iPhone.",
  session_ios_mode_unsupported: "La grabación de la sesión en iPhone usa solo el modo «Solo video».",
} as const;

/** 409 `recording_session_open` del backend al generar (§6.5): mismo texto que el bloqueo del paso 5. */
export const RECORDING_SESSION_OPEN_MESSAGE = "Primero cerrá la grabación de la sesión.";

/** Textos de los códigos nuevos de la API (§7.7), por si el backend no manda `error`. */
export const RECORDING_SESSION_API_TEXT: Record<string, string> = {
  recording_session_disabled: "La grabación de la sesión está deshabilitada en esta instalación.",
  recording_session_required: "La configuración de esta instalación exige grabar la sesión.",
  recording_session_not_enabled: "Este caso no tiene activada la grabación de la sesión.",
  invalid_recording_session: "Datos de la grabación de la sesión inválidos.",
  recording_session_conflict: "La grabación de la sesión registrada no coincide con la de esta PC.",
  recording_session_busy: "Otra pestaña está actualizando la grabación de la sesión. Reintentá en unos segundos.",
  recording_session_open: RECORDING_SESSION_OPEN_MESSAGE,
  recording_session_file: "Los tramos de la grabación de la sesión no se pueden quitar del caso.",
};

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
      // Generar desde dos pestañas (wizard-concurrencia-pestanas DT9): advertencia, no error.
      return ctx.action === "generate" ? GENERATION_BUSY_MESSAGE : "Ya se está armando el ZIP de este caso en esta PC.";
    case "zip_failed":
      return "No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta. Reintentá.";
    case "zip_not_found":
      return "El ZIP no está en esta PC.";
    case "agent_updating":
      return "Tatana se está actualizando. Esperá unos segundos y reintentá.";
    case "screen_off":
    case "screen_locked":
      // Normalmente lo muestra el aviso del paso 3 (con "Capturar igual"); esto es para un llamador genérico.
      return screenBlockMessage(ctx.action === "record" ? "record" : "screenshot");
    case "session_active":
      // En la sesión, el aviso propio (§9.3); en `record/start`, el texto de Tatana ("Usá «Marcar momento»").
      return ctx.action === "session" ? SESSION_ERROR_TEXT.session_active : tatanaText ?? SESSION_ERROR_TEXT.session_active;
    case "session_not_active":
    case "session_not_found":
    case "session_file_protected":
    case "session_platform_unsupported":
    case "session_ios_mode_unsupported":
      return tatanaText ?? SESSION_ERROR_TEXT[err.code];
  }
  const detail = tatanaText;
  // Grabación: los errores de iPhone y de scrcpy ya vienen en castellano y se muestran tal cual.
  if ((ctx.action === "record" || ctx.action === "session") && detail) return detail;
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
