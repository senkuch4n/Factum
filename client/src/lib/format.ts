export function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString("es-AR", { day: "2-digit", month: "short", year: "numeric" });
}

export function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });
}

/**
 * `true` si la sigla del usuario es un dato real para mostrar. El backend
 * devuelve `""` (proveedor sin sigla) o `"-"` (placeholder de modo dev)
 * cuando no hay una; en esos casos la UI no la muestra.
 */
export function hasRealSigla(sigla: string | null | undefined): boolean {
  const s = sigla?.trim() ?? "";
  return s !== "" && s !== "-";
}

const BYTE_UNITS = ["B", "KB", "MB", "GB"] as const;

function unitIndex(bytes: number): number {
  let i = 0;
  let v = Math.max(0, bytes);
  while (v >= 1024 && i < BYTE_UNITS.length - 1) { v /= 1024; i++; }
  return i;
}

function formatIn(bytes: number, i: number): string {
  const v = Math.max(0, bytes) / 1024 ** i;
  // Un decimal con coma y sin ",0" final (mismas reglas que EvidenceUpload.FormatBytes del backend).
  return v.toLocaleString("es-AR", { maximumFractionDigits: 1, useGrouping: false });
}

/**
 * Tamaño legible en base 1024 (como el Explorador de Windows): `4294967296 → "4 GB"`,
 * `5583457485 → "5,2 GB"`, `325058560 → "310 MB"`, `512 → "512 B"`.
 */
export function formatBytes(bytes: number): string {
  const i = unitIndex(bytes);
  return `${formatIn(bytes, i)} ${BYTE_UNITS[i]}`;
}

/** `"184 / 310 MB"`: los dos valores en la unidad del total. Sin total, solo lo cargado. */
export function formatBytesPair(loaded: number, total: number | null): string {
  if (total == null) return formatBytes(loaded);
  const i = unitIndex(total);
  return `${formatIn(Math.min(loaded, total), i)} / ${formatIn(total, i)} ${BYTE_UNITS[i]}`;
}
