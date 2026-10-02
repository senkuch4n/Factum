/**
 * Mensajes en castellano para los errores del envío de evidencia
 * (subida-archivos-grandes, SDD §6.5). Puro: no depende de React.
 * Nunca devuelve "Failed to fetch" ni "HTTP 413" crudos.
 */

import { UploadError } from "./api";
import { formatBytes } from "./format";

export interface UploadMessage {
  tone: "error" | "info";
  text: string;
}

const fmt = (n: number | null | undefined) => (typeof n === "number" && Number.isFinite(n) ? formatBytes(n) : null);

export function uploadErrorMessage(err: unknown, name: string, size: number | null): UploadMessage {
  if (!(err instanceof UploadError)) {
    // Falla de la bajada desde Tatana (red local, agente cerrado, disco del navegador lleno).
    return {
      tone: "error",
      text: `No se pudo preparar ${name} desde el agente Tatana. Verificá que el agente siga abierto y que la PC tenga espacio libre en disco, y volvé a enviar.`,
    };
  }

  if (err.kind === "aborted") {
    return {
      tone: "info",
      text: "Envío cancelado. Los archivos ya enviados quedaron guardados en el caso; los demás siguen en la lista.",
    };
  }

  const cut = `Se cortó el envío de ${name}. Los archivos anteriores quedaron guardados; tocá "Enviar evidencia y continuar" para seguir.`;
  if (err.kind === "network") return { tone: "error", text: cut };

  const body = err.body;
  const code = err.code;
  const status = err.status;

  if (status === 413 || code === "file_too_large") {
    const pesa = fmt(body?.size ?? size);
    const max = fmt(body?.max_upload_bytes);
    const head = pesa ? `El archivo ${name} pesa ${pesa}` : `El archivo ${name} es demasiado grande`;
    const tail = max ? ` y el máximo permitido es ${max}` : "";
    return { tone: "error", text: `${head}${tail}. Quitalo de la lista o pedí que se suba el tope.` };
  }

  if (status === 507 || code === "insufficient_storage") {
    const required = fmt(body?.required_bytes);
    const available = fmt(body?.available_bytes);
    return {
      tone: "error",
      text: required && available
        ? `No hay espacio en el disco del servidor para guardar ${name} (hace falta ${required}, quedan ${available}). Liberá espacio y volvé a enviar.`
        : `No hay espacio en el disco del servidor para guardar ${name}. Liberá espacio y volvé a enviar.`,
    };
  }

  if (status === 409 || code === "case_not_editable") {
    return { tone: "error", text: "El caso ya fue generado y no admite más evidencia." };
  }

  if (code === "invalid_filename") {
    return {
      tone: "error",
      text: `El nombre del archivo ${name} no es válido para guardarlo en el caso (tiene una ruta o caracteres no permitidos). Quitalo de la lista.`,
    };
  }

  if (code === "incomplete_upload" || code === "length_required") return { tone: "error", text: cut };

  if (status === 500 || code === "storage_error") {
    return { tone: "error", text: `El servidor no pudo guardar ${name}. Los archivos anteriores quedaron guardados; volvé a intentar.` };
  }

  if (status === 401) {
    return { tone: "error", text: "Tu sesión expiró. Volvé a iniciar sesión y enviá de nuevo la evidencia." };
  }

  return {
    tone: "error",
    text: body?.error
      ? `No se pudo enviar ${name}: ${body.error}`
      : `No se pudo enviar ${name} (error ${status}).`,
  };
}
