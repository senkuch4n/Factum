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
