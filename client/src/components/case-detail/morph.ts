import { animate, type Transition } from "framer-motion";

/*
 * Coreografía del detalle, portada desde el repo de referencia
 * (imleticio/animation-modal-dinamic, `DynamicIsland.tsx`, GSAP) a
 * framer-motion. Las curvas GSAP se traducen a cúbicas equivalentes.
 */

type Bezier = [number, number, number, number];

export const POWER2_IN: Bezier    = [0.55, 0.085, 0.68, 0.53];
export const POWER2_OUT: Bezier   = [0.25, 0.46, 0.45, 0.94];
export const POWER3_IN: Bezier    = [0.55, 0.055, 0.675, 0.19];

/*
 * Apertura ~35% más corta que el repo (pedido del usuario): resorte de la
 * superficie .55 → .38s y contenido .22 → .13s de delay, mismo rebote sutil
 * (el `elastic.out(1, .72)` del repo, domado). El cierre no cambia.
 */
export const SHELL_SPRING: Transition   = { type: "spring", duration: 0.38, bounce: 0.16 };
export const CONTENT_SPRING: Transition = { type: "spring", duration: 0.34, bounce: 0.16 };
/** Delay de entrada del contenido (la superficie ya hizo casi todo el morph). */
export const CONTENT_DELAY = 0.13;

/** Máscara: oscurecimiento plano (`--fx-overlay`, sin `backdrop-filter`). */
export const OVERLAY_FADE: Transition = { duration: 0.26, ease: "easeOut" };

export const FADE: Transition = { duration: 0.15, ease: "easeOut" };

export type Rect = { left: number; top: number; width: number; height: number };

/**
 * Rect del disparador sin las transformaciones que le aplica la propia
 * coreografía (escala/blur del "dissolve"): se anulan un instante, sin pintar.
 */
export function naturalRect(el: HTMLElement): Rect {
  const prev = el.style.transform;
  el.style.transform = "none";
  const r = el.getBoundingClientRect();
  el.style.transform = prev;
  return { left: r.left, top: r.top, width: r.width, height: r.height };
}

/**
 * Rect de layout de la superficie (sin su `transform`): `offset*` relativo al
 * contenedor `fixed inset-0`, que coincide con el viewport.
 */
export function layoutRect(el: HTMLElement): Rect {
  return { left: el.offsetLeft, top: el.offsetTop, width: el.offsetWidth, height: el.offsetHeight };
}

/** Escala del dissolve: la del repo (.75) es para una píldora; en filas anchas se nota de más. */
const dissolveScale = (el: HTMLElement) => (el.getBoundingClientRect().width < 240 ? 0.75 : 0.96);

/** Apertura: el contenido del disparador se desenfoca y se va (repo: .22s; acá .15s, blur 8, power2.in). */
export function dissolveTrigger(el: HTMLElement) {
  el.style.willChange = "opacity, filter, transform";
  return animate(el, { opacity: 0, scale: dissolveScale(el), filter: "blur(8px)" }, { duration: 0.15, ease: POWER2_IN });
}

/** Cierre: el disparador vuelve hacia el final (repo: blur 3→0, scale .985→1, y 2→0, .18s). */
export function restoreTrigger(el: HTMLElement, delay: number) {
  return animate(
    el,
    { opacity: [0, 1], scale: [0.985, 1], y: [2, 0], filter: ["blur(3px)", "blur(0px)"] },
    { duration: 0.18, ease: POWER2_OUT, delay },
  );
}

/** Deja el disparador como estaba (sin estilos inline de la coreografía). */
export function clearTrigger(el: HTMLElement | null) {
  if (!el) return;
  for (const p of ["opacity", "filter", "transform", "will-change"]) el.style.removeProperty(p);
}
