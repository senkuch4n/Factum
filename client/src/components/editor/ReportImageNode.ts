/**
 * Nodo `reportImage`: una captura del caso dentro de un texto del informe
 * (editor-imagenes-informe, SDD §7.2).
 *
 * - Está en el grupo `figure`, que solo admite el documento (`FxDocument`):
 *   el esquema impide que entre en una lista, una cita o un párrafo (§4.1).
 * - En el Markdown es `![alt](captura:<nombre-codificado>)` como bloque propio.
 * - En HTML es `<figure data-fx-report-image>` sin `<img>`: copiar y pegar
 *   dentro del editor mantiene la referencia y nunca se renderiza una URL.
 */

import { Extension, Node, type Editor } from "@tiptap/core";
import { NodeSelection, Plugin, PluginKey } from "@tiptap/pm/state";
import type { Node as PMNode } from "@tiptap/pm/model";
import { ReactNodeViewRenderer } from "@tiptap/react";
import {
  MAX_REPORT_IMAGES_PER_SECTION, REPORT_IMAGE_LINE, decodeReportImageName, formatReportImageMarkdown,
  normalizeReportImageAlt, unescapeReportImageAlt,
} from "@/lib/report-markdown";
import { ReportImageView } from "./ReportImageView";

export interface ReportImageAttrs {
  filename: string;
  alt: string;
}

declare module "@tiptap/core" {
  interface Commands<ReturnType> {
    reportImage: {
      /** Inserta la captura después del bloque de nivel superior de la selección. */
      insertReportImage: (attrs: ReportImageAttrs) => ReturnType;
      /** Cambia la descripción de la imagen que está en `pos`. */
      updateReportImageAlt: (pos: number, alt: string) => ReturnType;
    };
  }
}

export interface ReportImageOptions {
  /** Enter sobre una imagen seleccionada: abrir "Editar descripción". */
  onRequestEdit: (editor: Editor, pos: number) => void;
  /** Se intentó pasar el tope de imágenes por sección. */
  onLimit: () => void;
}

/** Imágenes del documento (solo pueden estar en el primer nivel). */
export function countReportImages(doc: PMNode): number {
  let n = 0;
  doc.forEach(child => { if (child.type.name === "reportImage") n++; });
  return n;
}

/**
 * Tokenizer de bloque (§4.8 paso 3). Solo reconoce la línea si:
 * - está en el nivel superior: el arreglo de tokens de primer nivel de marked
 *   es el único que tiene `links` (los de listas y citas son arreglos nuevos);
 * - la línea anterior está vacía (o es el inicio) y la siguiente también (o es el fin).
 * Cualquier otro `![…](…)` queda en un párrafo y `InlineImageAsText` lo deja como texto.
 */
function tokenizeReportImage(src: string, tokens: unknown[]) {
  if (!tokens || !("links" in tokens)) return undefined;
  const prev = tokens[tokens.length - 1] as { type?: string; raw?: string } | undefined;
  if (prev && prev.type !== "space" && !(prev.raw ?? "").endsWith("\n\n")) return undefined;
  const lineEnd = src.indexOf("\n");
  const line = lineEnd < 0 ? src : src.slice(0, lineEnd);
  const m = REPORT_IMAGE_LINE.exec(line);
  if (!m) return undefined;
  const rest = lineEnd < 0 ? "" : src.slice(lineEnd + 1);
  if (rest !== "" && !/^[ \t]*(\n|$)/.test(rest)) return undefined;
  const filename = decodeReportImageName(m[2]);
  if (filename === null) return undefined;
  return {
    type: "reportImage",
    raw: lineEnd < 0 ? line : line + "\n",
    filename,
    alt: unescapeReportImageAlt(m[1]),
  };
}

export const ReportImage = Node.create<ReportImageOptions>({
  name: "reportImage",
  group: "figure",
  atom: true,
  selectable: true,
  draggable: false,
  isolating: true,

  addOptions() {
    return { onRequestEdit: () => {}, onLimit: () => {} };
  },

  addAttributes() {
    return {
      filename: { default: "" },
      alt: { default: "" },
    };
  },

  parseHTML() {
    return [
      {
        tag: "figure[data-fx-report-image]",
        getAttrs: el => {
          const filename = (el as HTMLElement).getAttribute("data-fx-report-image") ?? "";
          return filename ? { filename, alt: normalizeReportImageAlt((el as HTMLElement).getAttribute("data-alt") ?? "") } : false;
        },
      },
    ];
  },

  renderHTML({ node }) {
    return ["figure", { "data-fx-report-image": node.attrs.filename, "data-alt": node.attrs.alt }];
  },

  addNodeView() {
    return ReactNodeViewRenderer(ReportImageView);
  },

  renderMarkdown: node => formatReportImageMarkdown(String(node.attrs?.filename ?? ""), String(node.attrs?.alt ?? "")),

  markdownTokenizer: {
    name: "reportImage",
    level: "block",
    start: (src: string) => {
      const m = /(^|\n)!\[/.exec(src);
      return m ? m.index + m[1].length : -1;
    },
    tokenize: (src, tokens) => tokenizeReportImage(src, tokens),
  },

  parseMarkdown: token => ({
    type: "reportImage",
    attrs: { filename: token.filename, alt: token.alt ?? "" },
  }),

  addCommands() {
    return {
      insertReportImage: ({ filename, alt }) => ({ state, tr, dispatch }) => {
        const type = state.schema.nodes.reportImage;
        if (!type || !filename) return false;
        const node = type.create({ filename, alt: normalizeReportImageAlt(alt) });
        const { selection } = state;
        const { $from } = selection;

        let from: number;
        let to: number;
        if ($from.depth === 0) {
          // Nodo de primer nivel seleccionado (otra imagen) o cursor entre bloques.
          from = to = selection instanceof NodeSelection ? selection.to : $from.pos;
        } else {
          const top = $from.node(1);
          const isEmptyParagraph = top.type.name === "paragraph" && top.content.size === 0;
          from = isEmptyParagraph ? $from.before(1) : $from.after(1);
          to = isEmptyParagraph ? $from.after(1) : from;
        }
        if (!dispatch) return true;
        tr.replaceWith(from, to, node);
        const after = from + node.nodeSize;
        // Si la imagen queda última, un párrafo vacío después para seguir escribiendo.
        if (after >= tr.doc.content.size) tr.insert(after, state.schema.nodes.paragraph.create());
        tr.setSelection(NodeSelection.create(tr.doc, from)).scrollIntoView();
        return true;
      },

      updateReportImageAlt: (pos, alt) => ({ tr, dispatch }) => {
        const node = tr.doc.nodeAt(pos);
        if (!node || node.type.name !== "reportImage") return false;
        if (dispatch) {
          tr.setNodeMarkup(pos, undefined, { ...node.attrs, alt: normalizeReportImageAlt(alt) });
          tr.setSelection(NodeSelection.create(tr.doc, pos));
        }
        return true;
      },
    };
  },

  addKeyboardShortcuts() {
    return {
      Enter: ({ editor }) => {
        const sel = editor.state.selection;
        if (sel instanceof NodeSelection && sel.node.type.name === this.name) {
          this.options.onRequestEdit(editor, sel.from);
          return true;
        }
        return false;
      },
    };
  },

  addProseMirrorPlugins() {
    const { onLimit } = this.options;
    return [
      new Plugin({
        key: new PluginKey("fxReportImageLimit"),
        // Tope de 20 por sección (D4): se rechaza la transacción que lo pase.
        // Borrar o editar estando por encima (no debería pasar) siempre se deja.
        filterTransaction: (tr, state) => {
          if (!tr.docChanged) return true;
          const next = countReportImages(tr.doc);
          if (next <= MAX_REPORT_IMAGES_PER_SECTION || next <= countReportImages(state.doc)) return true;
          if (!tr.getMeta("preventUpdate")) onLimit();
          return false;
        },
      }),
    ];
  },
});

/**
 * Defensa (§7.2): un `![…](…)` que no es bloque propio (en una línea con
 * texto, en una lista o con otro esquema) llega como token inline `image`.
 * Se convierte en texto con su fuente tal cual: no se pierde nada y nunca se
 * crea un nodo.
 */
export const InlineImageAsText = Extension.create({
  name: "fxInlineImageAsText",
  markdownTokenName: "image",
  parseMarkdown: token => ({ type: "text", text: String(token.raw ?? "") }),
});

