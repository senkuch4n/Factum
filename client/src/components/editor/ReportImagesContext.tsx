"use client";

/**
 * Contexto de las capturas del caso para el editor del informe
 * (editor-imagenes-informe, SDD §7.1 y §7.7). Lo provee `ReportStep` (sin
 * importar Tiptap) y lo consumen el NodeView de la imagen y el selector.
 */

import { createContext, useContext } from "react";
import type { Editor } from "@tiptap/core";
import type { ReportImage } from "@/lib/api";
import type { ReportImagePreviewCache, ReportImagesStatus } from "@/lib/report-images";

/**
 * Pedido de abrir el selector:
 * - sin `pos`: insertar una captura en `editor`;
 * - con `pos`: editar la descripción de la imagen que está en `pos`.
 */
export interface ImagePickerRequest {
  editor: Editor;
  pos?: number;
}

export interface ReportImagesContextValue {
  caseId: string;
  /** Listado del servidor; `null` mientras carga o si falló. */
  images: ReportImage[] | null;
  imagesStatus: ReportImagesStatus;
  reloadImages: () => void;
  cache: ReportImagePreviewCache;
  openPicker: (req: ImagePickerRequest) => void;
}

const ReportImagesContext = createContext<ReportImagesContextValue | null>(null);

export const ReportImagesProvider = ReportImagesContext.Provider;

/** `null` fuera de `ReportStep` (el editor funciona igual, sin botón "Imagen"). */
export function useReportImagesContext(): ReportImagesContextValue | null {
  return useContext(ReportImagesContext);
}

/** Etiqueta del epígrafe provisorio según el rol de la captura (D13). */
export function captionPrefix(role: ReportImage["role"] | undefined): string {
  if (role === "imei_modelo") return "Identificación (IMEI y modelo)";
  if (role === "nombre_dispositivo") return "Identificación (nombre del dispositivo)";
  return "Figura";
}

/** Texto del epígrafe después del prefijo: "desc (archivo)" o solo "archivo". */
export function captionText(alt: string, filename: string): string {
  return alt ? `${alt} (${filename})` : filename;
}
