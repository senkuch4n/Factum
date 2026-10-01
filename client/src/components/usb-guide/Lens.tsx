"use client";

// Adaptado de Aceternity UI — https://ui.aceternity.com/components/lens
// Lupa que sigue al cursor para las fichas de la guía USB: renderiza el
// contenido dos veces (una normal, otra escalada dentro de una máscara
// circular). Solo con mouse; la alternativa de teclado y táctil es el link
// "Abrir en pestaña nueva" de la ficha.
import { useState } from "react";

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
          <div
            aria-hidden="true"
            className="pointer-events-none absolute inset-0 z-40 overflow-hidden motion-safe:animate-[fx-fade-in_var(--fx-dur-fast)_var(--fx-ease-out)_both]"
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
          </div>

          <span
            aria-hidden="true"
            className="pointer-events-none absolute z-50 rounded-full ring-2 ring-fx-border-strong shadow-fx-2"
            style={{ width: lensSize, height: lensSize, left: pos.x - r, top: pos.y - r }}
          />
        </>
      )}
    </div>
  );
}
