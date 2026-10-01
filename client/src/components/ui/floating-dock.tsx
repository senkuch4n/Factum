"use client";

/**
 * Dock flotante inferior — adaptado de Aceternity UI (floating-dock) al design
 * system de factum:
 *  - los ítems disparan `onClick` (abren modales / togglean tema), no `href`
 *  - superficie forzada a `dark` para hacer juego con la top bar diagonal
 *  - tokens de factum, sin blur decorativo
 *  - `prefers-reduced-motion` desactiva la lupa (tamaños fijos)
 *  - z-40: por debajo de los modales (z-50)
 */

import { useRef, useState } from "react";
import {
  AnimatePresence,
  MotionValue,
  motion,
  useMotionValue,
  useReducedMotion,
  useSpring,
  useTransform,
} from "framer-motion";
import { ChevronUp } from "lucide-react";
import { cn } from "@/lib/utils";

export interface FloatingDockItem {
  title: string;
  icon: React.ReactNode;
  onClick: () => void;
  active?: boolean;
}

export function FloatingDock({
  items,
  className,
}: {
  items: FloatingDockItem[];
  className?: string;
}) {
  return (
    <div
      className={cn(
        "dark fixed left-1/2 z-40 -translate-x-1/2",
        "bottom-[calc(1rem+env(safe-area-inset-bottom))]",
        className,
      )}
    >
      <FloatingDockDesktop items={items} />
      <FloatingDockMobile items={items} />
    </div>
  );
}

/* ── Desktop: dock horizontal con lupa al pasar el mouse ── */
function FloatingDockDesktop({ items }: { items: FloatingDockItem[] }) {
  const mouseX = useMotionValue(Infinity);
  const reduce = !!useReducedMotion();

  return (
    <motion.div
      onMouseMove={(e) => mouseX.set(e.pageX)}
      onMouseLeave={() => mouseX.set(Infinity)}
      className="mx-auto hidden h-16 items-end gap-3 rounded-2xl px-3 pb-2 md:flex"
      style={{
        background: "var(--bg-surface)",
        border: "1px solid var(--border-md)",
        boxShadow: "0 8px 30px rgba(0,0,0,0.14)",
      }}
    >
      {items.map((item) => (
        <IconContainer key={item.title} mouseX={mouseX} reduce={reduce} {...item} />
      ))}
    </motion.div>
  );
}

function IconContainer({
  mouseX,
  reduce,
  title,
  icon,
  onClick,
  active,
}: FloatingDockItem & { mouseX: MotionValue; reduce: boolean }) {
  const ref = useRef<HTMLButtonElement>(null);

  const distance = useTransform(mouseX, (val) => {
    const bounds = ref.current?.getBoundingClientRect() ?? { x: 0, width: 0 };
    return val - bounds.x - bounds.width / 2;
  });

  const spring = { mass: 0.1, stiffness: 150, damping: 12 };
  const size = useSpring(
    useTransform(distance, [-140, 0, 140], reduce ? [44, 44, 44] : [44, 64, 44]),
    spring,
  );
  const iconSize = useSpring(
    useTransform(distance, [-140, 0, 140], reduce ? [20, 20, 20] : [20, 30, 20]),
    spring,
  );

  const [hovered, setHovered] = useState(false);

  return (
    <motion.button
      ref={ref}
      type="button"
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      aria-label={title}
      style={{
        width: size,
        height: size,
        background: active ? "var(--border-accent)" : "var(--bg-elevated)",
        border: `1px solid ${active ? "var(--blue-lg)" : "var(--border-md)"}`,
        color: active ? "var(--blue-lg)" : "var(--text-secondary)",
      }}
      className="relative flex aspect-square items-center justify-center rounded-full transition-colors"
    >
      <AnimatePresence>
        {hovered && (
          <motion.span
            initial={{ opacity: 0, y: 6, x: "-50%" }}
            animate={{ opacity: 1, y: 0, x: "-50%" }}
            exit={{ opacity: 0, y: 2, x: "-50%" }}
            className="pointer-events-none absolute -top-9 left-1/2 whitespace-pre rounded-md px-2 py-1 text-xs"
            style={{
              background: "var(--bg-elevated)",
              border: "1px solid var(--border-md)",
              color: "var(--text-primary)",
            }}
          >
            {title}
          </motion.span>
        )}
      </AnimatePresence>

      <motion.span
        style={{ width: iconSize, height: iconSize }}
        className="flex items-center justify-center"
      >
        {icon}
      </motion.span>
    </motion.button>
  );
}

/* ── Mobile: botón que despliega los ítems en vertical ── */
function FloatingDockMobile({ items }: { items: FloatingDockItem[] }) {
  const [open, setOpen] = useState(false);

  return (
    <div className="relative flex justify-center md:hidden">
      <AnimatePresence>
        {open && (
          <motion.div className="absolute inset-x-0 bottom-full mb-2 flex flex-col items-center gap-2">
            {items.map((item, idx) => (
              <motion.button
                key={item.title}
                type="button"
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 10, transition: { delay: idx * 0.04 } }}
                transition={{ delay: (items.length - 1 - idx) * 0.04 }}
                onClick={() => {
                  item.onClick();
                  setOpen(false);
                }}
                aria-label={item.title}
                className="flex h-11 w-11 items-center justify-center rounded-full"
                style={{
                  background: item.active ? "var(--border-accent)" : "var(--bg-elevated)",
                  border: `1px solid ${item.active ? "var(--blue-lg)" : "var(--border-md)"}`,
                  color: item.active ? "var(--blue-lg)" : "var(--text-secondary)",
                }}
              >
                <span className="flex h-[18px] w-[18px] items-center justify-center">
                  {item.icon}
                </span>
              </motion.button>
            ))}
          </motion.div>
        )}
      </AnimatePresence>

      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-label={open ? "Cerrar acciones" : "Abrir acciones"}
        aria-expanded={open}
        className="flex h-12 w-12 items-center justify-center rounded-full"
        style={{
          background: "var(--bg-surface)",
          border: "1px solid var(--border-md)",
          color: "var(--text-secondary)",
          boxShadow: "0 8px 30px rgba(0,0,0,0.14)",
        }}
      >
        <ChevronUp className={cn("h-5 w-5 transition-transform", open && "rotate-180")} />
      </button>
    </div>
  );
}

export default FloatingDock;
