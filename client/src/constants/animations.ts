export const EASE = [0.22, 1, 0.36, 1] as const;

export const spring = {
  default: { type: "spring" as const, stiffness: 300, damping: 25 },
  snappy:  { type: "spring" as const, stiffness: 380, damping: 28 },
  bouncy:  { type: "spring" as const, stiffness: 400, damping: 18 },
};

export const fadeSlide = {
  initial: { opacity: 0, y: 8 },
  animate: { opacity: 1, y: 0 },
  exit:    { opacity: 0, y: -4 },
};

export const fadeUp = {
  initial: (i = 0) => ({ opacity: 0, y: 16, scale: 0.97 }),
  animate: { opacity: 1, y: 0, scale: 1 },
};

export const scaleIn = {
  initial: { opacity: 0, scale: 0.88 },
  animate: { opacity: 1, scale: 1 },
  exit:    { opacity: 0, scale: 0.94 },
};

export const slideDir = {
  initial: (dir: number) => ({ opacity: 0, x: dir * 32 }),
  animate: { opacity: 1, x: 0 },
  exit:    (dir: number) => ({ opacity: 0, x: dir * -32 }),
};
