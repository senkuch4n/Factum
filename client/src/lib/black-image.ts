/**
 * Detección de capturas de pantalla negras (grabacion-android-robusta §7.3, Duda 6 A).
 * Solo visual y no bloqueante: marca la captura con un aviso; nunca la borra.
 * No toca el `<img>` de la bandeja (ni `crossOrigin`): baja la imagen aparte.
 */

/** Luminancia (0–255) por debajo de la cual un píxel cuenta como oscuro. */
export const BLACK_LUMA = 8;
/** Proporción de píxeles oscuros a partir de la cual la imagen es "negra". */
export const BLACK_RATIO = 0.99;

const SAMPLE_W = 48;
const SAMPLE_H = 96;

/**
 * Proporción (0–1) de píxeles con luminancia < `BLACK_LUMA` en un buffer RGBA.
 * Luminancia Rec. 709: 0.2126 R + 0.7152 G + 0.0722 B. Buffer vacío → 0.
 */
export function ratioDark(data: Uint8ClampedArray): number {
  const pixels = Math.floor(data.length / 4);
  if (pixels === 0) return 0;
  let dark = 0;
  for (let i = 0; i < pixels * 4; i += 4) {
    const luma = 0.2126 * data[i] + 0.7152 * data[i + 1] + 0.0722 * data[i + 2];
    if (luma < BLACK_LUMA) dark++;
  }
  return dark / pixels;
}

/** Píxeles RGBA de la imagen reducida a 48×96 (OffscreenCanvas o, si no hay, `<canvas>`). */
function samplePixels(bitmap: ImageBitmap): Uint8ClampedArray | null {
  if (typeof OffscreenCanvas !== "undefined") {
    const ctx = new OffscreenCanvas(SAMPLE_W, SAMPLE_H).getContext("2d");
    if (!ctx) return null;
    ctx.drawImage(bitmap, 0, 0, SAMPLE_W, SAMPLE_H);
    return ctx.getImageData(0, 0, SAMPLE_W, SAMPLE_H).data;
  }
  if (typeof document === "undefined") return null;
  const canvas = document.createElement("canvas");
  canvas.width = SAMPLE_W;
  canvas.height = SAMPLE_H;
  const ctx = canvas.getContext("2d");
  if (!ctx) return null;
  ctx.drawImage(bitmap, 0, 0, SAMPLE_W, SAMPLE_H);
  return ctx.getImageData(0, 0, SAMPLE_W, SAMPLE_H).data;
}

/**
 * `true` si ≥ 99 % de los píxeles tiene luminancia < 8; `false` si no; `null`
 * si no se pudo leer (red, CORS, formato, cancelación). Un `null` no avisa nada.
 */
export async function isMostlyBlack(src: string, signal?: AbortSignal): Promise<boolean | null> {
  if (typeof createImageBitmap !== "function") return null;
  let bitmap: ImageBitmap | null = null;
  try {
    const res = await fetch(src, { cache: "force-cache", signal });
    if (!res.ok) return null;
    const blob = await res.blob();
    if (signal?.aborted || blob.size === 0) return null;
    bitmap = await createImageBitmap(blob, { resizeWidth: SAMPLE_W, resizeHeight: SAMPLE_H, resizeQuality: "low" });
    if (signal?.aborted) return null;
    const data = samplePixels(bitmap);
    return data ? ratioDark(data) >= BLACK_RATIO : null;
  } catch {
    return null;
  } finally {
    bitmap?.close();
  }
}
