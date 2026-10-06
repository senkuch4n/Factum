/**
 * Reglas y textos de la marca del informe por cuenta (marca-por-cliente, SDD
 * §5, §8.4 y §9.2). Puro, sin React. Los textos de `BRANDING_MESSAGES`, las
 * constantes y los algoritmos de color son los mismos del backend
 * (`Services/Branding/BrandingErrors.cs`, `BrandingRules.cs`,
 * `BrandingColors.cs`): si cambian allá, cambian acá. El backend es la
 * autoridad; esto es para avisar antes de enviar.
 */

import { ApiError, SERVER_UNREACHABLE } from "@/lib/api";
import type { AccountBranding, BrandingField, BrandingImageAction, BrandingSaveMetadata } from "@/types";

/* ── Constantes (§8.4, iguales al backend) ── */

export const BRANDING_LIMITS = {
  name: 150,
  contactLines: 6,
  contactLine: 150,
  imageBytes: 1_048_576,
  minSide: 16,
  maxSide: 4096,
  /** Solo informativo: el tope real lo aplica el backend (413). */
  requestBytes: 2_621_440,
} as const;

/** Verde de Factum: primario por defecto ("RRGGBB", sin '#'). */
export const FACTUM_PRIMARY = "2F6F12";
/** Tinte de Factum: acento por defecto. */
export const FACTUM_ACCENT = "E8F3DF";
/** Tinta del informe: el texto que va sobre el acento. */
export const INK_COLOR = "0E1013";
/** Contraste mínimo (WCAG AA, texto normal) del primario con blanco y del acento con la tinta. */
export const MIN_CONTRAST = 4.5;

/* ── Textos (§5.1, idénticos al backend) ── */

export const BRANDING_MESSAGES = {
  name_long: "El nombre de la organización puede tener hasta 150 caracteres.",
  control_chars: "No puede tener saltos de línea ni caracteres de control.",
  contact_line_long: "Cada línea de contacto puede tener hasta 150 caracteres.",
  contact_lines_max: "Podés cargar hasta 6 líneas de contacto.",
  color_format: "Ingresá un color en formato #RRGGBB.",
  image_format: "El archivo no es una imagen PNG ni JPEG.",
  image_too_big: "La imagen supera el máximo de 1 MB.",
  image_missing: "Falta el archivo de la imagen.",
  invalid_request: "La solicitud no es válida. Recargá e intentá de nuevo.",
  stale_update: "La marca se modificó desde otra sesión. Recargá para ver los cambios.",
  request_too_large: "La marca supera el tamaño máximo permitido (2,5 MB en total).",
  image_not_found: "La imagen no existe.",
} as const;

/** `Contraste {c}:1 con blanco, mínimo 4.5:1.` (`c` ya truncado). */
export function primaryContrastMessage(c: number): string {
  return `Contraste ${formatContrast(c)}:1 con blanco, mínimo 4.5:1.`;
}

/** `Contraste {c}:1 con el texto, mínimo 4.5:1.` */
export function accentContrastMessage(c: number): string {
  return `Contraste ${formatContrast(c)}:1 con el texto, mínimo 4.5:1.`;
}

/** `La imagen mide {w}×{h} px; …` (× = U+00D7). */
export function imageSizeMessage(w: number, h: number): string {
  return `La imagen mide ${w}×${h} px; cada lado tiene que estar entre 16 y 4096 px.`;
}

/** Mensaje genérico cuando la API no trae un texto propio. */
const GENERIC_ERROR = "No se pudo guardar la marca. Intentá de nuevo.";

/* ── Colores (mismo algoritmo que `BrandingColors`) ── */

const HEX6 = /^[0-9A-Fa-f]{6}$/;

/** "#1a2b3c" | "1A2B3C" (con espacios alrededor o no) → "1A2B3C"; cualquier otra cosa → null. */
export function normalizeHex(raw: string | null | undefined): string | null {
  let s = (raw ?? "").trim();
  if (s.startsWith("#")) s = s.slice(1);
  return HEX6.test(s) ? s.toUpperCase() : null;
}

function relativeLuminance(hex6: string): number {
  const channel = (offset: number) => {
    const c = parseInt(hex6.slice(offset, offset + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(0) + 0.7152 * channel(2) + 0.0722 * channel(4);
}

/** Contraste WCAG 2.x entre dos colores (6 hex, sin '#'). Simétrico. */
export function contrast(hexA: string, hexB: string): number {
  const a = relativeLuminance(hexA);
  const b = relativeLuminance(hexB);
  const [hi, lo] = a >= b ? [a, b] : [b, a];
  return (hi + 0.05) / (lo + 0.05);
}

export function contrastWithWhite(hex6: string): number {
  return contrast(hex6, "FFFFFF");
}

/** Truncado (no redondeo) a 1 decimal, igual que el backend: 4.49 → 4.4. */
export function displayContrast(ratio: number): number {
  return Math.floor(ratio * 10) / 10;
}

/** "4.4" (punto decimal, como el backend). */
export function formatContrast(c: number): string {
  return c.toFixed(1);
}

export type ColorRole = "primary" | "accent";

/** Estado del contraste de un color del formulario, para el badge y la validación. */
export type ColorCheck =
  | { kind: "default" }
  | { kind: "invalid" }
  | { kind: "ok"; hex: string; ratio: number; shown: number }
  | { kind: "low"; hex: string; ratio: number; shown: number };

/** Evalúa un color del formulario ("" = el de Factum). La comparación con 4.5 usa el valor sin truncar. */
export function checkColor(raw: string, role: ColorRole): ColorCheck {
  if (raw.trim() === "") return { kind: "default" };
  const hex = normalizeHex(raw);
  if (!hex) return { kind: "invalid" };
  const ratio = role === "primary" ? contrastWithWhite(hex) : contrast(hex, INK_COLOR);
  const shown = displayContrast(ratio);
  return ratio < MIN_CONTRAST ? { kind: "low", hex, ratio, shown } : { kind: "ok", hex, ratio, shown };
}

/** Color que efectivamente usa el informe: el cargado si es válido, si no el de Factum. */
export function effectiveColor(raw: string | null | undefined, role: ColorRole): string {
  const c = checkColor(raw ?? "", role);
  if (c.kind === "ok") return `#${c.hex}`;
  return `#${role === "primary" ? FACTUM_PRIMARY : FACTUM_ACCENT}`;
}

/* ── Estado del formulario ── */

export interface BrandingImageValue {
  action: BrandingImageAction;
  /** Archivo nuevo (solo con `replace`). */
  file: File | null;
  /** Object URL del archivo nuevo (lo libera el diálogo). */
  previewUrl: string | null;
  width?: number;
  height?: number;
  size?: number;
}

export interface BrandingFormValues {
  organization_name: string;
  /** En edición pueden estar vacías: se descartan al enviar. */
  contact_lines: string[];
  /** "" = Factum; si no, lo que escribió el usuario (se normaliza al enviar). */
  primary_color: string;
  accent_color: string;
  logo: BrandingImageValue;
  isotype: BrandingImageValue;
}

export const KEEP_IMAGE: BrandingImageValue = { action: "keep", file: null, previewUrl: null };

/** Formulario a partir de la marca guardada (o vacía si `exists=false`). Siempre una línea de contacto visible. */
export function brandingToForm(b: AccountBranding | null): BrandingFormValues {
  const lines = b?.contact_lines?.length ? [...b.contact_lines] : [""];
  return {
    organization_name: b?.organization_name ?? "",
    contact_lines: lines,
    primary_color: b?.primary_color ?? "",
    accent_color: b?.accent_color ?? "",
    logo: KEEP_IMAGE,
    isotype: KEEP_IMAGE,
  };
}

/** Líneas que viajan: trim y sin vacías. */
export function cleanContactLines(lines: string[]): string[] {
  return lines.map(l => (l ?? "").trim()).filter(l => l !== "");
}

/** Índices del formulario de las líneas que viajan (para mapear el `index` de un 400). */
export function sentLineIndexes(lines: string[]): number[] {
  const out: number[] = [];
  lines.forEach((l, i) => { if ((l ?? "").trim() !== "") out.push(i); });
  return out;
}

function colorForSave(raw: string): string {
  if (raw.trim() === "") return "";
  const hex = normalizeHex(raw);
  return hex ? `#${hex}` : raw.trim();
}

function sameImage(a: BrandingImageValue, b: BrandingImageValue): boolean {
  return a.action === b.action && a.file === b.file;
}

/** `true` si no hay cambios para guardar (después de normalizar). */
export function sameBrandingForm(a: BrandingFormValues, b: BrandingFormValues): boolean {
  const la = cleanContactLines(a.contact_lines);
  const lb = cleanContactLines(b.contact_lines);
  return a.organization_name.trim() === b.organization_name.trim()
    && la.length === lb.length && la.every((l, i) => l === lb[i])
    && colorForSave(a.primary_color) === colorForSave(b.primary_color)
    && colorForSave(a.accent_color) === colorForSave(b.accent_color)
    && sameImage(a.logo, b.logo)
    && sameImage(a.isotype, b.isotype);
}

/* ── Validación (§5, mismo orden que el backend) ── */

export type BrandingErrorKey = BrandingField | `contact_lines.${number}`;
export type BrandingErrors = Partial<Record<BrandingErrorKey, string>>;

// Constructor y no literal: mismo criterio que `char.IsControl` (categoría Cc).
const CONTROL_CHARS = new RegExp("[\\u0000-\\u001F\\u007F-\\u009F]");

export function hasControlChars(s: string): boolean {
  return CONTROL_CHARS.test(s);
}

export function validateOrganizationName(raw: string): string | null {
  const v = raw.trim();
  if (v.length > BRANDING_LIMITS.name) return BRANDING_MESSAGES.name_long;
  if (hasControlChars(v)) return BRANDING_MESSAGES.control_chars;
  return null;
}

export function validateContactLine(raw: string): string | null {
  const v = (raw ?? "").trim();
  if (v.length > BRANDING_LIMITS.contactLine) return BRANDING_MESSAGES.contact_line_long;
  if (hasControlChars(v)) return BRANDING_MESSAGES.control_chars;
  return null;
}

export function validateColor(raw: string, role: ColorRole): string | null {
  const c = checkColor(raw, role);
  if (c.kind === "invalid") return BRANDING_MESSAGES.color_format;
  if (c.kind === "low") return role === "primary" ? primaryContrastMessage(c.shown) : accentContrastMessage(c.shown);
  return null;
}

/** Todos los errores del formulario (a diferencia del backend, que corta en el primero). */
export function validateBrandingForm(form: BrandingFormValues): BrandingErrors {
  const errors: BrandingErrors = {};
  const name = validateOrganizationName(form.organization_name);
  if (name) errors.organization_name = name;
  form.contact_lines.forEach((l, i) => {
    const e = validateContactLine(l);
    if (e) errors[`contact_lines.${i}`] = e;
  });
  if (cleanContactLines(form.contact_lines).length > BRANDING_LIMITS.contactLines) {
    errors.contact_lines = BRANDING_MESSAGES.contact_lines_max;
  }
  const primary = validateColor(form.primary_color, "primary");
  if (primary) errors.primary_color = primary;
  const accent = validateColor(form.accent_color, "accent");
  if (accent) errors.accent_color = accent;
  if (form.logo.action === "replace" && !form.logo.file) errors.logo = BRANDING_MESSAGES.image_missing;
  if (form.isotype.action === "replace" && !form.isotype.file) errors.isotype = BRANDING_MESSAGES.image_missing;
  return errors;
}

/** Orden de foco ante varios errores (el de la pantalla, igual al del backend). */
export function firstErrorKey(errors: BrandingErrors, lineCount: number): BrandingErrorKey | null {
  if (errors.organization_name) return "organization_name";
  for (let i = 0; i < lineCount; i++) if (errors[`contact_lines.${i}`]) return `contact_lines.${i}`;
  if (errors.contact_lines) return "contact_lines";
  if (errors.primary_color) return "primary_color";
  if (errors.accent_color) return "accent_color";
  if (errors.logo) return "logo";
  if (errors.isotype) return "isotype";
  if (errors.metadata) return "metadata";
  return null;
}

/* ── Envío ── */

/** Metadata del PUT (§8.1). `replace` sin archivo se manda como `keep` (no debería pasar: lo frena la validación). */
export function buildBrandingMetadata(form: BrandingFormValues, base: AccountBranding): BrandingSaveMetadata {
  const action = (img: BrandingImageValue): BrandingImageAction =>
    img.action === "replace" && !img.file ? "keep" : img.action;
  return {
    organization_name: form.organization_name.trim(),
    contact_lines: cleanContactLines(form.contact_lines),
    primary_color: colorForSave(form.primary_color),
    accent_color: colorForSave(form.accent_color),
    logo_action: action(form.logo),
    isotype_action: action(form.isotype),
    expected_updated_at: base.exists ? base.updated_at : null,
  };
}

/** `multipart/form-data`: `metadata` (JSON) y `logo`/`isotype` solo con `replace`. */
export function buildBrandingFormData(form: BrandingFormValues, base: AccountBranding): FormData {
  const fd = new FormData();
  const metadata = buildBrandingMetadata(form, base);
  fd.append("metadata", JSON.stringify(metadata));
  if (metadata.logo_action === "replace" && form.logo.file) fd.append("logo", form.logo.file, form.logo.file.name);
  if (metadata.isotype_action === "replace" && form.isotype.file) {
    fd.append("isotype", form.isotype.file, form.isotype.file.name);
  }
  return fd;
}

/* ── Imágenes (chequeo previo; el backend vuelve a validar) ── */

export type ImageInspection =
  | { ok: true; width: number; height: number; contentType: "image/png" | "image/jpeg" }
  | { ok: false; message: string };

const PNG_SIG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const JPEG_SIG = [0xff, 0xd8, 0xff];

function startsWith(bytes: Uint8Array, sig: number[]): boolean {
  return bytes.length >= sig.length && sig.every((b, i) => bytes[i] === b);
}

/** Peso → firma PNG/JPEG → dimensiones, en ese orden (mismo que el backend). */
export async function inspectImageFile(file: File): Promise<ImageInspection> {
  if (file.size > BRANDING_LIMITS.imageBytes) return { ok: false, message: BRANDING_MESSAGES.image_too_big };
  if (file.size === 0) return { ok: false, message: BRANDING_MESSAGES.image_format };
  let head: Uint8Array;
  try {
    head = new Uint8Array(await file.slice(0, 32).arrayBuffer());
  } catch {
    return { ok: false, message: BRANDING_MESSAGES.image_format };
  }
  const contentType = startsWith(head, PNG_SIG) ? "image/png" : startsWith(head, JPEG_SIG) ? "image/jpeg" : null;
  if (!contentType) return { ok: false, message: BRANDING_MESSAGES.image_format };
  let bitmap: ImageBitmap | null = null;
  try {
    bitmap = await createImageBitmap(file);
    const { width, height } = bitmap;
    const inRange = (n: number) => n >= BRANDING_LIMITS.minSide && n <= BRANDING_LIMITS.maxSide;
    if (!inRange(width) || !inRange(height)) return { ok: false, message: imageSizeMessage(width, height) };
    return { ok: true, width, height, contentType };
  } catch {
    return { ok: false, message: BRANDING_MESSAGES.image_format };
  } finally {
    bitmap?.close();
  }
}

/** "245 KB" / "1,0 MB" / "830 B" (es-AR). */
export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${Math.round(n / 1024).toLocaleString("es-AR")} KB`;
  return `${(n / (1024 * 1024)).toLocaleString("es-AR", { minimumFractionDigits: 1, maximumFractionDigits: 1 })} MB`;
}

/* ── Errores de la API ── */

/** Texto para un error que no va a un campo (banner). */
export function brandingErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 0) return SERVER_UNREACHABLE;
    if (err.code === "stale_update") return BRANDING_MESSAGES.stale_update;
    if (err.serverMessage) return err.serverMessage;
    if (err.code === "request_too_large" || err.status === 413) return BRANDING_MESSAGES.request_too_large;
  }
  return GENERIC_ERROR;
}
