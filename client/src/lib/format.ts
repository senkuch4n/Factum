export function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString("es-AR", { day: "2-digit", month: "short", year: "numeric" });
}

export function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });
}
