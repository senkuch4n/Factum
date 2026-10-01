"use client";

import React, { useEffect, useRef, useState } from "react";

/**
 * Fondo de puntos en canvas que brillan y se atenúan de forma orgánica.
 * Adaptado de Aceternity UI (dotted-glow-background) al design system de
 * factum: paleta monocroma + acento teal, glow muy contenido, y respeto
 * de `prefers-reduced-motion` (si está activo, se pinta un frame estático y
 * no se arranca el loop de animación).
 *
 * Pensado como capa decorativa detrás de superficies de marca / estados
 * transitorios (pantalla "Listo", empty states) — NO detrás de contenido
 * denso ni de texto que deba leerse.
 */
type DottedGlowBackgroundProps = {
  className?: string;
  /** distancia entre centros de puntos, en px */
  gap?: number;
  /** radio base de cada punto, en px CSS */
  radius?: number;
  /** color del punto en light (pulsa por alpha) */
  color?: string;
  /** color del punto en dark */
  darkColor?: string;
  /** color del glow para los puntos brillantes (light) */
  glowColor?: string;
  /** color del glow en dark */
  darkGlowColor?: string;
  /** opacidad global de toda la capa */
  opacity?: number;
  /** velocidad mínima por punto, en rad/s */
  speedMin?: number;
  /** velocidad máxima por punto, en rad/s */
  speedMax?: number;
  /** tema actual — si se pasa, se usa tal cual (source of truth del caller);
   *  si no, se infiere de la clase `.dark` en <html> (estrategia class de Tailwind) */
  isDark?: boolean;
};

const prefersReducedMotion = () =>
  typeof window !== "undefined" &&
  window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

export const DottedGlowBackground = ({
  className,
  gap = 24,
  radius = 1.8,
  // Casi opacos: el fade lo hacen `opacity` + el pulso por punto, no el alpha
  // del color. En light los puntos negros sobre #fafafa necesitan más peso que
  // los blancos sobre #0a0a0a en dark (asimetría perceptual del contraste).
  color = "rgba(9,9,11,0.95)",           // zinc-950 — puntos negros en light
  darkColor = "rgba(250,250,250,0.7)",   // zinc-50  — puntos claros en dark
  glowColor = "rgba(13,148,136,0.6)",    // --blue-lg light (teal de marca)
  darkGlowColor = "rgba(45,212,191,0.55)", // --blue-lg dark
  opacity = 0.35,
  speedMin = 0.15,
  speedMax = 0.5,
  isDark,
}: DottedGlowBackgroundProps) => {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [resolvedColor, setResolvedColor] = useState(isDark ? darkColor : color);
  const [resolvedGlow, setResolvedGlow] = useState(isDark ? darkGlowColor : glowColor);

  // El tema de factum es estrategia `class`: <html> lleva `.dark` o nada.
  // NO usar prefers-color-scheme como fallback — el toggle a "light" solo quita
  // la clase, así que con el SO en dark daría un falso positivo (puntos blancos
  // invisibles sobre #fafafa). Si el caller pasa `isDark`, esa es la verdad.
  useEffect(() => {
    const compute = () => {
      const dark = isDark ?? document.documentElement.classList.contains("dark");
      setResolvedColor(dark ? darkColor : color);
      setResolvedGlow(dark ? darkGlowColor : glowColor);
    };
    compute();

    if (isDark !== undefined) return; // controlado por el caller, no observar el DOM

    const mo = new MutationObserver(compute);
    mo.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    return () => mo.disconnect();
  }, [isDark, color, darkColor, glowColor, darkGlowColor]);

  useEffect(() => {
    const el = canvasRef.current;
    const container = containerRef.current;
    if (!el || !container) return;
    const ctx = el.getContext("2d");
    if (!ctx) return;

    const dpr = Math.min(Math.max(1, window.devicePixelRatio || 1), 2);
    let dots: { x: number; y: number; phase: number; speed: number }[] = [];

    const resize = () => {
      const { width, height } = container.getBoundingClientRect();
      el.width = Math.max(1, Math.floor(width * dpr));
      el.height = Math.max(1, Math.floor(height * dpr));
      el.style.width = `${Math.floor(width)}px`;
      el.style.height = `${Math.floor(height)}px`;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };

    const regenDots = () => {
      dots = [];
      const { width, height } = container.getBoundingClientRect();
      const cols = Math.ceil(width / gap) + 2;
      const rows = Math.ceil(height / gap) + 2;
      const min = Math.min(speedMin, speedMax);
      const span = Math.max(speedMax - speedMin, 0);
      for (let i = -1; i < cols; i++) {
        for (let j = -1; j < rows; j++) {
          dots.push({
            x: i * gap + (j % 2 === 0 ? 0 : gap * 0.5), // offset filas impares
            y: j * gap,
            phase: Math.random() * Math.PI * 2,
            speed: min + Math.random() * span,
          });
        }
      }
    };

    const paintFrame = (now: number) => {
      const time = now / 1000;
      ctx.clearRect(0, 0, el.width, el.height);
      ctx.save();
      ctx.fillStyle = resolvedColor;
      for (const d of dots) {
        const mod = (time * d.speed + d.phase) % 2;
        const lin = mod < 1 ? mod : 2 - mod; // onda triangular 0..1..0
        const a = 0.35 + 0.65 * lin;
        if (a > 0.6) {
          ctx.shadowColor = resolvedGlow;
          ctx.shadowBlur = 5 * ((a - 0.6) / 0.4);
        } else {
          ctx.shadowColor = "transparent";
          ctx.shadowBlur = 0;
        }
        ctx.globalAlpha = a * opacity;
        ctx.beginPath();
        ctx.arc(d.x, d.y, radius, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
    };

    const ro = new ResizeObserver(() => {
      resize();
      regenDots();
    });
    ro.observe(container);
    resize();
    regenDots();

    // Reduced motion: un frame estático y salimos, sin rAF.
    if (prefersReducedMotion()) {
      ctx.clearRect(0, 0, el.width, el.height);
      ctx.save();
      ctx.fillStyle = resolvedColor;
      ctx.globalAlpha = 0.6 * opacity;
      for (const d of dots) {
        ctx.beginPath();
        ctx.arc(d.x, d.y, radius, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
      return () => ro.disconnect();
    }

    let raf = 0;
    let stopped = false;
    let visible = true;

    const loop = (now: number) => {
      if (stopped) return;
      if (visible) paintFrame(now);
      raf = requestAnimationFrame(loop);
    };

    const io = new IntersectionObserver(
      (entries) => { visible = entries[0]?.isIntersecting ?? true; },
      { threshold: 0.1 },
    );
    io.observe(container);
    raf = requestAnimationFrame(loop);

    return () => {
      stopped = true;
      cancelAnimationFrame(raf);
      io.disconnect();
      ro.disconnect();
    };
  }, [gap, radius, resolvedColor, resolvedGlow, opacity, speedMin, speedMax]);

  return (
    <div
      ref={containerRef}
      aria-hidden="true"
      className={className}
      style={{ position: "absolute", inset: 0, pointerEvents: "none" }}
    >
      <canvas ref={canvasRef} style={{ display: "block", width: "100%", height: "100%" }} />
    </div>
  );
};

export default DottedGlowBackground;
