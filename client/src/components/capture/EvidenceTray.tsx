"use client";

import {
  AlertCircle, AlertTriangle, Check, CheckCircle2, Clock, ClockAlert, ClockCheck, Film, HardDrive, Loader2, Trash2,
} from "lucide-react";
import type { CaptureRoleValue, CaseTimestamp } from "@/lib/api";
import { CAPTURE_ROLE_LABELS } from "@/lib/pericial";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { kindMeta, srcOf, type GItem } from "./gallery";
import { MEDIA_SURFACE } from "./media";
import { SafeImg } from "./StageScreen";

/**
 * Estado de envío de un ítem de la bandeja (derivado en CaptureStep).
 * `saving`/`stored`/`missing`/`save_error`/`auth_pending`: flujo agent (evidencia en esta PC).
 * `auth_pending`: ya está en la PC, el registro espera a que se vuelva a iniciar sesión (sesion-larga §8.9).
 */
export type TrayUploadState =
  | "pending" | "uploading" | "uploaded" | "error"
  | "saving" | "stored" | "missing" | "save_error" | "auth_pending";

const UPLOAD_STATE_TEXT: Record<TrayUploadState, string> = {
  pending: "pendiente de envío",
  uploading: "enviando",
  uploaded: "subido",
  error: "error al enviar",
  saving: "guardando en esta PC",
  stored: "en esta PC",
  missing: "no está en esta PC",
  save_error: "error al guardar en esta PC",
  auth_pending: "pendiente de registrar: iniciá sesión",
};

/** Texto corto visible del estado (chips): solo los estados del flujo agent que importan. */
const VISIBLE_STATE_TEXT: Partial<Record<TrayUploadState, string>> = {
  stored: "En esta PC",
  missing: "No está en esta PC",
  auth_pending: "Pendiente de registrar",
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
function UploadStateIcon({ state, hash, className }: { state?: TrayUploadState; hash?: string; className?: string }) {
  if (!state || state === "pending") return null;
  const busy = state === "uploading" || state === "saving";
  const Icon = state === "uploaded" ? CheckCircle2
    : state === "stored" ? HardDrive
    : state === "auth_pending" ? Clock
    : busy ? Loader2
    : AlertCircle;
  // El hash abreviado va solo en el `title` (SDD §7.7): ayuda a cotejar sin ocupar lugar.
  const title = state === "stored" && hash ? `${UPLOAD_STATE_TEXT[state]} · SHA-256 ${hash.slice(0, 8)}…` : UPLOAD_STATE_TEXT[state];
  return (
    <span
      aria-hidden="true"
      title={title}
      className={cn(
        "flex shrink-0 items-center justify-center rounded-full",
        (state === "uploaded" || state === "stored") && "text-fx-success",
        busy && "text-fx-accent-text",
        (state === "error" || state === "save_error") && "text-fx-danger",
        (state === "missing" || state === "auth_pending") && "text-fx-warning",
        className,
      )}
    >
      <Icon className={cn("h-3.5 w-3.5", busy && "motion-safe:animate-spin")} strokeWidth={2.5} aria-hidden="true" />
    </span>
  );
}

/** Texto del sello de tiempo de un archivo (tooltip y `aria-label`, sellado-tiempo §9.3). */
export function sealText(seal: CaseTimestamp): string {
  switch (seal.status) {
    case "granted": {
      const parts = [seal.gen_time_display, seal.authority].filter(Boolean);
      return parts.length > 0 ? `Sello de tiempo: ${parts.join(" · ")}` : "Sello de tiempo obtenido";
    }
    case "pending":
      return "Sello de tiempo pendiente; se reintenta solo";
    case "failed":
      return "No se pudo obtener el sello de tiempo";
  }
}

/**
 * Indicador chico del sello de tiempo: reloj con check (sellado), reloj gris
 * (pendiente) o reloj con advertencia (falló). Ícono + forma distinta por
 * estado, nunca solo color. El texto va en el `title` (tooltip) y en el
 * `aria-label` del ítem; el indicador en sí es decorativo para el lector.
 */
function SealBadge({ seal, className }: { seal: CaseTimestamp; className?: string }) {
  const Icon = seal.status === "granted" ? ClockCheck : seal.status === "failed" ? ClockAlert : Clock;
  return (
    <span
      aria-hidden="true"
      title={sealText(seal)}
      className={cn(
        "flex shrink-0 items-center justify-center rounded-full",
        seal.status === "granted" ? "text-fx-success" : seal.status === "failed" ? "text-fx-warning" : "text-fx-text-3",
        className,
      )}
    >
      <Icon className="h-3.5 w-3.5" strokeWidth={2.25} aria-hidden="true" />
    </span>
  );
}

/** Sufijo del `aria-label` con el sello (vacío sin sello). */
function sealSuffix(seal?: CaseTimestamp | null) {
  return seal ? ` — ${sealText(seal)}` : "";
}

/**
 * Miniatura de la bandeja de capturas (un `<li>`). `<button>` real para el
 * orden de tabulación; el borrar es un botón hermano (HTML no permite
 * `<button>` dentro de `<button>`). El check en la esquina refuerza la
 * selección para quien no distingue colores.
 */
/** Aviso no bloqueante de un ítem (grabacion-android-robusta §7.9). */
export type TrayNotice = "incomplete" | "black";

const NOTICE_TEXT: Record<TrayNotice, string> = {
  incomplete: "grabación incompleta",
  black: "captura negra",
};

export function EvidenceTrayTile({
  item, active, index, total, role, notice, uploadState, seal, removeDisabled, onSelect, onRemove,
}: {
  item: GItem; active: boolean; index: number; total: number; role?: CaptureRoleValue;
  notice?: TrayNotice;
  uploadState?: TrayUploadState;
  /** Sello de tiempo del archivo registrado (solo con el sellado activo). */
  seal?: CaseTimestamp | null;
  removeDisabled?: boolean;
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
        aria-label={`Ver ${meta.label.toLowerCase()} ${index + 1} de ${total} — ${item.name}${role ? ` — marcada como ${CAPTURE_ROLE_LABELS[role]}` : ""}${notice ? ` — ${NOTICE_TEXT[notice]}` : ""}${uploadSuffix(uploadState)}${sealSuffix(seal)}`}
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
          hash={item.remoteFile?.sha256}
          className="pointer-events-none absolute left-1 top-1 h-5 w-5 bg-fx-surface-1 ring-1 ring-fx-border"
        />
      )}

      {/* Sello de tiempo: al lado del estado de envío. Con `title` propio (tooltip al pasar el mouse). */}
      {seal && (
        <SealBadge
          seal={seal}
          className="absolute left-[26px] top-1 h-5 w-5 cursor-default bg-fx-surface-1 ring-1 ring-fx-border"
        />
      )}

      {/* Aviso (ícono + texto en el aria-label, nunca solo color). Abajo a la derecha:
          no tapa el check de seleccionado (abajo a la izquierda) ni la marca de rol. */}
      {notice && (
        <span
          aria-hidden="true"
          title={NOTICE_TEXT[notice][0].toUpperCase() + NOTICE_TEXT[notice].slice(1)}
          className={cn(
            "pointer-events-none absolute -bottom-1 -right-1 flex h-5 w-5 items-center justify-center rounded-full",
            "bg-fx-warning-soft text-fx-warning ring-2 ring-fx-surface-1",
          )}
        >
          <AlertTriangle className="h-3 w-3" strokeWidth={2.5} aria-hidden="true" />
        </span>
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
  item, active, sizeMB, uploadState, seal, removeDisabled, onSelect, onRemove,
}: {
  item: GItem; active: boolean; sizeMB: string | null;
  uploadState?: TrayUploadState;
  /** Sello de tiempo del archivo registrado (solo con el sellado activo). */
  seal?: CaseTimestamp | null;
  removeDisabled?: boolean;
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
        aria-label={`${item.originalName ? `Ver ${item.originalName}, se sube como ${item.name}` : `Ver ${item.name}`}${uploadSuffix(uploadState)}${sealSuffix(seal)}`}
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
        <UploadStateIcon state={uploadState} hash={item.remoteFile?.sha256} />
        {seal && <SealBadge seal={seal} />}
        {uploadState && VISIBLE_STATE_TEXT[uploadState] && (
          <span aria-hidden="true" className="shrink-0 whitespace-nowrap text-[11px] font-medium text-fx-text-2">
            {VISIBLE_STATE_TEXT[uploadState]}
          </span>
        )}
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

/**
 * Tramo de la grabación de la sesión (grabacion-sesion-completa §9.4): fila del
 * grupo "Sesión" con "Pantalla · tramo N · HH:MM–HH:MM", estado de guardado y
 * sello. Sin botón de quitar (D-T6): los tramos son el registro de la recolección.
 */
export function SessionSegmentTile({
  item, active, label, uploadState, seal, onSelect,
}: {
  item: GItem; active: boolean;
  /** "Pantalla · tramo 2 · 14:02–14:32". */
  label: string;
  uploadState?: TrayUploadState;
  seal?: CaseTimestamp | null;
  onSelect: () => void;
}) {
  return (
    <li className="motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={active}
        aria-label={`Ver ${label} — ${item.name}${uploadSuffix(uploadState)}${sealSuffix(seal)}`}
        title={item.name}
        className={cn(
          "flex min-h-11 w-full items-center gap-2 rounded-fx-md border bg-fx-surface-2 px-2.5 py-1.5 text-left text-xs",
          "transition-colors duration-fx-fast ease-fx",
          FOCUS_RING,
          active ? "border-fx-accent" : "border-fx-border hover:border-fx-border-strong",
        )}
      >
        <Film className="h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
        <span className="min-w-0 flex-1 break-words font-medium leading-snug text-fx-text">{label}</span>
        <UploadStateIcon state={uploadState} hash={item.remoteFile?.sha256} />
        {seal && <SealBadge seal={seal} />}
        {uploadState && VISIBLE_STATE_TEXT[uploadState] && (
          <span aria-hidden="true" className="shrink-0 whitespace-nowrap text-[11px] font-medium text-fx-text-2">
            {VISIBLE_STATE_TEXT[uploadState]}
          </span>
        )}
      </button>
    </li>
  );
}
