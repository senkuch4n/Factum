/**
 * Dialecto Markdown de Factum para los textos del informe
 * (editor-texto-enriquecido, SDD §4.3-4.5).
 *
 * Módulo puro, sin Tiptap: lo usan `pericial.ts` (chequeo de "vacío"),
 * `ReportStep` (conversión de texto plano viejo) y las extensiones del editor.
 * `plainToMarkdown` tiene que dar el mismo resultado, byte a byte, que
 * `ReportMarkdown.FromPlainText` del backend (fixtures F1-F17).
 */

import type { ReportTexts } from "@/lib/api";

/** Valor de `report_texts.formato` que manda siempre este cliente. */
export const REPORT_TEXT_FORMAT = "markdown" as const;

/** Bloque de párrafo vacío (línea en blanco intencional), D9. */
export const EMPTY_PARAGRAPH = "&nbsp;";

const NBSP = " ";

/**
 * Limpieza igual a `ReportValues.Clean` del backend: `\r\n`/`\r` → `\n`,
 * tab → espacio y fuera los caracteres de control `< 0x20` salvo `\n`.
 */
export function cleanText(text: string): string {
  return text
    .replace(/\r\n?/g, "\n")
    .replace(/\t/g, " ")
    // eslint-disable-next-line no-control-regex
    .replace(/[\u0000-\u0009\u000B-\u001F]/g, "");
}

/**
 * Escape de texto literal (§4.4 pasos 1 y 2): entidades para `& < >` y `\`
 * delante de `` \ ` * _ [ ] ~ ``. Es el mismo que aplica `@tiptap/markdown`
 * a los nodos de texto, así texto convertido y texto escrito coinciden.
 */
export function escapeInlineText(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/([\\`*_[\]~])/g, "\\$1");
}

/**
 * Escape de inicio de línea (§4.4 paso 3), sobre una línea ya escapada:
 * - los espacios iniciales pasan a U+00A0 (conserva la sangría y no se vuelve código);
 * - `1.`/`1)` + espacio → `1\.`; `#`, `-`, `+`, `*` + espacio, `===`, `---`,
 *   ```` ``` ```` y `~~~` → `\` adelante.
 * Solo mira el principio: los dos espacios finales de un salto duro quedan.
 */
export function escapeLineStart(line: string): string {
  const lead = /^ +/.exec(line);
  // Con la sangría en U+00A0 la línea ya no puede abrir un bloque: no hace falta más.
  if (lead) return NBSP.repeat(lead[0].length) + line.slice(lead[0].length);
  const ordered = /^(\d{1,9})(?=[.)](?:\s|$))/.exec(line);
  if (ordered) return ordered[1] + "\\" + line.slice(ordered[1].length);
  if (
    /^#{1,6}(?=\s|$)/.test(line) ||
    /^[-+*](?=\s|$)/.test(line) ||
    /^=+\s*$/.test(line) ||
    /^-+\s*$/.test(line) ||
    /^`{3,}/.test(line) ||
    /^~{3,}/.test(line)
  ) {
    return "\\" + line;
  }
  return line;
}

/** Escape completo de un contenido de línea (pasos 1-3 de §4.4). */
function escapeLine(text: string): string {
  return escapeLineStart(escapeInlineText(text));
}

/**
 * Texto plano (casos anteriores a esta HU) → Markdown del dialecto (§4.4).
 * Cada línea es un bloque; `"- "` al principio es un ítem de viñeta; las
 * líneas vacías del medio son `&nbsp;`.
 */
export function plainToMarkdown(text: string): string {
  const lines = cleanText(text ?? "").split("\n").map(l => l.replace(/\s+$/u, ""));
  let start = 0;
  let end = lines.length;
  while (start < end && lines[start] === "") start++;
  while (end > start && lines[end - 1] === "") end--;

  const blocks: string[] = [];
  let list: string[] | null = null;
  for (let i = start; i < end; i++) {
    const line = lines[i];
    if (line.startsWith("- ")) {
      const item = "- " + escapeLine(line.slice(2).replace(/^\s+/u, ""));
      if (list) list.push(item);
      else { list = [item]; blocks.push(""); }
      blocks[blocks.length - 1] = list.join("\n");
      continue;
    }
    list = null;
    blocks.push(line === "" ? EMPTY_PARAGRAPH : escapeLine(line));
  }
  return blocks.join("\n\n");
}

/**
 * Normaliza lo que sale del editor antes de compararlo o guardarlo (§7.4):
 * `\r\n` → `\n`, fuera los bloques `&nbsp;` del principio y del final y los
 * espacios/saltos finales. Un documento vacío da `""`.
 */
export function normalizeMarkdown(md: string): string {
  const blocks = (md ?? "").replace(/\r\n?/g, "\n").replace(/[ \n]+$/, "").split("\n\n");
  const isEmpty = (b: string) => b.trim() === "" || b.trim() === EMPTY_PARAGRAPH;
  let start = 0;
  let end = blocks.length;
  while (start < end && isEmpty(blocks[start])) start++;
  while (end > start && isEmpty(blocks[end - 1])) end--;
  return blocks.slice(start, end).join("\n\n").replace(/^\n+/, "").replace(/[ \n]+$/, "");
}

/**
 * Vacío en Markdown (§4.5, DP1 A): no hay ninguna letra ni dígito visible.
 * Por regex, sin cargar el editor. El servidor es la autoridad en los bordes.
 */
export function isBlankMarkdown(md: string | null | undefined): boolean {
  if (!md) return true;
  // Una imagen no aporta texto visible: ni el alt ni el nombre cuentan
  // (editor-imagenes-informe §4.7, D12 A para los obligatorios).
  const visible = stripReportImages(md)
    .replace(/&(#\d+|#x[0-9a-f]+|[a-z][a-z0-9]*);/gi, "")
    .replace(/<\/?u>/g, "")
    .replace(/\]\([^)]*\)/g, "]")
    .replace(/^(\s*(>\s*)*)\d{1,9}[.)](?=\s|$)/gm, "$1");
  return !/[\p{L}\p{N}]/u.test(visible);
}

/** "Vacío" según el formato del caso: texto plano como siempre, Markdown por §4.5. */
export function isBlankReportText(
  value: string | null | undefined,
  formato: ReportTexts["formato"],
): boolean {
  if (formato === REPORT_TEXT_FORMAT) return isBlankMarkdown(value);
  return !value || !value.trim();
}

/** Esquemas de enlace permitidos (D7): http, https y mailto, sin espacios iniciales. */
export function isAllowedUrl(url: string | null | undefined): boolean {
  return !!url && /^(https?:\/\/|mailto:)/i.test(url);
}

/** URL de un enlace Markdown: espacios, `(`, `)`, `<` y `>` van percent-encoded (§4.3). */
export function encodeUrlForMarkdown(url: string): string {
  return url.replace(/[ ()<>]/g, c => "%" + c.charCodeAt(0).toString(16).toUpperCase().padStart(2, "0"));
}

/* ── Imágenes: capturas del caso dentro de los textos (editor-imagenes-informe, SDD §4.1 y §4.8) ── */

/** Esquema de la referencia: `![alt](captura:<nombre-codificado>)`. */
export const REPORT_IMAGE_SCHEME = "captura:" as const;
/** Tope de imágenes por sección (D4). */
export const MAX_REPORT_IMAGES_PER_SECTION = 20;
/** Largo máximo de la descripción (alt), en unidades UTF-16 (`.length`). */
export const MAX_REPORT_IMAGE_ALT = 200;

const NAME_SAFE_BYTE = /^[A-Za-z0-9._-]$/;

/**
 * Nombre de archivo → forma codificada del destino: cada byte UTF-8 fuera de
 * `[A-Za-z0-9._-]` va como `%XX` con hex en mayúsculas (igual que
 * `ReportImageRef.EncodeName` del servidor).
 */
export function encodeReportImageName(name: string): string {
  let out = "";
  for (const byte of new TextEncoder().encode(name)) {
    const ch = String.fromCharCode(byte);
    out += byte < 0x80 && NAME_SAFE_BYTE.test(ch) ? ch : "%" + byte.toString(16).toUpperCase().padStart(2, "0");
  }
  return out;
}

/** Inversa de `encodeReportImageName`. `null` si hay un `%` mal formado, otro carácter o UTF-8 inválido. */
export function decodeReportImageName(encoded: string): string | null {
  if (!/^(?:[A-Za-z0-9._-]|%[0-9A-Fa-f]{2})+$/.test(encoded)) return null;
  const bytes: number[] = [];
  for (let i = 0; i < encoded.length; i++) {
    if (encoded[i] === "%") {
      bytes.push(parseInt(encoded.slice(i + 1, i + 3), 16));
      i += 2;
    } else {
      bytes.push(encoded.charCodeAt(i));
    }
  }
  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(new Uint8Array(bytes));
  } catch {
    return null;
  }
}

/** Descripción de una línea: sin caracteres de control, `trim` y espacios internos colapsados. */
export function normalizeReportImageAlt(alt: string): string {
  // eslint-disable-next-line no-control-regex
  return (alt ?? "").replace(/[\u0000-\u001F\u007F]/g, " ").replace(/\s+/g, " ").trim();
}

/** Forma canónica de la referencia (la única que acepta el servidor). */
export function formatReportImageMarkdown(filename: string, alt: string): string {
  return `![${escapeInlineText(normalizeReportImageAlt(alt))}](${REPORT_IMAGE_SCHEME}${encodeReportImageName(filename)})`;
}

/** Línea de imagen del dialecto (§4.8 paso 3). g1 = alt escapado, g2 = nombre codificado. */
export const REPORT_IMAGE_LINE =
  /^!\[((?:\\.|&[a-z0-9#]+;|[^\\\]\n])*)\]\(captura:((?:[A-Za-z0-9._-]|%[0-9A-Fa-f]{2})+)\)[ \t]*$/;

const NAMED_ENTITIES: Record<string, string> = { amp: "&", lt: "<", gt: ">", quot: '"', nbsp: "\u00A0" };

/** Alt escapado → texto literal: quita `\` de la puntuación ASCII y decodifica las entidades básicas. */
export function unescapeReportImageAlt(escaped: string): string {
  return escaped.replace(
    /\\([!-/:-@[-`{-~])|&(amp|lt|gt|quot|nbsp|#\d+|#[xX][0-9a-fA-F]+);/g,
    (m, punct: string | undefined, ent: string | undefined) => {
      if (punct !== undefined) return punct;
      if (!ent) return m;
      if (ent[0] !== "#") return NAMED_ENTITIES[ent] ?? m;
      const code = ent[1] === "x" || ent[1] === "X" ? parseInt(ent.slice(2), 16) : parseInt(ent.slice(1), 10);
      try {
        return code > 0 ? String.fromCodePoint(code) : m;
      } catch {
        return m;
      }
    },
  );
}

export interface ReportImageRef {
  filename: string;
  alt: string;
  /** Índice de línea (0-based) en el Markdown normalizado. */
  line: number;
}

const FENCE_OPEN = /^ {0,3}(`{3,}|~{3,})/;

/**
 * Escáner de líneas (§4.8): devuelve las referencias que son imagen según el
 * dialecto (línea propia de nivel superior, fuera de un bloque de código,
 * rodeada de líneas vacías). Lo comparten `extractReportImageRefs` y `stripReportImages`.
 */
function scanReportImages(md: string): { lines: string[]; refs: ReportImageRef[] } {
  const lines = (md ?? "").replace(/\r\n?/g, "\n").split("\n");
  const refs: ReportImageRef[] = [];
  let fence: { ch: string; len: number } | null = null;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (fence) {
      const close = /^ {0,3}(`+|~+)[ \t]*$/.exec(line);
      if (close && close[1][0] === fence.ch && close[1].length >= fence.len) fence = null;
      continue;
    }
    const open = FENCE_OPEN.exec(line);
    if (open) {
      fence = { ch: open[1][0], len: open[1].length };
      continue;
    }
    const m = REPORT_IMAGE_LINE.exec(line);
    if (!m) continue;
    const prevBlank = i === 0 || lines[i - 1].trim() === "";
    const nextBlank = i === lines.length - 1 || lines[i + 1].trim() === "";
    if (!prevBlank || !nextBlank) continue;
    const filename = decodeReportImageName(m[2]);
    if (filename === null) continue;
    refs.push({ filename, alt: unescapeReportImageAlt(m[1]), line: i });
  }
  return { lines, refs };
}

/** Referencias `captura:` del Markdown, en orden de documento. Sin Tiptap. */
export function extractReportImageRefs(md: string): ReportImageRef[] {
  return scanReportImages(md).refs;
}

/** El mismo Markdown con cada línea de imagen reemplazada por una línea vacía. */
export function stripReportImages(md: string): string {
  const { lines, refs } = scanReportImages(md);
  if (refs.length === 0) return lines.join("\n");
  const out = lines.slice();
  refs.forEach(r => { out[r.line] = ""; });
  return out.join("\n");
}

/** Entidades básicas del dialecto → texto (misma tabla que `unescapeReportImageAlt`). */
function decodeEntities(text: string): string {
  return text.replace(
    /&(amp|lt|gt|quot|nbsp|#\d+|#[xX][0-9a-fA-F]+);/g,
    (m, ent: string) => {
      if (ent[0] !== "#") return NAMED_ENTITIES[ent] ?? m;
      const code = ent[1] === "x" || ent[1] === "X" ? parseInt(ent.slice(2), 16) : parseInt(ent.slice(1), 10);
      try {
        return code > 0 ? String.fromCodePoint(code) : m;
      } catch {
        return m;
      }
    },
  );
}

/**
 * Markdown del dialecto → texto plano visible, para diffear una versión contra
 * otra (versionado-informe, HU6 D7). Quita las imágenes (no aportan texto),
 * los marcadores de énfasis/viñeta/encabezado y el escape, y de los enlaces
 * conserva solo el texto visible. No pretende ser un render fiel, solo el texto
 * que el perito lee, para que el diff hable de palabras y no de sintaxis.
 */
export function markdownToVisibleText(md: string): string {
  const withoutImages = stripReportImages(md ?? "");
  const lines = withoutImages.replace(/\r\n?/g, "\n").split("\n");
  const out: string[] = [];
  for (const raw of lines) {
    let line = raw;
    if (line.trim() === EMPTY_PARAGRAPH || line.trim() === "") { out.push(""); continue; }
    // Enlaces `[texto](url)` → `texto` (sin el destino).
    line = line.replace(/\[((?:\\.|[^\]\\])*)\]\([^)]*\)/g, "$1");
    // Encabezados y viñetas al inicio.
    line = line.replace(/^\s*#{1,6}\s+/, "").replace(/^\s*[-+*]\s+/, "");
    // Énfasis `**`, `*`, `_`, `~~`.
    line = line.replace(/\*\*|__|~~|[*_`]/g, "");
    // Escape de puntuación ASCII (`\*`, `\[`, …) → el carácter.
    // eslint-disable-next-line no-useless-escape
    line = line.replace(/\\([!-\/:-@\[-`{-~])/g, "$1");
    line = decodeEntities(line);
    // El NBSP de sangría vuelve a espacio.
    line = line.replace(/ /g, " ");
    out.push(line.replace(/\s+$/u, ""));
  }
  // Colapsa líneas vacías múltiples en una sola separación.
  return out.join("\n").replace(/\n{3,}/g, "\n\n").trim();
}
