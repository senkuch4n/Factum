export type { User, Case, DeviceInput } from "@/lib/api";
export type { Device, AgentEvent, AgentFile, VideoVariant } from "@/lib/agent";

export interface CapturedFile {
  name: string;
  uploaded: boolean;
  // Ruta de origen en el dispositivo (solo para archivos traídos con el explorador de
  // archivos) — viaja hasta el informe forense como referencia de dónde salió cada archivo.
  sourcePath?: string;
}

export interface CaseFormData {
  NroReferencia: string;
  NombreDenunciante: string;
  DNIDenunciante: string;
  Observaciones: string;
  imeiOverride: string;
}

export type IosRecordMode = "video_only" | "with_mic" | "on_device" | "airplay";
