/**
 * Pegado en el editor del informe (editor-texto-enriquecido, SDD §7.6, D8 A / D15).
 *
 * Se transforma el HTML del portapapeles antes de que ProseMirror lo parsee
 * con su esquema estricto (que ya descarta fuentes, tamaños, colores y todo
 * lo que no sea negrita, cursiva, subrayado, listas, cita, código, subtítulo
 * y enlace). ProseMirror lo parsea en un documento inerte: un `<img onerror>`
 * o un `<script>` pegado no se ejecuta.
 *
 * Acá solo se resuelve lo que el esquema perdería o leería mal:
 * - listas de Word (`<p style="mso-list:l0 level1">`) → `<ul>`/`<ol>` reales;
 * - tablas → un párrafo por fila, con las celdas unidas por " | ".
 * Y se detecta lo que se descarta para avisarle al perito.
 */

/** Elementos que el informe no admite: si vienen en el pegado, se avisa. */
const UNSUPPORTED = /<(img|table|h[124-6]|s|del|strike|hr)\b/i;

const MSO_LIST = /mso-list:\s*l(\d+)\s+level(\d+)/i;

export interface PasteResult {
  html: string;
  /** true si se quitó algo que el informe no admite (imágenes, tablas, tachado…). */
  stripped: boolean;
  /**
   * true si el HTML traía una imagen (`<img>`): se pega el resto y el aviso es
   * "usá el botón Imagen" (editor-imagenes-informe, D10).
   */
  image: boolean;
}

/** Marcador de un ítem de lista de Word (el `span` con `mso-list:Ignore`). */
function takeWordMarker(p: Element): string {
  const marker = Array.from(p.querySelectorAll("span")).find(s => /mso-list:\s*ignore/i.test(s.getAttribute("style") ?? ""));
  const text = (marker?.textContent ?? "").replace(/ /g, " ").trim();
  marker?.remove();
  return text;
}

/** `<p>` consecutivos con `mso-list` → listas anidadas según `levelN`. */
function convertWordLists(body: HTMLElement, doc: Document): void {
  // Word suele envolver todo en `<div class=WordSection1>`: se procesa cada contenedor.
  const parents = new Set<Element>();
  for (const el of Array.from(body.querySelectorAll("[style]"))) {
    if (MSO_LIST.test(el.getAttribute("style") ?? "") && el.parentElement) parents.add(el.parentElement);
  }
  parents.forEach(parent => convertWordListsIn(parent, doc));
}

function convertWordListsIn(container: Element, doc: Document): void {
  const children = Array.from(container.children);
  let i = 0;
  while (i < children.length) {
    const first = children[i];
    if (!MSO_LIST.test(first.getAttribute("style") ?? "")) { i++; continue; }

    // Racha de párrafos de lista consecutivos.
    const run: Element[] = [];
    while (i < children.length && MSO_LIST.test(children[i].getAttribute("style") ?? "")) run.push(children[i++]);

    const root = doc.createElement("div");
    // Pila de listas abiertas: stack[n] = lista del nivel n+1.
    const stack: HTMLElement[] = [];
    for (const p of run) {
      const level = Math.max(1, parseInt(MSO_LIST.exec(p.getAttribute("style") ?? "")?.[2] ?? "1", 10));
      const marker = takeWordMarker(p);
      const tag = /^(\d+|[a-z]+)[.)]$/i.test(marker) ? "ol" : "ul";
      while (stack.length > level) stack.pop();
      while (stack.length < level) {
        const list = doc.createElement(tag);
        const parent = stack[stack.length - 1];
        if (parent) {
          // Una sublista va dentro del último <li> del nivel de arriba.
          let host = parent.lastElementChild as HTMLElement | null;
          if (!host) { host = doc.createElement("li"); parent.appendChild(host); }
          host.appendChild(list);
        } else {
          root.appendChild(list);
        }
        stack.push(list);
      }
      const li = doc.createElement("li");
      const para = doc.createElement("p");
      para.innerHTML = p.innerHTML;
      li.appendChild(para);
      stack[stack.length - 1].appendChild(li);
    }
    first.before(...Array.from(root.childNodes));
    run.forEach(p => p.remove());
  }
}

/** Cada fila de una tabla pasa a ser un párrafo con las celdas separadas por " | ". */
function convertTables(body: HTMLElement, doc: Document): void {
  for (const table of Array.from(body.querySelectorAll("table"))) {
    const frag = doc.createDocumentFragment();
    for (const tr of Array.from(table.querySelectorAll("tr"))) {
      const cells = Array.from(tr.children)
        .filter(c => c.tagName === "TD" || c.tagName === "TH")
        .map(c => (c.textContent ?? "").replace(/\s+/g, " ").trim())
        .filter(Boolean);
      if (!cells.length) continue;
      const p = doc.createElement("p");
      p.textContent = cells.join(" | ");
      frag.appendChild(p);
    }
    table.replaceWith(frag);
  }
}

/** Transforma el HTML pegado. Pura sobre un documento inerte (`DOMParser`). */
export function transformPastedHtml(html: string): PasteResult {
  const stripped = UNSUPPORTED.test(html);
  const image = /<img\b/i.test(html);
  if (typeof DOMParser === "undefined") return { html, stripped, image };
  const doc = new DOMParser().parseFromString(html, "text/html");
  const body = doc.body;
  convertWordLists(body, doc);
  convertTables(body, doc);
  return { html: body.innerHTML, stripped, image };
}

/** Pegado solo de archivos (una imagen copiada): no se inserta nada. */
export function isFilesOnlyPaste(event: ClipboardEvent): boolean {
  const data = event.clipboardData;
  if (!data || data.files.length === 0) return false;
  return !data.getData("text/html") && !data.getData("text/plain");
}

/**
 * El portapapeles trae algún archivo de imagen (una captura copiada suele traer
 * también HTML o texto): se consume el pegado entero y se avisa (D10).
 */
export function hasImageFile(data: DataTransfer | null | undefined): boolean {
  if (!data) return false;
  return Array.from(data.files ?? []).some(f => f.type.startsWith("image/"))
    || Array.from(data.items ?? []).some(i => i.kind === "file" && i.type.startsWith("image/"));
}

/** Arrastre desde afuera con archivos o con una imagen en el HTML: no entra nada (D10). */
export function isExternalImageDrop(event: DragEvent): boolean {
  const dt = event.dataTransfer;
  if (!dt) return false;
  return (dt.files?.length ?? 0) > 0 || /<img\b/i.test(dt.getData("text/html") ?? "");
}
