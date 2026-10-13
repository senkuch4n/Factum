"use client";

/**
 * Teléfono 3D de la Ficha de la causa (formulario-causa-rediseno §6.8 D7 + §11 P1 B).
 *
 * ÚNICO archivo del repo que importa `three` / `@react-three/fiber`; solo lo
 * importa `DeviceStage` vía `next/dynamic` (chunk aparte, nunca en la carga
 * inicial de /dashboard).
 *
 * - `frameloop="demand"`: solo se dibuja tras `invalidate()` (entrada, textura
 *   nueva, tema, `dim`, arrastre y su vuelta). Tipear no dibuja nada.
 * - Geometría procedural (sin .glb ni drei), colores leídos de los tokens --fx-*.
 * - Arrastre horizontal acotado a ±25° (P1 B): handlers de puntero propios con
 *   pointer capture; amortiguado con tanh (sin tope seco) y vuelta suave a la
 *   pose al soltar. Es decorativo: no expone información ni acciones (la
 *   captura se amplía con el botón "Ver captura" de la ficha).
 */

import { useEffect, useMemo, useRef, useState } from "react";
import { Canvas, useFrame, type RootState } from "@react-three/fiber";
import * as THREE from "three";
import { useTheme } from "@/lib/theme";
import { readFxColor, readFxDurationMs } from "./phone3d-support";

export interface PhoneModel3DProps {
  /** URL de la captura (agentFileURL). */
  src: string | null;
  /** Captura vieja (stale) → pantalla atenuada. */
  dim: boolean;
  platform: "android" | "ios";
  /** Después del primer frame dibujado. */
  onReady: () => void;
  onError: (reason: string) => void;
}

/* ── Medidas (unidades de escena) ─────────────────────────────── */
const BODY_W = 0.72;
const BODY_H = 1.44;
const DEPTH = 0.06;
const BEVEL = 0.012;
const SCREEN_W = 0.66;
const SCREEN_H = 1.38;
const SCREEN_Z = DEPTH / 2 + BEVEL + 0.0015;

/* ── Pose ─────────────────────────────────────────────────────── */
const POSE_X = 0.06;
const POSE_Y = -0.28;
const ENTRY_FROM_Y = -0.7;
/** ±25° de arrastre. */
const DRAG_MAX = (25 * Math.PI) / 180;
/** Radianes por píxel de arrastre (antes del amortiguado). */
const RAD_PER_PX = 0.009;

const easeOutCubic = (t: number) => 1 - Math.pow(1 - t, 3);

function roundedRectShape(w: number, h: number, r: number): THREE.Shape {
  const x = -w / 2;
  const y = -h / 2;
  const s = new THREE.Shape();
  s.moveTo(x + r, y);
  s.lineTo(x + w - r, y);
  s.quadraticCurveTo(x + w, y, x + w, y + r);
  s.lineTo(x + w, y + h - r);
  s.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  s.lineTo(x + r, y + h);
  s.quadraticCurveTo(x, y + h, x, y + h - r);
  s.lineTo(x, y + r);
  s.quadraticCurveTo(x, y, x + r, y);
  return s;
}

/** Las UV de ShapeGeometry vienen en coordenadas de la forma: se llevan a 0..1 sobre la caja. */
function normalizeUv(geo: THREE.BufferGeometry) {
  geo.computeBoundingBox();
  const box = geo.boundingBox!;
  const w = box.max.x - box.min.x;
  const h = box.max.y - box.min.y;
  const pos = geo.attributes.position;
  const uv = new Float32Array(pos.count * 2);
  for (let i = 0; i < pos.count; i++) {
    uv[i * 2] = (pos.getX(i) - box.min.x) / w;
    uv[i * 2 + 1] = (pos.getY(i) - box.min.y) / h;
  }
  geo.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
}

interface SceneColors { body: THREE.Color; screen: THREE.Color }

function readColors(): SceneColors {
  const c = (name: `--fx-${string}`) => {
    const col = new THREE.Color();
    const v = readFxColor(name);
    if (v) col.setStyle(v);
    return col;
  };
  return { body: c("--fx-device-body"), screen: c("--fx-bg") };
}

/** Estado del arrastre, compartido entre los handlers DOM y `useFrame` (sin re-render). */
interface DragState {
  pointerId: number | null;
  startX: number;
  startRaw: number;
  /** Desplazamiento actual sobre la pose, ya amortiguado (rad). */
  offset: number;
  /** Vuelta a la pose en curso. */
  returning: { from: number; start: number } | null;
}

function Scene({
  texture, dim, platform, colors, drag, entryMs, onFirstFrame,
}: {
  texture: THREE.Texture | null;
  dim: boolean;
  platform: "android" | "ios";
  colors: SceneColors;
  drag: React.RefObject<DragState>;
  entryMs: number;
  onFirstFrame: () => void;
}) {
  const group = useRef<THREE.Group>(null);
  const screenMat = useRef<THREE.MeshBasicMaterial>(null);
  const first = useRef(true);
  // La entrada arranca en el primer frame; con la pestaña oculta, directo a la pose final.
  const entry = useRef<{ start: number | null; done: boolean }>({
    start: null,
    done: typeof document !== "undefined" && document.visibilityState !== "visible",
  });

  const geos = useMemo(() => {
    const body = new THREE.ExtrudeGeometry(roundedRectShape(BODY_W, BODY_H, platform === "ios" ? 0.12 : 0.11), {
      depth: DEPTH, bevelEnabled: true, bevelThickness: BEVEL, bevelSize: BEVEL, bevelSegments: 3, curveSegments: 10,
    });
    body.center();
    const screen = new THREE.ShapeGeometry(roundedRectShape(SCREEN_W, SCREEN_H, 0.085), 10);
    normalizeUv(screen);
    const island = platform === "ios"
      ? new THREE.ShapeGeometry(roundedRectShape(0.17, 0.042, 0.021), 8)
      : new THREE.CircleGeometry(0.018, 24);
    return { body, screen, island };
  }, [platform]);

  useEffect(() => () => {
    geos.body.dispose();
    geos.screen.dispose();
    geos.island.dispose();
  }, [geos]);

  // Mapa / color de la pantalla: con `map` cambia el shader → needsUpdate.
  useEffect(() => {
    const m = screenMat.current;
    if (!m) return;
    m.map = texture;
    if (texture) m.color.setScalar(dim ? 0.4 : 1);
    else m.color.copy(colors.screen);
    m.needsUpdate = true;
  }, [texture, dim, colors]);

  useFrame((state: RootState) => {
    const g = group.current;
    if (!g) return;
    const now = performance.now();
    let keepGoing = false;

    // Entrada única al montar.
    let baseY = POSE_Y;
    const e = entry.current;
    if (!e.done) {
      if (e.start === null) e.start = now;
      const t = Math.min(1, (now - e.start) / entryMs);
      baseY = ENTRY_FROM_Y + (POSE_Y - ENTRY_FROM_Y) * easeOutCubic(t);
      if (t >= 1) e.done = true;
      else keepGoing = true;
    }

    // Vuelta a la pose después de soltar.
    const d = drag.current;
    if (d.returning) {
      const t = Math.min(1, (now - d.returning.start) / entryMs);
      d.offset = d.returning.from * (1 - easeOutCubic(t));
      if (t >= 1) { d.offset = 0; d.returning = null; }
      else keepGoing = true;
    }

    g.rotation.set(POSE_X, baseY + d.offset, 0);
    if (keepGoing) state.invalidate();
    if (first.current) {
      first.current = false;
      // El primer frame se dibuja justo después de este callback.
      requestAnimationFrame(onFirstFrame);
    }
  });

  const islandY = SCREEN_H / 2 - 0.045;

  return (
    <group ref={group} rotation={[POSE_X, entry.current.done ? POSE_Y : ENTRY_FROM_Y, 0]}>
      <mesh geometry={geos.body}>
        <meshStandardMaterial color={colors.body} roughness={0.42} metalness={0.25} />
      </mesh>
      <mesh geometry={geos.screen} position={[0, 0, SCREEN_Z]}>
        <meshBasicMaterial ref={screenMat} toneMapped={false} />
      </mesh>
      <mesh geometry={geos.island} position={[0, islandY, SCREEN_Z + 0.001]}>
        <meshBasicMaterial color={colors.body} toneMapped={false} />
      </mesh>
    </group>
  );
}

export default function PhoneModel3D({ src, dim, platform, onReady, onError }: PhoneModel3DProps) {
  const { theme } = useTheme();
  const [colors, setColors] = useState<SceneColors>(readColors);
  const [texture, setTexture] = useState<THREE.Texture | null>(null);
  const invalidateRef = useRef<(() => void) | null>(null);
  const cleanupGl = useRef<(() => void) | null>(null);
  const entryMs = useMemo(() => readFxDurationMs("--fx-dur-slow"), []);
  const drag = useRef<DragState>({ pointerId: null, startX: 0, startRaw: 0, offset: 0, returning: null });

  // Los callbacks del padre pueden cambiar de identidad; los efectos usan la última.
  const onErrorRef = useRef(onError);
  onErrorRef.current = onError;
  const onReadyRef = useRef(onReady);
  onReadyRef.current = onReady;

  // Tema claro/oscuro (o color de marca): releer tokens y redibujar.
  const firstTheme = useRef(true);
  useEffect(() => {
    if (firstTheme.current) { firstTheme.current = false; return; }
    setColors(readColors());
  }, [theme]);

  // Textura de la captura (sin useLoader: caché y errores bajo control).
  useEffect(() => {
    if (!src) return;
    let cancelled = false;
    // Query propio: el <img> 2D pide la misma URL sin CORS y esa respuesta puede
    // quedar en la caché HTTP sin Access-Control-Allow-Origin (§6.8 D7a).
    const url = src + (src.includes("?") ? "&" : "?") + "tex=1";
    new THREE.TextureLoader().setCrossOrigin("anonymous").load(
      url,
      tex => {
        if (cancelled) { tex.dispose(); return; }
        tex.colorSpace = THREE.SRGBColorSpace;
        const img = tex.image as { width: number; height: number };
        const imgAspect = img.width / img.height;
        const screenAspect = SCREEN_W / SCREEN_H;
        // "cover", como el object-cover del marco 2D.
        if (imgAspect > screenAspect) {
          tex.repeat.set(screenAspect / imgAspect, 1);
          tex.offset.set((1 - tex.repeat.x) / 2, 0);
        } else {
          tex.repeat.set(1, imgAspect / screenAspect);
          tex.offset.set(0, (1 - tex.repeat.y) / 2);
        }
        setTexture(prev => { prev?.dispose(); return tex; });
      },
      undefined,
      () => { if (!cancelled) onErrorRef.current("texture"); },
    );
    return () => { cancelled = true; };
  }, [src]);

  // Liberar la última textura al desmontar.
  const textureRef = useRef(texture);
  textureRef.current = texture;
  useEffect(() => () => { textureRef.current?.dispose(); cleanupGl.current?.(); }, []);

  // Cualquier cambio visible pide un frame.
  useEffect(() => { invalidateRef.current?.(); }, [texture, dim, colors]);

  /* ── Arrastre (P1 B) ── */
  function onPointerDown(e: React.PointerEvent<HTMLDivElement>) {
    const d = drag.current;
    if (d.pointerId !== null) return;                       // un solo puntero
    if (e.pointerType === "mouse" && e.button !== 0) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    d.pointerId = e.pointerId;
    d.startX = e.clientX;
    d.returning = null;                                     // interrumpible: sigue desde donde está
    const ratio = Math.max(-0.999, Math.min(0.999, d.offset / DRAG_MAX));
    d.startRaw = DRAG_MAX * Math.atanh(ratio);
  }
  function onPointerMove(e: React.PointerEvent<HTMLDivElement>) {
    const d = drag.current;
    if (e.pointerId !== d.pointerId) return;
    const raw = d.startRaw + (e.clientX - d.startX) * RAD_PER_PX;
    d.offset = DRAG_MAX * Math.tanh(raw / DRAG_MAX);        // amortiguado: se acerca a ±25° sin tope seco
    invalidateRef.current?.();
  }
  function endDrag(e: React.PointerEvent<HTMLDivElement>) {
    const d = drag.current;
    if (e.pointerId !== d.pointerId) return;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
    d.pointerId = null;
    if (d.offset !== 0) d.returning = { from: d.offset, start: performance.now() };
    invalidateRef.current?.();
  }

  return (
    <div
      aria-hidden="true"
      className="h-full w-full cursor-grab touch-pan-y select-none active:cursor-grabbing"
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onLostPointerCapture={endDrag}
    >
      <Canvas
        frameloop="demand"
        dpr={[1, 1.5]}
        flat
        gl={{ antialias: true, alpha: true, powerPreference: "low-power", failIfMajorPerformanceCaveat: true, preserveDrawingBuffer: false }}
        camera={{ fov: 28, position: [0, 0, 3.4], near: 0.1, far: 20 }}
        style={{ pointerEvents: "none" }}
        aria-hidden="true"
        onCreated={state => {
          invalidateRef.current = state.invalidate;
          const canvas = state.gl.domElement;
          const lost = (ev: Event) => { ev.preventDefault(); onErrorRef.current("context_lost"); };
          canvas.addEventListener("webglcontextlost", lost);
          cleanupGl.current = () => canvas.removeEventListener("webglcontextlost", lost);
        }}
      >
        {/* Cuerpo grafito (--fx-device-body): luz clave de frente-arriba, relleno suave y
            una luz de contorno desde atrás que marca el bisel y separa el teléfono del
            fondo en el tema oscuro (reintento 1). */}
        <ambientLight intensity={0.55} />
        <directionalLight position={[2, 3, 4]} intensity={1.1} />
        <directionalLight position={[-3, 1.5, -2.5]} intensity={1.6} />
        <Scene
          texture={texture}
          dim={dim}
          platform={platform}
          colors={colors}
          drag={drag}
          entryMs={entryMs}
          onFirstFrame={() => onReadyRef.current()}
        />
      </Canvas>
    </div>
  );
}
