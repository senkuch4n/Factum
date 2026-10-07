/**
 * Tipos y helpers puros de la galería del paso 3 (extraídos de CaptureStep
 * sin cambiar su lógica).
 */
import { FileText, ImageIcon, Video, Volume2, type LucideIcon } from "lucide-react";
import { agentFileURL } from "@/lib/agent";
import type { CapturedFile } from "@/types";

/** Una sola definición en `@/types` (zip-local-informe-servidor F6); se reexporta por compatibilidad. */
export type { CapturedFile };

export function isVideo(name: string) { return /\.(mp4|mkv|mov)$/i.test(name); }
export function fileType(name: string): "funcionario" | "denunciante" | "screenshot" | "video" | "other" {
  if (name.includes("funcionario"))  return "funcionario";
  if (name.includes("denunciante"))  return "denunciante";
  if (name.includes("screenshot") || name.includes("captura")) return "screenshot";
  if (isVideo(name))                 return "video";
  return "other";
}
/**
 * Tipo de ítem de un archivo generado en el navegador (cámara externa o adjunto
 * de la PC). Manda el MIME; si viene vacío, la extensión. Lo que no se reconoce
 * queda como "Archivo" (no como "Audio"). Nunca devuelve "video": un video
 * local no tiene variantes del agente y va a "Adjuntos", no a `VideoCard`.
 */
export function localKindOf(type: string, name: string): "local-image" | "local-video" | "local-audio" | "other" {
  if (type.startsWith("image/")) return "local-image";
  if (type.startsWith("video/")) return "local-video";
  if (type.startsWith("audio/")) return "local-audio";
  const ext = name.split(".").pop()?.toLowerCase() ?? "";
  if (/^(jpe?g|png|gif|webp|heic|bmp)$/.test(ext))             return "local-image";
  if (/^(mp4|mov|mkv|webm|avi|m4v)$/.test(ext))                return "local-video";
  if (/^(mp3|m4a|wav|ogg|opus|aac|amr|flac)$/.test(ext))       return "local-audio";
  return "other";
}

/**
 * Nombre `adjunto_<fecha>_<hora>_<n>.<ext>` con el menor `n ≥ 1` libre en
 * `taken`. Mismo formato de siempre; solo evita que dos tandas en el mismo
 * segundo repitan nombre (y pisen el blob de la primera).
 */
export function nextAdjuntoName(date: string, time: string, ext: string, taken: ReadonlySet<string>): string {
  let n = 1;
  while (taken.has(`adjunto_${date}_${time}_${n}.${ext}`)) n++;
  return `adjunto_${date}_${time}_${n}.${ext}`;
}

/* Ítem unificado de la galería: funde screenshots/videos/otros (que vienen del
   agente) y los adjuntos locales en una sola línea de tiempo. */
export type GKind = "screenshot" | "video" | "other" | "local-image" | "local-video" | "local-audio";
export interface GItem {
  kind: GKind;
  key: string;
  name: string;
  url?: string;
  sourcePath?: string;
  remoteFile?: CapturedFile;
  /** Tamaño en bytes (solo archivos generados en el navegador). */
  sizeBytes?: number;
  /** Nombre original del archivo de la PC; `name` es el generado, que es el que se sube. */
  originalName?: string;
}

/** Etiqueta e ícono por tipo de ítem. Sin color por tipo (DP5 A): el ícono ya diferencia. */
export function kindMeta(kind: GKind): { label: string; Icon: LucideIcon } {
  switch (kind) {
    case "screenshot":  return { label: "Captura",   Icon: ImageIcon };
    case "video":       return { label: "Grabación", Icon: Video };
    case "local-image": return { label: "Imagen",    Icon: ImageIcon };
    case "local-video": return { label: "Video",     Icon: Video };
    case "local-audio": return { label: "Audio",     Icon: Volume2 };
    default:            return { label: "Archivo",   Icon: FileText };
  }
}

/**
 * URL de vista de un ítem. `CaptureStep` resuelve `url` (blob local o carpeta
 * del caso en Tatana); sin `url`, el archivo sigue en la raíz de Tatana.
 */
export const srcOf = (item: GItem) => item.url ?? agentFileURL(item.name);
