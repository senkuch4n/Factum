"use client";

import { AlertCircle, Check, CheckCircle2, Loader2, Trash2 } from "lucide-react";
import type { CaptureRoleValue } from "@/lib/api";
import { CAPTURE_ROLE_LABELS } from "@/lib/pericial";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { kindMeta, srcOf, type GItem } from "./gallery";
import { MEDIA_SURFACE } from "./media";
import { SafeImg } from "./StageScreen";

/** Estado de envío de un ítem de la bandeja (derivado en CaptureStep). */
export type TrayUploadState = "pending" | "uploading" | "uploaded" | "error";

const UPLOAD_STATE_TEXT: Record<TrayUploadState, string> = {
  pending: "pendiente de envío",
  uploading: "enviando",
  uploaded: "subido",
  error: "error al enviar",
};

/** Sufijo del `aria-label` con el estado de envío (vacío si no hay estado). */
function uploadSuffix(state?: TrayUploadState) {
  // Pendiente es el estado por defecto (sin indicador visual): no se anuncia.
  return state && state !== "pending" ? ` — ${UPLOAD_STATE_TEXT[state]}` : "";
}

/**
 * Indicador chico de envío: ícono + color, nunca solo color. Pendiente no
 * muestra nada. Decorativo: el estado ya va en el `aria-label` del ítem.
 */
function UploadStateIcon({ state, className }: { state?: TrayUploadState; className?: string }) {
  if (!state || state === "pending") return null;
  const Icon = state === "uploaded" ? CheckCircle2 : state === "uploading" ? Loader2 : AlertCircle;
  return (
    <span
      aria-hidden="true"
      title={UPLOAD_STATE_TEXT[state]}
      className={cn(
        "flex shrink-0 items-center justify-center rounded-full",
        state === "uploaded" && "text-fx-success",
        state === "uploading" && "text-fx-accent-text",
        state === "error" && "text-fx-danger",
        className,
      )}
    >
      <Icon className={cn("h-3.5 w-3.5", state === "uploading" && "motion-safe:animate-spin")} strokeWidth={2.5} aria-hidden="true" />
    </span>
  );
}

/**
 * Miniatura de la bandeja de capturas (un `<li>`). `<button>` real para el
 * orden de tabulación; el borrar es un botón hermano (HTML no permite
 * `<button>` dentro de `<button>`). El check en la esquina refuerza la
 * selección para quien no distingue colores.
 */
export function EvidenceTrayTile({
  item, active, index, total, role, uploadState, removeDisabled, onSelect, onRemove,
}: {
  item: GItem; active: boolean; index: number; total: number; role?: CaptureRoleValue;
  uploadState?: TrayUploadState; removeDisabled?: boolean;
  onSelect: () => void; onRemove: () => void;
}) {
  const meta = kindMeta(item.kind);
  const isImg = item.kind === "screenshot" || item.kind === "local-image";

  return (
    <li className="group relative shrink-0 [scroll-snap-align:start] motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={active}
        aria-label={`Ver ${meta.label.toLowerCase()} ${index + 1} de ${total} — ${item.name}${role ? ` — marcada como ${CAPTURE_ROLE_LABELS[role]}` : ""}${uploadSuffix(uploadState)}`}
        className={cn(
          "block h-[76px] w-[76px] overflow-hidden rounded-fx-lg border-2 bg-fx-surface-2 transition-colors duration-fx-fast ease-fx",
          FOCUS_RING,
          active ? "border-fx-accent" : "border-fx-border hover:border-fx-border-strong",
        )}
      >
        {isImg
          ? <SafeImg src={srcOf(item)} alt="" className="h-full w-full object-cover" />
          : (
            <span className="flex h-full w-full items-center justify-center text-fx-text-3">
              <meta.Icon className="h-5 w-5" aria-hidden="true" />
            </span>
          )}
      </button>

      {role && (
        <span
          aria-hidden="true"
          title={CAPTURE_ROLE_LABELS[role]}
          className={cn(
            MEDIA_SURFACE,
            "pointer-events-none absolute inset-x-1 bottom-1 truncate rounded-fx-sm bg-fx-surface-1 px-1 py-px text-center text-[10px] font-bold leading-tight",
          )}
        >
          {role === "imei_modelo" ? "IMEI" : "Nombre"}
        </span>
      )}

      {uploadState && uploadState !== "pending" && (
        <UploadStateIcon
          state={uploadState}
          className="pointer-events-none absolute left-1 top-1 h-5 w-5 bg-fx-surface-1 ring-1 ring-fx-border"
        />
      )}

      {active && (
        <span
          aria-hidden="true"
          className="pointer-events-none absolute -bottom-1 -left-1 flex h-4 w-4 items-center justify-center rounded-full bg-fx-accent text-fx-on-accent ring-2 ring-fx-surface-1"
        >
          <Check className="h-2.5 w-2.5" strokeWidth={3} aria-hidden="true" />
        </span>
      )}

      {!removeDisabled && (
        <button
          type="button"
          onClick={onRemove}
          aria-label={`Eliminar ${item.name}`}
          className={cn(
            "absolute -right-1.5 -top-1.5 flex h-6 w-6 items-center justify-center rounded-full",
            "bg-fx-danger-fill text-fx-on-danger ring-2 ring-fx-surface-1",
            "transition-opacity duration-fx-fast ease-fx focus-visible:opacity-100 [@media(hover:none)]:opacity-100",
            FOCUS_RING,
            active ? "opacity-100" : "opacity-0 group-hover:opacity-100",
          )}
        >
          <Trash2 className="h-3 w-3" aria-hidden="true" />
        </button>
      )}
    </li>
  );
}

/**
 * Chip de adjunto: click en el nombre lo lleva al escenario; borrar al lado. Un
 * adjunto de la PC muestra su nombre original y, debajo, el generado (el que se
 * sube y figura en el informe).
 */
export function AttachmentChip({
  item, active, sizeMB, uploadState, removeDisabled, onSelect, onRemove,
}: {
  item: GItem; active: boolean; sizeMB: string | null;
  uploadState?: TrayUploadState; removeDisabled?: boolean;
  onSelect: () => void; onRemove: () => void;
}) {
  const meta = kindMeta(item.kind);
  const label = item.originalName ?? item.name;
  return (
    <li
      className={cn(
        "inline-flex min-w-0 max-w-full items-center gap-2 rounded-fx-md border bg-fx-surface-2 py-1.5 pl-2.5 pr-1.5 text-xs",
        "transition-colors duration-fx-fast ease-fx",
        active ? "border-fx-accent" : "border-fx-border",
      )}
    >
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={active}
        aria-label={`${item.originalName ? `Ver ${item.originalName}, se sube como ${item.name}` : `Ver ${item.name}`}${uploadSuffix(uploadState)}`}
        title={item.originalName ? `${item.originalName} (se sube como ${item.name})` : item.name}
        className={cn("flex min-w-0 items-center gap-2 rounded-fx-sm text-left", FOCUS_RING)}
      >
        <meta.Icon className="h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
        <span translate="no" className="min-w-0 max-w-[150px]">
          <span className="block truncate font-medium text-fx-text-2">{label}</span>
          {item.originalName && (
            <span className="block truncate font-mono text-[11px] text-fx-text-3">{item.name}</span>
          )}
        </span>
        {sizeMB != null && <span className="shrink-0 tabular-nums text-fx-text-3">{sizeMB}&nbsp;MB</span>}
        <UploadStateIcon state={uploadState} />
      </button>
      <button
        type="button"
        onClick={onRemove}
        disabled={removeDisabled}
        aria-label={`Eliminar ${label}`}
        className={cn(
          "flex h-7 w-7 shrink-0 items-center justify-center rounded-fx-sm text-fx-danger",
          "transition-colors duration-fx-fast ease-fx enabled:hover:bg-fx-danger-soft disabled:cursor-not-allowed disabled:opacity-40",
          FOCUS_RING,
        )}
      >
        <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
    </li>
  );
}
