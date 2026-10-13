"use client";

import { Button } from "primereact/button";
import { Check, History, Undo2, X } from "lucide-react";
import { formatDate } from "@/lib/format";
import type { Case } from "@/types";

export const PREFILL_CHANGE_ID = "case-prefill-change";
export const PREFILL_BLANK_ID = "case-prefill-blank";
export const PREFILL_UNDO_ID = "case-prefill-undo";

const ACTION = "h-8 px-2.5 [@media(pointer:coarse)]:min-h-11";

function abbreviate(s: string, max = 60): string {
  const t = s.trim();
  return t.length > max ? t.slice(0, max).trimEnd() + "…" : t;
}

/** "la causa 1234/2026" o "tu causa del 12 oct 2026" (sin expediente). */
export function causaLabel(cas: Case): string {
  const nro = cas.nro_referencia?.trim();
  return nro ? `la causa ${nro}` : `tu causa del ${formatDate(cas.created_at)}`;
}

interface Props {
  /** Causa de origen activa; null = "Empezaste en blanco". */
  source: Case | null;
  /** Hay snapshot para "Deshacer". */
  canUndo: boolean;
  /** Hay otras causas para elegir como origen (si no, no se ofrece el buscador). */
  canChoose: boolean;
  /** "Empezaste en blanco": cuántos campos se vaciaron y de qué causa venían. */
  blank?: { count: number; from: Case | null } | null;
  onChangeSource: () => void;
  onStartBlank: () => void;
  onKeep: () => void;
  onUndo: () => void;
  onDismiss: () => void;
}

/**
 * Aviso de la precarga visible (formulario-causa-rediseno §6.10). Compacto y
 * neutro (reintento 1): superficie 2 + borde sutil, marca en el acento (la
 * misma que llevan los campos precargados) y acciones como botones ghost
 * chicos a la derecha. Todos los botones son `type="button"`: el aviso vive
 * dentro del `<form>`. `role="status"`: anuncia los cambios (en blanco / deshacer).
 */
export function PrefillNotice({ source, canUndo, canChoose, blank, onChangeSource, onStartBlank, onKeep, onUndo, onDismiss }: Props) {
  const undoButton = canUndo && (
    <Button
      id={PREFILL_UNDO_ID}
      type="button" text severity="secondary" size="small"
      icon={<Undo2 className="h-3.5 w-3.5" aria-hidden="true" />}
      label="Deshacer"
      onClick={onUndo}
      className={ACTION}
    />
  );

  const caratula = source?.caratula?.trim();
  const nro = source?.nro_referencia?.trim();

  return (
    <div
      role="status"
      className="flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-fx-md border border-fx-border bg-fx-surface-2 py-1.5 pl-3 pr-1.5 text-fx-body-sm motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
    >
      <History className="h-4 w-4 shrink-0 text-fx-accent-text" aria-hidden="true" />
      {source ? (
        <p className="m-0 min-w-[14rem] flex-1 py-1 text-fx-text-2" title={caratula ? abbreviate(caratula, 120) : undefined}>
          Precargado de{" "}
          {nro ? (
            <>la causa <span className="font-medium text-fx-text" translate="no">{nro}</span></>
          ) : (
            <>tu causa del <span className="font-medium text-fx-text">{formatDate(source.created_at)}</span></>
          )}
          <span className="text-fx-text-3">
            . Revisá los campos marcados con{" "}
            <History className="inline h-3 w-3 align-[-1px] text-fx-accent-text" aria-hidden="true" />
            <span className="sr-only">el ícono de historial</span>.
          </span>
        </p>
      ) : (
        <p className="m-0 min-w-[14rem] flex-1 py-1 text-fx-text-2">
          Empezaste en blanco: se vaciaron {blank?.count ?? 0} {blank?.count === 1 ? "campo" : "campos"}
          {blank?.from ? ` de ${causaLabel(blank.from)}` : ""}.
        </p>
      )}
      <div className="ml-auto flex flex-wrap items-center gap-0.5">
        {source ? (
          <>
            {canChoose && (
              <Button id={PREFILL_CHANGE_ID} type="button" text severity="secondary" size="small" label="Cambiar origen" aria-label="Cambiar causa de origen" aria-haspopup="dialog" onClick={onChangeSource} className={ACTION} />
            )}
            <Button id={PREFILL_BLANK_ID} type="button" text severity="secondary" size="small" label="En blanco" aria-label="Empezar en blanco" onClick={onStartBlank} className={ACTION} />
            {undoButton}
            <Button
              type="button" text size="small"
              icon={<Check className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Mantener" aria-label="Mantener los datos precargados" onClick={onKeep} className={ACTION}
            />
          </>
        ) : (
          <>
            {undoButton}
            {canChoose && (
              <Button id={PREFILL_CHANGE_ID} type="button" text severity="secondary" size="small" label="Elegir origen" aria-label="Elegir causa de origen" aria-haspopup="dialog" onClick={onChangeSource} className={ACTION} />
            )}
            <button
              type="button"
              onClick={onDismiss}
              aria-label="Cerrar aviso"
              className="inline-flex h-8 w-8 shrink-0 cursor-pointer items-center justify-center rounded-fx-md border-0 bg-transparent p-0 text-fx-text-3 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring [@media(pointer:coarse)]:h-11 [@media(pointer:coarse)]:w-11"
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          </>
        )}
      </div>
    </div>
  );
}
