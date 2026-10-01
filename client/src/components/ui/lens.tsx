"use client";

// Adaptado de Aceternity UI — https://ui.aceternity.com/components/lens
// Lupa que sigue al cursor: renderiza el contenido dos veces (una normal, otra
// escalada dentro de una máscara circular). Pensado para imágenes estáticas
// (la ficha del PDF), no para contenido pesado.
import { useState } from "react";
import { motion } from "framer-motion";

export function Lens({
  children,
  zoomFactor = 2,
  lensSize = 220,
  className = "",
}: {
  children: React.ReactNode;
  zoomFactor?: number;
  lensSize?: number;
  className?: string;
}) {
  const [hovering, setHovering] = useState(false);
  const [pos, setPos] = useState({ x: 0, y: 0 });
  const r = lensSize / 2;

  return (
    <div
      className={`relative overflow-hidden ${className}`}
      onMouseEnter={() => setHovering(true)}
      onMouseLeave={() => setHovering(false)}
      onMouseMove={(e) => {
        const rect = e.currentTarget.getBoundingClientRect();
        setPos({ x: e.clientX - rect.left, y: e.clientY - rect.top });
      }}
    >
      {children}

      {hovering && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.15, ease: "easeOut" }}
            className="pointer-events-none absolute inset-0 z-40 overflow-hidden"
            style={{
              maskImage: `radial-gradient(circle ${r}px at ${pos.x}px ${pos.y}px, black 100%, transparent 100%)`,
              WebkitMaskImage: `radial-gradient(circle ${r}px at ${pos.x}px ${pos.y}px, black 100%, transparent 100%)`,
            }}
          >
            <div
              className="absolute inset-0"
              style={{ transform: `scale(${zoomFactor})`, transformOrigin: `${pos.x}px ${pos.y}px` }}
            >
              {children}
            </div>
          </motion.div>

          <span
            className="pointer-events-none absolute z-50 rounded-full shadow-lg ring-2 ring-white/70"
            style={{ width: lensSize, height: lensSize, left: pos.x - r, top: pos.y - r }}
          />
        </>
      )}
    </div>
  );
}
