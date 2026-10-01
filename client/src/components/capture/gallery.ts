/**
 * Tipos y helpers puros de la galería del paso 3 (extraídos de CaptureStep
 * sin cambiar su lógica).
 */
import { FileText, ImageIcon, Video, Volume2, type LucideIcon } from "lucide-react";
import type { CaptureRoleValue } from "@/lib/api";
import { agentFileURL } from "@/lib/agent";

export interface CapturedFile { name: string; uploaded: boolean; sourcePath?: string; captureRole?: CaptureRoleValue; }
export interface LocalFile { file: File; url: string; filename: string; kind: "image" | "video" | "audio"; }

export function isVideo(name: string) { return /\.(mp4|mkv|mov)$/i.test(name); }
export function fileType(name: string): "funcionario" | "denunciante" | "screenshot" | "video" | "other" {
  if (name.includes("funcionario"))  return "funcionario";
  if (name.includes("denunciante"))  return "denunciante";
  if (name.includes("screenshot") || name.includes("captura")) return "screenshot";
  if (isVideo(name))                 return "video";
  return "other";
}
export function localFileKind(file: File): "image" | "video" | "audio" {
  if (file.type.startsWith("image/")) return "image";
  if (file.type.startsWith("video/")) return "video";
  return "audio";
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
  localIdx?: number;
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

export const srcOf = (item: GItem) => item.url ?? agentFileURL(item.name);
