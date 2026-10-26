"use client";

import type { AgentExtractionCategory } from "@/lib/agent";
import type { ExtractionCategory } from "@/lib/api";
import { categoryLabel, categoryNoun, categoryStatusView, reasonCodeLabel } from "@/lib/extraction";
import { StatusChip } from "@/components/extraction/common";
import { formatBytes } from "@/lib/format";

/** Vista normalizada de una categoría (snapshot de Tatana o DTO del backend). */
export interface CategoryView {
  key: string;
  kind: "structured" | "files";
  status: string;
  count?: number;
  files?: number;
  bytes?: number;
  filesDone?: number;
  bytesDone?: number;
  errors?: number;
  hashMismatches?: number;
  reasonCode?: string;
  reason?: string;
}

/** Normaliza una categoría del snapshot (`files_total`/`files_done`…). */
export function fromSnapshotCategory(c: AgentExtractionCategory): CategoryView {
  return {
    key: c.key, kind: c.kind, status: c.status, count: c.count,
    files: c.files_total, bytes: c.bytes_total, filesDone: c.files_done, bytesDone: c.bytes_done,
    errors: c.errors, hashMismatches: c.hash_mismatches, reasonCode: c.reason_code, reason: c.reason,
  };
}

/** Normaliza una categoría del DTO del backend (`files`/`bytes`). */
export function fromDtoCategory(c: ExtractionCategory): CategoryView {
  return {
    key: c.key, kind: c.kind, status: c.status, count: c.count,
    files: c.files, bytes: c.bytes, errors: c.errors, hashMismatches: c.hash_mismatches,
    reasonCode: c.reason_code, reason: c.reason,
  };
}

function detailText(c: CategoryView): string | null {
  if (c.status === "not_extracted") {
    const base = reasonCodeLabel(c.reasonCode);
    return c.reason ? `${base} (${c.reason})` : base;
  }
  const parts: string[] = [];
  if (c.kind === "structured") {
    if (typeof c.count === "number") parts.push(`${c.count.toLocaleString("es-AR")} ${categoryNoun(c.key)}`);
  } else {
    const running = c.status === "running" && typeof c.filesDone === "number";
    const files = running ? c.filesDone : c.files;
    if (typeof files === "number") {
      const total = running && typeof c.files === "number" ? `/${c.files.toLocaleString("es-AR")}` : "";
      parts.push(`${files.toLocaleString("es-AR")}${total} archivos`);
    }
    const bytes = running ? c.bytesDone : c.bytes;
    if (typeof bytes === "number") parts.push(formatBytes(bytes));
  }
  if (typeof c.errors === "number" && c.errors > 0) parts.push(`${c.errors} con error`);
  if (typeof c.hashMismatches === "number" && c.hashMismatches > 0) parts.push(`${c.hashMismatches} sin coincidir`);
  return parts.length > 0 ? parts.join(" · ") : null;
}

/** Fila de una categoría: etiqueta + detalle (cantidades/motivo) + chip de estado. */
export function CategoryRow({ category }: { category: CategoryView }) {
  const detail = detailText(category);
  return (
    <li className="flex items-center justify-between gap-3 rounded-fx-md border border-fx-border bg-fx-surface-2 px-3 py-2.5">
      <div className="min-w-0">
        <p className="m-0 text-fx-body-sm font-medium text-fx-text">{categoryLabel(category.key)}</p>
        {detail && <p className="m-0 mt-0.5 text-xs text-fx-text-3">{detail}</p>}
      </div>
      <StatusChip view={categoryStatusView(category.status)} className="shrink-0" />
    </li>
  );
}
