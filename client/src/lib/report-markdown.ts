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
  const visible = md
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
