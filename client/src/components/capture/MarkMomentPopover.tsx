"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { Bookmark, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";

interface Props {
  id: string;
  open: boolean;
  /** Tile "Marcar momento": el foco vuelve ahí al cerrar. */
  anchorRef: RefObject<HTMLButtonElement | null>;
  busy: boolean;
  onClose: () => void;
  /** `true` si la marca se guardó (cierra el popover). */
  onSave: (text: string) => Promise<boolean>;
  /**
   * Aclaración sobre dónde queda la marca (F2 [DU1=A]): con el cable desconectado y la
   * cámara grabando, la marca se ubica solo en el video de la cámara.
   */
  hint?: string | null;
}

/**
 * "Marcar momento" (grabacion-sesion-completa D5, §9.4): popover no modal
 * anclado al tile. Esc o un clic afuera lo cierran y el foco vuelve al tile.
 * Entra desde el tile (origen arriba, 150 ms) y solo con `motion-safe`.
 */
export function MarkMomentPopover({ id, open, anchorRef, busy, onClose, onSave, hint = null }: Props) {
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [text, setText] = useState("");

  useEffect(() => {
    if (!open) return;
    setText("");
    inputRef.current?.focus();
    const onDown = (e: PointerEvent) => {
      const t = e.target as Node;
      if (rootRef.current?.contains(t) || anchorRef.current?.contains(t)) return;
      onClose();
    };
    document.addEventListener("pointerdown", onDown);
    return () => document.removeEventListener("pointerdown", onDown);
  }, [open, anchorRef, onClose]);

  if (!open) return null;

  function close() {
    onClose();
    anchorRef.current?.focus();
  }

  async function submit() {
    if (busy) return;
    const ok = await onSave(text);
    if (ok) close();
  }

  return (
    <div
      ref={rootRef}
      id={id}
      role="group"
      aria-label="Marcar momento de la grabación de la sesión"
      onKeyDown={e => { if (e.key === "Escape") { e.stopPropagation(); close(); } }}
      className={cn(
        "rounded-fx-lg border border-fx-border-strong bg-fx-surface-1 p-3 shadow-fx-2",
        "origin-top motion-safe:animate-[fx-fade-in_150ms_var(--fx-ease-out)_both]",
      )}
    >
      <form
        onSubmit={e => { e.preventDefault(); void submit(); }}
        className="space-y-2"
      >
        <label htmlFor={`${id}-text`} className="block text-xs font-medium text-fx-text-2">
          Qué pasó (opcional)
        </label>
        <InputText
          ref={inputRef}
          id={`${id}-text`}
          name="session-mark-text"
          maxLength={300}
          value={text}
          onChange={e => setText(e.target.value)}
          autoComplete="off"
          aria-describedby={hint ? `${id}-hint` : undefined}
          className="w-full"
        />
        {hint && <p id={`${id}-hint`} className="m-0 text-xs text-fx-text-2">{hint}</p>}
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Button type="button" text severity="secondary" size="small" label="Cancelar" onClick={close}
            className="min-h-11 sm:min-h-0" />
          <Button
            type="submit"
            size="small"
            icon={busy
              ? <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />
              : <Bookmark className="h-3.5 w-3.5" aria-hidden="true" />}
            label="Guardar marca"
            aria-disabled={busy || undefined}
            className={cn("min-h-11 sm:min-h-0", busy && "cursor-not-allowed opacity-60")}
          />
        </div>
      </form>
    </div>
  );
}
