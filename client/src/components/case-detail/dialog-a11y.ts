/**
 * Utilidades del diálogo de detalle (bloqueo de scroll y foco atrapado). El
 * resto de los modales usa el `Dialog` de Prime, que ya resuelve esto; este
 * shell es propio porque Prime no permite el morph con `layoutId`.
 */

const FOCUSABLE = [
  "a[href]", "button:not([disabled])", "input:not([disabled]):not([type=hidden])",
  "select:not([disabled])", "textarea:not([disabled])", "[tabindex]:not([tabindex='-1'])",
  "[contenteditable='true']",
].join(",");

function focusablesIn(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE))
    .filter(el => el.getClientRects().length > 0 && !el.closest("[inert]"));
}

/**
 * Tab / Shift+Tab ciclan dentro de `container`. Si el foco quedó afuera (p. ej.
 * en un toast), el próximo Tab lo devuelve al diálogo.
 */
export function trapTab(e: KeyboardEvent, container: HTMLElement) {
  const items = focusablesIn(container);
  const active = document.activeElement as HTMLElement | null;
  if (items.length === 0) {
    e.preventDefault();
    container.focus({ preventScroll: true });
    return;
  }
  const first = items[0];
  const last = items[items.length - 1];
  if (!active || !container.contains(active)) {
    e.preventDefault();
    (e.shiftKey ? last : first).focus();
    return;
  }
  // El foco puede estar en algo fuera del orden de tabulación (el propio
  // diálogo o el cuerpo con tabindex=-1): se ubica por posición en el DOM.
  const outOfOrder = !items.includes(active);
  if (e.shiftKey) {
    const atStart = active === first || (outOfOrder && !!(active.compareDocumentPosition(first) & Node.DOCUMENT_POSITION_FOLLOWING));
    if (atStart) { e.preventDefault(); last.focus(); }
  } else {
    const atEnd = active === last || (outOfOrder && !!(active.compareDocumentPosition(last) & Node.DOCUMENT_POSITION_PRECEDING));
    if (atEnd) { e.preventDefault(); first.focus(); }
  }
}

function isScrollable(el: HTMLElement) {
  const { overflowY } = getComputedStyle(el);
  return (overflowY === "auto" || overflowY === "scroll") && el.scrollHeight > el.clientHeight;
}

/**
 * Bloquea el scroll del documento y de los contenedores con scroll propio que
 * envuelven a `origin` (el dashboard scrollea en un `div.overflow-y-auto`, no
 * en `body`). Compensa el ancho de la barra para que el contenido no salte
 * (y el morph de vuelta caiga sobre el disparador). Devuelve la restauración.
 */
export function lockScroll(origin: Element | null): () => void {
  const targets: HTMLElement[] = [document.documentElement];
  for (let el = origin?.parentElement ?? null; el && el !== document.body; el = el.parentElement) {
    if (isScrollable(el)) targets.push(el);
  }
  const saved = targets.map(el => {
    const prev = { el, overflow: el.style.overflow, paddingRight: el.style.paddingRight };
    const bar = el === document.documentElement
      ? window.innerWidth - el.clientWidth
      : el.offsetWidth - el.clientWidth - parseFloat(getComputedStyle(el).borderLeftWidth) - parseFloat(getComputedStyle(el).borderRightWidth);
    if (bar > 0) el.style.paddingRight = `${parseFloat(getComputedStyle(el).paddingRight) + bar}px`;
    el.style.overflow = "hidden";
    return prev;
  });
  return () => {
    for (const { el, overflow, paddingRight } of saved) {
      el.style.overflow = overflow;
      el.style.paddingRight = paddingRight;
    }
  };
}
