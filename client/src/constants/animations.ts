/* Transición entre pasos del wizard de /dashboard (framer-motion). */
export const EASE = [0.22, 1, 0.36, 1] as const;

export const slideDir = {
  initial: (dir: number) => ({ opacity: 0, x: dir * 32 }),
  animate: { opacity: 1, x: 0 },
  exit:    (dir: number) => ({ opacity: 0, x: dir * -32 }),
};
