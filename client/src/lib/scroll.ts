/**
 * Scroll automático acotado al contenedor (SDD wizard-pulido-ux §8.6, DT15).
 * `Element.scrollIntoView` desplaza TODOS los ancestros desplazables y llegaba
 * a mover <html>, sacando de la vista el header sticky. Estos helpers mueven
 * solo el contenedor desplazable más cercano.
 */

function isScrollableOverflow(v: string): boolean {
  return v === "auto" || v === "scroll" || v === "overlay";
}

/** Ancestro más cercano con overflow-y auto|scroll (no `hidden`/`clip`/`visible`). null = el documento. */
export function findScrollParent(el: HTMLElement): HTMLElement | null {
  const docEl = document.documentElement;
  let node = el.parentElement;
  while (node && node !== document.body && node !== docEl) {
    if (isScrollableOverflow(getComputedStyle(node).overflowY)) return node;
    node = node.parentElement;
  }
  return null;
}

function px(v: string): number {
  const n = parseFloat(v);
  return Number.isFinite(n) ? n : 0;
}

function prefersReducedMotion(): boolean {
  return typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/**
 * Lleva `el` a la vista desplazando SOLO su contenedor desplazable más cercano
 * (nunca <html>/<body> ni los ancestros de ese contenedor). Respeta
 * scroll-margin-top de `el` y scroll-padding-top del contenedor. Con
 * prefers-reduced-motion el desplazamiento es instantáneo, aunque se pida
 * "smooth" (default: "smooth").
 */
export function scrollIntoContainer(
  el: HTMLElement,
  opts: { block: "start" | "center"; behavior?: ScrollBehavior },
): void {
  const behavior: ScrollBehavior = prefersReducedMotion() ? "auto" : opts.behavior ?? "smooth";
  const c = findScrollParent(el);

  if (!c) {
    // Página con scroll de documento (admin, detalle): ahí es lo correcto.
    el.scrollIntoView({ block: opts.block, behavior });
    return;
  }

  const r = el.getBoundingClientRect();
  const cr = c.getBoundingClientRect();
  const offset = r.top - cr.top;
  let top: number;
  if (opts.block === "start") {
    top = c.scrollTop + offset - px(getComputedStyle(el).scrollMarginTop) - px(getComputedStyle(c).scrollPaddingTop);
  } else {
    top = c.scrollTop + offset - (c.clientHeight - r.height) / 2;
  }
  const max = Math.max(0, c.scrollHeight - c.clientHeight);
  top = Math.min(Math.max(0, top), max);
  c.scrollTo({ top, behavior });

  // Red de seguridad: el documento nunca queda desplazado.
  const docScroller = document.scrollingElement;
  if (docScroller && docScroller.scrollTop !== 0) docScroller.scrollTop = 0;
}
