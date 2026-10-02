/**
 * Extensiones de Tiptap del editor del informe (editor-texto-enriquecido, SDD §7.3).
 *
 * El editor escribe el "dialecto Factum" (§4.3): CommonMark sin GFM + `<u>`.
 * Los overrides de acá son los que hacen que `getMarkdown()` produzca
 * exactamente ese dialecto y que el Markdown que llega por API se lea igual
 * que lo lee el servidor (Markdig).
 *
 * Punto de extensión para imágenes (D19, HU `editor-imagenes-informe`): hoy no
 * hay nodo `image` en el esquema, así que un `![…](…)` que llegue se lee como
 * texto. La HU de imágenes agrega acá el nodo y su botón en la barra.
 */

import { Extension, type AnyExtension, type Editor, type JSONContent } from "@tiptap/core";
import { Plugin, PluginKey } from "@tiptap/pm/state";
import StarterKit from "@tiptap/starter-kit";
import { Paragraph } from "@tiptap/extension-paragraph";
import { Heading } from "@tiptap/extension-heading";
import { CodeBlock } from "@tiptap/extension-code-block";
import { Underline } from "@tiptap/extension-underline";
import { Link } from "@tiptap/extension-link";
import { OrderedList } from "@tiptap/extension-list";
import { Placeholder } from "@tiptap/extensions";
import { Markdown } from "@tiptap/markdown";
import {
  EMPTY_PARAGRAPH, encodeUrlForMarkdown, escapeLineStart, isAllowedUrl, normalizeMarkdown,
} from "@/lib/report-markdown";

/** Profundidad máxima de las listas (D3: hasta 3 niveles). */
export const MAX_LIST_DEPTH = 3;

const contentOf = (node: JSONContent | undefined): JSONContent[] =>
  Array.isArray(node?.content) ? node.content : [];

/**
 * Párrafo: todo párrafo vacío es `&nbsp;` (D9; el de Tiptap deja vacío el
 * primero de una racha) y cada línea pasa por el escape de inicio de línea
 * (§4.4 paso 3), así "1. x" escrito a mano no se vuelve una lista al releer.
 */
const FxParagraph = Paragraph.extend({
  renderMarkdown: (node, h) => {
    const content = contentOf(node);
    if (content.length === 0) return EMPTY_PARAGRAPH;
    return h.renderChildren(content).split("\n").map(escapeLineStart).join("\n");
  },
});

/**
 * Subtítulo: un solo nivel (`###`). Lo que llega con otro nivel se lee como
 * subtítulo igual (en el DOCX todos los niveles son subtítulo, D11). Si el
 * texto termina en ` #…`, se escapa ese `#`: si no, Markdig lo toma como cierre.
 */
const FxHeading = Heading.extend({
  parseMarkdown: (token, h) => h.createNode("heading", { level: 3 }, h.parseInline(token.tokens || [])),
  renderMarkdown: (node, h) => {
    const content = contentOf(node);
    if (content.length === 0) return "";
    const text = h.renderChildren(content).replace(/(^|\s)#(#*\s*)$/, "$1\\#$2");
    return `### ${text}`;
  },
}).configure({ levels: [3] });

/** Bloque de código: fence de backticks más largo que cualquier racha del contenido y sin lenguaje. */
const FxCodeBlock = CodeBlock.extend({
  renderMarkdown: (node, h) => {
    const content = contentOf(node);
    const text = content.length ? h.renderChildren(content) : "";
    const longest = Math.max(0, ...(text.match(/`+/g) ?? []).map(r => r.length));
    const fence = "`".repeat(Math.max(3, longest + 1));
    return text ? `${fence}\n${text}\n${fence}` : `${fence}\n${fence}`;
  },
});

/**
 * Subrayado como `<u>…</u>` (lo de adentro es Markdown). Reemplaza el `++x++`
 * de Tiptap: `++` vuelve a ser texto literal.
 */
const FxUnderline = Underline.extend({
  renderMarkdown: (node, h) => `<u>${h.renderChildren(node)}</u>`,
  markdownTokenizer: {
    name: "underline",
    level: "inline",
    start: (src: string) => src.indexOf("<u>"),
    tokenize: (src, _tokens, lexer) => {
      const m = /^<u>([\s\S]+?)<\/u>/.exec(src);
      return m ? { type: "underline", raw: m[0], text: m[1], tokens: lexer.inlineTokens(m[1]) } : undefined;
    },
  },
});

/**
 * Enlace: solo http, https y mailto (D7). Un `[t](javascript:…)` que llegue
 * por API queda como texto. Al escribir, la URL va percent-encoded y sin título.
 */
const FxLink = Link.extend({
  parseMarkdown: (token, h) => {
    const children = h.parseInline(token.tokens || []);
    return isAllowedUrl(token.href) ? h.applyMark("link", children, { href: token.href }) : children;
  },
  renderMarkdown: (node, h) => {
    const href = typeof node.attrs?.href === "string" ? node.attrs.href : "";
    return `[${h.renderChildren(node)}](${encodeUrlForMarkdown(href)})`;
  },
}).configure({
  openOnClick: false,
  autolink: true,
  linkOnPaste: true,
  defaultProtocol: "https",
  isAllowedUri: url => isAllowedUrl(url),
  HTMLAttributes: { rel: "noopener noreferrer nofollow", target: null },
});

/**
 * Numerada sin el atributo `type`: un `<ol type="i">` pegado se serializaría
 * como `i.`, que en CommonMark no es una lista. Queda solo `start`.
 */
const FxOrderedList = OrderedList.extend({
  addAttributes() {
    return {
      start: {
        default: 1,
        parseHTML: (el: HTMLElement) => (el.hasAttribute("start") ? parseInt(el.getAttribute("start") || "", 10) || 1 : 1),
      },
    };
  },
});

/** Tab dentro de un ítem que ya está en el tercer nivel: se consume sin anidar. */
const ListDepthLimit = Extension.create({
  name: "fxListDepthLimit",
  priority: 1000,
  addKeyboardShortcuts() {
    return {
      Tab: ({ editor }) => {
        const { $from } = editor.state.selection;
        let depth = 0;
        for (let d = $from.depth; d > 0; d--) if ($from.node(d).type.name === "listItem") depth++;
        return depth >= MAX_LIST_DEPTH;
      },
    };
  },
});

interface MaxLengthOptions {
  max: number;
  /** Largo del Markdown guardado en este momento (lo mantiene el componente). */
  getLength: () => number;
  onLimit: () => void;
}

/**
 * Tope de 20 000 sobre el Markdown guardado (D9 A). Deja pasar todo lo que no
 * alarga el texto (incluido borrar estando por encima del tope); si la
 * estimación se acerca al máximo, serializa y rechaza la transacción entera
 * (un pegado largo no entra a medias).
 */
const MaxMarkdownLength = Extension.create<MaxLengthOptions>({
  name: "fxMaxMarkdownLength",
  addOptions() {
    return { max: 20000, getLength: () => 0, onLimit: () => {} };
  },
  addProseMirrorPlugins() {
    const editor = this.editor as Editor;
    const { max, getLength, onLimit } = this.options;
    return [
      new Plugin({
        key: new PluginKey("fxMaxMarkdownLength"),
        filterTransaction: (tr, state) => {
          if (!tr.docChanged || tr.getMeta("preventUpdate")) return true;
          const current = getLength();
          const estimate = current + 4 * (tr.doc.content.size - state.doc.content.size) + 200;
          if (estimate <= max || !editor.markdown) return true;
          const next = normalizeMarkdown(editor.markdown.serialize(tr.doc.toJSON())).length;
          if (next <= max || next <= current) return true;
          onLimit();
          return false;
        },
      }),
    ];
  },
});

/** Ctrl/Cmd+K abre el popover de enlace (atajo propio, §7.5). */
const LinkShortcut = Extension.create<{ onRequest: () => void }>({
  name: "fxLinkShortcut",
  addOptions() {
    return { onRequest: () => {} };
  },
  addKeyboardShortcuts() {
    return {
      "Mod-k": () => {
        this.options.onRequest();
        return true;
      },
    };
  },
});

export interface ReportExtensionsOptions {
  placeholder?: string;
  maxLength: number;
  getLength: () => number;
  onLimit: () => void;
  onRequestLink: () => void;
}

/** Todas las extensiones del editor del informe. */
export function buildReportExtensions(opts: ReportExtensionsOptions): AnyExtension[] {
  return [
    StarterKit.configure({
      heading: false,
      codeBlock: false,
      paragraph: false,
      underline: false,
      link: false,
      orderedList: false,
      strike: false,
      horizontalRule: false,
      trailingNode: false,
    }),
    FxParagraph,
    FxHeading,
    FxCodeBlock,
    FxUnderline,
    FxLink,
    FxOrderedList,
    ListDepthLimit,
    MaxMarkdownLength.configure({ max: opts.maxLength, getLength: opts.getLength, onLimit: opts.onLimit }),
    LinkShortcut.configure({ onRequest: opts.onRequestLink }),
    Placeholder.configure({ placeholder: opts.placeholder ?? "" }),
    Markdown.configure({ markedOptions: { gfm: false }, indentation: { style: "space", size: 2 } }),
  ];
}
