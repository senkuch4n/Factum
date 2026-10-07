"use client";

import { useId, useState } from "react";
import type { Editor } from "@tiptap/core";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { AlertCircle, Link2 } from "lucide-react";
import { isAllowedUrl } from "@/lib/report-markdown";

/** Rango y valores del enlace que se está editando (los toma el editor al abrir). */
export interface LinkTarget {
  from: number;
  to: number;
  text: string;
  href: string;
  /** true si el cursor estaba sobre un enlace existente. */
  existing: boolean;
}

/** Lee el enlace bajo el cursor (o la selección) para abrir el diálogo. */
export function readLinkTarget(editor: Editor): LinkTarget {
  const existing = editor.isActive("link");
  if (existing) editor.chain().extendMarkRange("link").run();
  const { from, to } = editor.state.selection;
  const href = existing ? String(editor.getAttributes("link").href ?? "") : "";
  return { from, to, text: editor.state.doc.textBetween(from, to, " "), href, existing };
}

/**
 * Completa lo que el perito escribió: "ejemplo.com" → "https://ejemplo.com",
 * "a@b.com" → "mailto:a@b.com". Devuelve `null` si no es http(s) ni mailto.
 */
export function normalizeUrl(raw: string): string | null {
  const url = raw.trim();
  if (!url) return null;
  if (isAllowedUrl(url)) return url;
  if (/^[^\s@/:]+@[^\s@/:]+\.[^\s@/:]+$/.test(url)) return `mailto:${url}`;
  if (!/^[a-z][a-z0-9+.-]*:/i.test(url) && /^[^\s/@]+\.[a-z]{2,}(?:[/?#:].*)?$/i.test(url)) return `https://${url}`;
  return null;
}

interface Props {
  editor: Editor;
  target: LinkTarget | null;
  onClose: () => void;
}

/**
 * Diálogo chico para agregar, editar o quitar un enlace (SDD §7.5). Solo
 * http(s) y mailto (D7). Esc cierra y el foco vuelve al editor.
 */
export function LinkPopover({ editor, target, onClose }: Props) {
  const uid = useId().replace(/:/g, "");
  const [text, setText] = useState("");
  const [url, setUrl] = useState("");
  const [error, setError] = useState("");

  function onShow() {
    setText(target?.text ?? "");
    setUrl(target?.href ?? "");
    setError("");
    document.getElementById(`${uid}-url`)?.focus();
  }

  function close() {
    onClose();
    // Devuelve el foco al editor, con la selección donde estaba.
    queueMicrotask(() => editor.commands.focus());
  }

  function apply() {
    if (!target) return;
    const href = normalizeUrl(url);
    if (!href) {
      setError("Usá una dirección http(s):// o mailto:");
      document.getElementById(`${uid}-url`)?.focus();
      return;
    }
    const range = { from: target.from, to: target.to };
    if (target.from !== target.to && text === target.text) {
      // Mismo texto: solo se aplica el enlace y se conservan negritas, cursivas, etc.
      editor.chain().focus().setTextSelection(range).setLink({ href }).run();
      onClose();
      return;
    }
    const label = text.trim() ? text : href.replace(/^mailto:/i, "");
    editor
      .chain()
      .focus()
      .insertContentAt(
        range,
        { type: "text", text: label, marks: [{ type: "link", attrs: { href } }] },
      )
      .run();
    onClose();
  }

  function remove() {
    if (!target) return;
    editor.chain().focus().setTextSelection({ from: target.from, to: target.to }).unsetLink().run();
    onClose();
  }

  return (
    <Dialog
      visible={target !== null}
      onHide={close}
      onShow={onShow}
      modal
      dismissableMask
      closeOnEscape
      draggable={false}
      resizable={false}
      header={
        <span className="flex items-center gap-2 text-fx-body font-bold">
          <Link2 className="h-4 w-4 text-fx-text-3" aria-hidden="true" />
          {target?.existing ? "Editar enlace" : "Agregar enlace"}
        </span>
      }
      pt={{ root: { className: "w-[min(26rem,100%)]" } }}
      footer={
        <>
          {target?.existing && (
            <Button type="button" text severity="danger" label="Quitar enlace" onClick={remove} className="mr-auto" />
          )}
          <Button type="button" severity="secondary" label="Cancelar" onClick={close} />
          <Button type="submit" form={`${uid}-form`} label="Aplicar" />
        </>
      }
    >
      <form
        id={`${uid}-form`}
        noValidate
        className="space-y-4"
        onSubmit={e => { e.preventDefault(); apply(); }}
      >
        <div>
          <label htmlFor={`${uid}-text`} className="mb-1.5 block text-fx-label uppercase text-fx-text-2">Texto</label>
          <InputText
            id={`${uid}-text`}
            name="link-text"
            value={text}
            onChange={e => setText(e.target.value)}
            placeholder="Ej.: Sitio del organismo…"
            autoComplete="off"
          />
        </div>
        <div>
          <label htmlFor={`${uid}-url`} className="mb-1.5 block text-fx-label uppercase text-fx-text-2">URL</label>
          <InputText
            id={`${uid}-url`}
            name="link-url"
            type="url"
            inputMode="url"
            value={url}
            onChange={e => { setUrl(e.target.value); if (error) setError(""); }}
            placeholder="https://… o mailto:…"
            autoComplete="off"
            spellCheck={false}
            invalid={!!error}
            aria-invalid={!!error || undefined}
            aria-describedby={error ? `${uid}-error` : `${uid}-hint`}
          />
          {error ? (
            <p id={`${uid}-error`} role="alert" className="m-0 mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-danger">
              <AlertCircle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {error}
            </p>
          ) : (
            <p id={`${uid}-hint`} className="m-0 mt-1.5 text-xs text-fx-text-3">
              En el informe impreso la dirección se agrega entre paréntesis.
            </p>
          )}
        </div>
      </form>
    </Dialog>
  );
}
