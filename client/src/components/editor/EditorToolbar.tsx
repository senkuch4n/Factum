"use client";

import { useMemo, useRef, useState, type KeyboardEvent, type ReactNode, type SyntheticEvent } from "react";
import type { Editor } from "@tiptap/core";
import { useEditorState } from "@tiptap/react";
import { Button } from "primereact/button";
import { Menu } from "primereact/menu";
import type { MenuItem } from "primereact/menuitem";
import {
  Bold, Check, CircleHelp, Code, Heading3, Italic, Link2, List, ListOrdered, MoreHorizontal, Quote,
  Redo2, SquareCode, Underline, Undo2,
} from "lucide-react";
import { FxTip } from "@/components/overlay/FxTip";
import { cn } from "@/lib/utils";

export interface Shortcut {
  /** Para mostrar: "Ctrl+B" / "⌘B". */
  label: string;
  /** Para `aria-keyshortcuts`: "Control+B" / "Meta+B". */
  aria: string;
}

/** Atajos según la plataforma, a partir de la forma canónica "Mod+Shift+8". */
export function useShortcuts() {
  return useMemo(() => {
    const mac = typeof navigator !== "undefined" && /Mac|iPhone|iPad|iPod/i.test(navigator.userAgent);
    return (keys: string): Shortcut => ({
      label: mac
        ? keys.replace(/Mod\+/g, "⌘").replace(/Alt\+/g, "⌥").replace(/Shift\+/g, "⇧")
        : keys.replace(/Mod\+/g, "Ctrl+"),
      aria: keys.replace(/Mod\+/g, mac ? "Meta+" : "Control+"),
    });
  }, []);
}

interface Props {
  editor: Editor;
  /** Nombre de la sección, para el `aria-label` de la barra. */
  sectionLabel: string;
  /** id del elemento editable (`aria-controls`). */
  controlsId: string;
  onLink: () => void;
  onHelp: () => void;
}

/** Estado activo de cada formato. `useEditorState` evita re-renderizar todo el editor. */
function useActiveFormats(editor: Editor) {
  return useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      bold: e.isActive("bold"),
      italic: e.isActive("italic"),
      underline: e.isActive("underline"),
      bulletList: e.isActive("bulletList"),
      orderedList: e.isActive("orderedList"),
      heading: e.isActive("heading"),
      blockquote: e.isActive("blockquote"),
      code: e.isActive("code"),
      codeBlock: e.isActive("codeBlock"),
      link: e.isActive("link"),
      canUndo: e.can().undo(),
      canRedo: e.can().redo(),
    }),
  });
}

const ICON = "h-4 w-4";

/**
 * Barra de formato del editor del informe (SDD §7.5).
 *
 * - `role="toolbar"` con roving tabindex: un solo botón en el orden de
 *   tabulación; flechas izquierda/derecha, Inicio y Fin recorren el resto.
 * - Botones de estado con `aria-pressed`; tooltip con el atajo de la plataforma.
 * - En pantallas angostas (< sm), "Bloques", "Enlace" y "Formatos disponibles" van al
 *   menú "Más": así la barra entra en una sola fila en 360 px.
 * - El `mousedown` no le saca el foco al editor: la selección queda donde estaba.
 */
export function EditorToolbar({ editor, sectionLabel, controlsId, onLink, onHelp }: Props) {
  const s = useActiveFormats(editor);
  const sc = useShortcuts();
  const barRef = useRef<HTMLDivElement>(null);
  const moreRef = useRef<Menu>(null);
  const [stop, setStop] = useState("bold");
  const run = () => editor.chain().focus();

  // Si la parada guardada quedó deshabilitada (Deshacer sin historial), vuelve al primero.
  const disabledKeys = new Set([!s.canUndo && "undo", !s.canRedo && "redo"].filter(Boolean));
  const tabStop = disabledKeys.has(stop) ? "bold" : stop;

  /** Botones operables en orden visual (los ocultos por breakpoint no cuentan). */
  function operable(): HTMLButtonElement[] {
    return Array.from(barRef.current?.querySelectorAll<HTMLButtonElement>("[data-fx-tool]") ?? [])
      .filter(b => !b.disabled && b.offsetParent !== null);
  }

  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    const list = operable();
    const idx = list.indexOf(document.activeElement as HTMLButtonElement);
    if (idx < 0) return;
    const next =
      e.key === "ArrowRight" ? (idx + 1) % list.length
      : e.key === "ArrowLeft" ? (idx - 1 + list.length) % list.length
      : e.key === "Home" ? 0
      : e.key === "End" ? list.length - 1
      : -1;
    if (next < 0) return;
    e.preventDefault();
    setStop(list[next].dataset.fxTool ?? "bold");
    list[next].focus();
  }

  const tool = (
    key: string, label: string, icon: ReactNode,
    onClick: (e: SyntheticEvent) => void,
    opts: { shortcut?: string; pressed?: boolean; disabled?: boolean; className?: string } = {},
  ) => (
    <ToolButton
      key={key}
      toolKey={key}
      label={label}
      icon={icon}
      onClick={onClick}
      tabIndex={tabStop === key ? 0 : -1}
      onFocus={() => setStop(key)}
      shortcut={opts.shortcut ? sc(opts.shortcut) : undefined}
      pressed={opts.pressed}
      disabled={opts.disabled}
      className={opts.className}
    />
  );

  // El <li> de Prime toma el nombre accesible de `label` (con "activo" si corresponde);
  // el template muestra el texto y un check visual. El clic lo maneja el <li>.
  const menuItem = (text: string, icon: ReactNode, active: boolean, command: () => void): MenuItem => ({
    label: active ? `${text} (activo)` : text,
    command,
    template: () => (
      <div className="flex cursor-pointer select-none items-center gap-2.5 px-3.5 py-2.5 text-fx-body-sm font-medium">
        {icon}
        <span className="min-w-0 flex-1 truncate">{text}</span>
        {active && <Check className="h-3.5 w-3.5 shrink-0 text-fx-accent-text" aria-hidden="true" />}
      </div>
    ),
  });
  const moreItems: MenuItem[] = [
    menuItem("Subtítulo", <Heading3 className={ICON} aria-hidden="true" />, s.heading, () => run().toggleHeading({ level: 3 }).run()),
    menuItem("Cita", <Quote className={ICON} aria-hidden="true" />, s.blockquote, () => run().toggleBlockquote().run()),
    menuItem("Código en línea", <Code className={ICON} aria-hidden="true" />, s.code, () => run().toggleCode().run()),
    menuItem("Bloque de código", <SquareCode className={ICON} aria-hidden="true" />, s.codeBlock, () => run().toggleCodeBlock().run()),
    { separator: true },
    { label: s.link ? "Editar enlace" : "Enlace", icon: <Link2 className={ICON} aria-hidden="true" />, command: () => onLink() },
    { separator: true },
    { label: "Formatos disponibles", icon: <CircleHelp className={ICON} aria-hidden="true" />, command: () => onHelp() },
  ];

  const wide = "hidden sm:inline-flex";

  return (
    <div
      ref={barRef}
      role="toolbar"
      aria-label={`Formato de ${sectionLabel}`}
      aria-controls={controlsId}
      onKeyDown={onKeyDown}
      className="sticky top-0 z-10 flex flex-wrap items-center gap-0 rounded-t-fx-md border-b border-fx-border bg-fx-surface-2 px-1 py-1 sm:gap-0.5 sm:px-1.5"
    >
      {tool("bold", "Negrita", <Bold className={ICON} aria-hidden="true" />, () => run().toggleBold().run(), { shortcut: "Mod+B", pressed: s.bold })}
      {tool("italic", "Cursiva", <Italic className={ICON} aria-hidden="true" />, () => run().toggleItalic().run(), { shortcut: "Mod+I", pressed: s.italic })}
      {tool("underline", "Subrayado", <Underline className={ICON} aria-hidden="true" />, () => run().toggleUnderline().run(), { shortcut: "Mod+U", pressed: s.underline })}
      <Separator />
      {tool("bullet", "Lista con viñetas", <List className={ICON} aria-hidden="true" />, () => run().toggleBulletList().run(), { shortcut: "Mod+Shift+8", pressed: s.bulletList })}
      {tool("ordered", "Lista numerada", <ListOrdered className={ICON} aria-hidden="true" />, () => run().toggleOrderedList().run(), { shortcut: "Mod+Shift+7", pressed: s.orderedList })}

      <Separator className="hidden sm:block" />
      {tool("heading", "Subtítulo", <Heading3 className={ICON} aria-hidden="true" />, () => run().toggleHeading({ level: 3 }).run(), { shortcut: "Mod+Alt+3", pressed: s.heading, className: wide })}
      {tool("quote", "Cita", <Quote className={ICON} aria-hidden="true" />, () => run().toggleBlockquote().run(), { shortcut: "Mod+Shift+B", pressed: s.blockquote, className: wide })}
      {tool("code", "Código en línea", <Code className={ICON} aria-hidden="true" />, () => run().toggleCode().run(), { shortcut: "Mod+E", pressed: s.code, className: wide })}
      {tool("codeBlock", "Bloque de código", <SquareCode className={ICON} aria-hidden="true" />, () => run().toggleCodeBlock().run(), { shortcut: "Mod+Alt+C", pressed: s.codeBlock, className: wide })}
      <Separator className="hidden sm:block" />
      {tool("link", s.link ? "Editar enlace" : "Enlace", <Link2 className={ICON} aria-hidden="true" />, () => onLink(), { shortcut: "Mod+K", pressed: s.link, className: wide })}

      <Separator />
      {tool("undo", "Deshacer", <Undo2 className={ICON} aria-hidden="true" />, () => run().undo().run(), { shortcut: "Mod+Z", disabled: !s.canUndo })}
      {tool("redo", "Rehacer", <Redo2 className={ICON} aria-hidden="true" />, () => run().redo().run(), { shortcut: "Mod+Shift+Z", disabled: !s.canRedo })}

      <span className="ml-auto flex items-center gap-0 sm:gap-0.5">
        {tool("more", "Más formatos", <MoreHorizontal className={ICON} aria-hidden="true" />, e => moreRef.current?.toggle(e), { className: "sm:hidden" })}
        {tool("help", "Formatos disponibles", <CircleHelp className={ICON} aria-hidden="true" />, () => onHelp(), { className: wide })}
      </span>
      <Menu ref={moreRef} model={moreItems} popup popupAlignment="right" pt={{ menu: { "aria-label": "Más formatos" } }} />
    </div>
  );
}

function Separator({ className }: { className?: string }) {
  return <span aria-hidden="true" className={cn("mx-0.5 h-5 w-px shrink-0 bg-fx-border sm:mx-1", className)} />;
}

function ToolButton({
  toolKey, label, shortcut, icon, pressed, disabled, tabIndex, onClick, onFocus, className,
}: {
  toolKey: string; label: string; shortcut?: Shortcut; icon: ReactNode; pressed?: boolean; disabled?: boolean;
  tabIndex: number; onClick: (e: SyntheticEvent) => void; onFocus: () => void; className?: string;
}) {
  return (
    <FxTip label={shortcut ? `${label} · ${shortcut.label}` : label} side="top">
      <Button
        type="button"
        text
        severity="secondary"
        size="small"
        icon={icon}
        aria-label={label}
        aria-pressed={pressed}
        aria-keyshortcuts={shortcut?.aria}
        disabled={disabled}
        tabIndex={tabIndex}
        data-fx-tool={toolKey}
        onMouseDown={e => e.preventDefault()}
        onFocus={onFocus}
        onClick={onClick}
        pt={{
          root: {
            className: cn(
              pressed && "bg-fx-accent-soft text-fx-accent-text enabled:hover:bg-fx-accent-soft enabled:hover:text-fx-accent-text",
              className,
            ),
          },
        }}
      />
    </FxTip>
  );
}
