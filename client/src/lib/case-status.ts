/**
 * Estado de un caso → etiqueta, severidad del `Tag` de Prime y franja de color
 * (clase `fx-*`). Lo usan `StatusBadge` y `CaseGridCard`. Puro, sin JSX.
 */
export type CaseStatusKey = "draft" | "generating" | "completed" | "error";

export interface CaseStatusMeta {
  label: string;
  severity: "secondary" | "info" | "success" | "danger";
  stripe: string;
}

export const CASE_STATUS: Record<CaseStatusKey, CaseStatusMeta> = {
  draft: { label: "Borrador", severity: "secondary", stripe: "bg-fx-border-strong" },
  generating: { label: "Generando…", severity: "info", stripe: "bg-fx-info" },
  completed: { label: "Completado", severity: "success", stripe: "bg-fx-success" },
  error: { label: "Error", severity: "danger", stripe: "bg-fx-danger" },
};

function isKnown(status: string): status is CaseStatusKey {
  return status in CASE_STATUS;
}

/**
 * Metadatos del estado. Un valor desconocido usa el estilo de borrador pero
 * muestra el valor crudo como etiqueta (igual que antes).
 */
export function caseStatusOf(status: string): CaseStatusMeta & { key: CaseStatusKey } {
  if (isKnown(status)) return { key: status, ...CASE_STATUS[status] };
  return { key: "draft", ...CASE_STATUS.draft, label: status };
}
