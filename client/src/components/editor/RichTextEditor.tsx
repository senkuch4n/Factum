"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { Editor } from "@tiptap/core";
import { EditorContent, useEditor } from "@tiptap/react";
import { AlertCircle, Info } from "lucide-react";
import { MAX_REPORT_IMAGES_PER_SECTION, normalizeMarkdown } from "@/lib/report-markdown";
import { cn } from "@/lib/utils";
import { buildReportExtensions } from "./extensions";
import { EditorToolbar } from "./EditorToolbar";
import { countReportImages } from "./ReportImageNode";
import { LinkPopover, readLinkTarget, type LinkTarget } from "./LinkPopover";
import { FormatHelp } from "./FormatHelp";
import { hasImageFile, isExternalImageDrop, isFilesOnlyPaste, transformPastedHtml } from "./pasteTransform";

export interface RichTextEditorProps {
  /** id del elemento editable (`report-<clave>`): `getElementById(id).focus()` funciona. */
  id: string;
  /** id del título de la sección (`aria-labelledby`). */
  labelId: string;
  /** Nombre de la sección, para la barra ("Formato de Resultados"). */
  label: string;
  /** Markdown del dialecto Factum. */
  value: string;
  /** Recibe el Markdown ya normalizado; solo se llama si cambió de verdad. */
  onChange: (markdown: string) => void;
  onBlur?: () => void;
  required?: boolean;
  placeholder?: string;
  /** Tope del texto guardado (MAX_LEN_TEXT). */
  maxLength: number;
  /** Error de la sección (p. ej. texto viejo que no entra en el tope al convertirse). */
  error?: string;
  /**
   * Abre el selector de capturas (editor-imagenes-informe): sin `pos` para
   * insertar, con `pos` para editar la descripción de esa imagen. Sin esta
   * prop no hay botón "Imagen".
   */
  onRequestImage?: (req: { editor: Editor; pos?: number }) => void;
}

/** Aviso temporal debajo del editor (`aria-live`). */
type Notice = "formats" | "image" | "imageLimit";

const NOTICE_TEXT: Record<Notice, string> = {
  formats: "Se quitaron formatos que el informe no admite",
  image: "Para insertar una imagen usá el botón Imagen",
  imageLimit: `Máximo ${MAX_REPORT_IMAGES_PER_SECTION} imágenes por sección`,
};

const PASTE_NOTICE_MS = 6000;
const fmt = new Intl.NumberFormat("es-AR");

/**
 * Editor de texto enriquecido de una sección del informe (SDD §7.4).
 * Tiptap headless con la estética de los campos de Factum; guarda Markdown.
 * Se carga con `next/dynamic` y `ssr: false` desde `ReportStep`.
 */
export default function RichTextEditor({
  id, labelId, label, value, onChange, onBlur, required, placeholder, maxLength, error, onRequestImage,
}: RichTextEditorProps) {
  const lastValueRef = useRef(value);
  const onChangeRef = useRef(onChange);
  const onBlurRef = useRef(onBlur);
  const onRequestImageRef = useRef(onRequestImage);
  useEffect(() => {
    onChangeRef.current = onChange;
    onBlurRef.current = onBlur;
    onRequestImageRef.current = onRequestImage;
  }, [onChange, onBlur, onRequestImage]);

  const [linkTarget, setLinkTarget] = useState<LinkTarget | null>(null);
  const [helpOpen, setHelpOpen] = useState(false);
  const [limitHit, setLimitHit] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);
  const pasteTimer = useRef<number | null>(null);

  // Estable: solo usa setState y refs.
  const showNotice = useRef((kind: Notice) => {
    setNotice(kind);
    if (pasteTimer.current) window.clearTimeout(pasteTimer.current);
    pasteTimer.current = window.setTimeout(() => setNotice(null), PASTE_NOTICE_MS);
  });
  useEffect(() => () => { if (pasteTimer.current) window.clearTimeout(pasteTimer.current); }, []);

  const errorId = `${id}-error`;
  const statusId = `${id}-status`;
  const describedBy = [error ? errorId : null, limitHit ? statusId : null].filter(Boolean).join(" ") || undefined;

  // Las extensiones se arman una vez; lo que cambia se lee por refs.
  const editorRef = useRef<ReturnType<typeof useEditor>>(null);
  const extensions = useMemo(
    () =>
      buildReportExtensions({
        placeholder,
        maxLength,
        getLength: () => lastValueRef.current.length,
        onLimit: () => setLimitHit(true),
        onRequestLink: () => { if (editorRef.current) setLinkTarget(readLinkTarget(editorRef.current)); },
        onRequestImageEdit: (ed, pos) => onRequestImageRef.current?.({ editor: ed, pos }),
        onImageLimit: () => showNotice.current("imageLimit"),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  const editor = useEditor({
    immediatelyRender: false,
    extensions,
    content: value,
    contentType: "markdown",
    editorProps: {
      attributes: {
        id,
        role: "textbox",
        "aria-multiline": "true",
        "aria-labelledby": labelId,
        ...(required ? { "aria-required": "true" } : {}),
        class: "fx-rte",
        spellcheck: "true",
      },
      // Deja ver el cursor debajo de la barra sticky al desplazarse.
      scrollMargin: { top: 56, bottom: 24, left: 8, right: 8 },
      scrollThreshold: { top: 56, bottom: 24, left: 8, right: 8 },
      transformPastedHTML: html => {
        const res = transformPastedHtml(html);
        // Con una imagen en el HTML se pega el resto y el aviso es el del botón Imagen (D10).
        if (res.image) showNotice.current("image");
        else if (res.stripped) showNotice.current("formats");
        return res.html;
      },
      handlePaste: (_view, event) => {
        // Una captura copiada trae el archivo y a veces HTML/texto: no entra nada (D10).
        if (hasImageFile(event.clipboardData)) { showNotice.current("image"); return true; }
        if (isFilesOnlyPaste(event)) { showNotice.current("formats"); return true; }
        return false;
      },
      // Arrastrar una imagen o un archivo desde afuera: no entra nada (D10).
      // Mover contenido dentro del editor (`moved`) sigue funcionando.
      handleDrop: (_view, event, _slice, moved) => {
        if (moved || !isExternalImageDrop(event)) return false;
        event.preventDefault();
        showNotice.current("image");
        return true;
      },
    },
    onUpdate: ({ editor: e }) => {
      const md = normalizeMarkdown(e.getMarkdown());
      // Solo un cambio real del texto cuenta: así abrir un caso viejo no lo marca como editado.
      if (md === lastValueRef.current) return;
      if (md.length < lastValueRef.current.length) setLimitHit(false);
      lastValueRef.current = md;
      onChangeRef.current(md);
    },
    onBlur: () => onBlurRef.current?.(),
  });
  useEffect(() => { editorRef.current = editor; }, [editor]);

  // Cambio externo (Restaurar texto por defecto): se carga sin disparar onUpdate.
  useEffect(() => {
    if (!editor || value === lastValueRef.current) return;
    lastValueRef.current = value;
    editor.commands.setContent(value, { contentType: "markdown", emitUpdate: false });
    setLimitHit(false);
  }, [editor, value]);

  // El <label> de la sección apunta a un elemento que no es "labelable" (contenteditable):
  // el clic en el título enfoca el editor a mano, como en un textarea.
  useEffect(() => {
    const labelEl = document.getElementById(labelId);
    if (!editor || !labelEl) return;
    const focus = () => editor.commands.focus();
    labelEl.addEventListener("click", focus);
    return () => labelEl.removeEventListener("click", focus);
  }, [editor, labelId]);

  // aria-invalid / aria-describedby cambian con el estado: se reaplican al elemento editable.
  useEffect(() => {
    if (!editor) return;
    const el = editor.view.dom;
    if (error) el.setAttribute("aria-invalid", "true"); else el.removeAttribute("aria-invalid");
    if (describedBy) el.setAttribute("aria-describedby", describedBy); else el.removeAttribute("aria-describedby");
  }, [editor, error, describedBy]);

  const length = value.length;
  const atLimit = limitHit || length >= maxLength;
  const nearLimit = !atLimit && length >= maxLength * 0.9;

  return (
    <div>
      <div
        className={cn(
          "fx-rte-field rounded-fx-md border bg-fx-surface-2 transition-[border-color] duration-fx-fast ease-fx",
          error ? "border-fx-danger" : "border-fx-border-strong hover:border-fx-text-3",
        )}
      >
        {editor ? (
          <EditorToolbar
            editor={editor}
            sectionLabel={label}
            controlsId={id}
            onLink={() => setLinkTarget(readLinkTarget(editor))}
            onHelp={() => setHelpOpen(true)}
            onImage={onRequestImage ? () => {
              if (countReportImages(editor.state.doc) >= MAX_REPORT_IMAGES_PER_SECTION) showNotice.current("imageLimit");
              else onRequestImageRef.current?.({ editor });
            } : undefined}
          />
        ) : (
          <div aria-hidden="true" className="h-10 rounded-t-fx-md border-b border-fx-border" />
        )}
        <EditorContent editor={editor} />
      </div>

      <div className="mt-1.5 flex items-start gap-3">
        <div className="min-w-0 flex-1 space-y-1">
          {error && (
            <p id={errorId} role="alert" className="m-0 flex items-start gap-1 text-xs font-medium text-fx-danger">
              <AlertCircle className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {error}
            </p>
          )}
          <p id={statusId} aria-live="polite" className="m-0 text-xs">
            {atLimit ? (
              <span className="font-medium text-fx-danger">Llegaste al máximo de {fmt.format(maxLength)} caracteres</span>
            ) : notice ? (
              <span className="inline-flex items-center gap-1 text-fx-text-3">
                <Info className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {NOTICE_TEXT[notice]}
              </span>
            ) : null}
          </p>
        </div>
        <p
          className={cn(
            "m-0 shrink-0 text-xs tabular-nums",
            atLimit ? "font-medium text-fx-danger" : nearLimit ? "text-fx-warning" : "text-fx-text-3",
          )}
        >
          <span className="sr-only">Caracteres: </span>
          {fmt.format(length)} / {fmt.format(maxLength)}
        </p>
      </div>

      {editor && (
        <>
          <LinkPopover editor={editor} target={linkTarget} onClose={() => setLinkTarget(null)} />
          <FormatHelp open={helpOpen} onClose={() => { setHelpOpen(false); editor.commands.focus(); }} />
        </>
      )}
    </div>
  );
}
