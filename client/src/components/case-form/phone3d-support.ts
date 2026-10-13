/**
 * Soporte del teléfono 3D de la ficha (formulario-causa-rediseno §6.8 D5).
 * Sin imports de `three`: lo usa `DeviceStage` para decidir si pide el chunk.
 */

let supported: boolean | null = null;
let disabledForSession = false;

/**
 * ¿Hay WebGL **acelerado**? `failIfMajorPerformanceCaveat` hace que en PCs con
 * renderizado por software (sin GPU, escritorio remoto, SwiftShader) no haya
 * contexto → se usa el marco 2D. Se calcula una vez por sesión.
 */
export function canUsePhone3D(): boolean {
  if (disabledForSession) return false;
  if (supported !== null) return supported;
  try {
    const canvas = document.createElement("canvas");
    const opts: WebGLContextAttributes = { failIfMajorPerformanceCaveat: true };
    const gl = (canvas.getContext("webgl2", opts) ?? canvas.getContext("webgl", opts)) as WebGLRenderingContext | null;
    if (gl) gl.getExtension("WEBGL_lose_context")?.loseContext();
    supported = !!gl;
  } catch {
    supported = false;
  }
  return supported;
}

/** Apaga el 3D hasta recargar la página (error de render, contexto perdido, textura que no carga). */
export function disablePhone3D(): void {
  disabledForSession = true;
}

/** Valor crudo de un token `--fx-*` del tema activo (p. ej. "#141619"). */
export function readFxColor(name: `--fx-${string}`): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

/** Duración de un token `--fx-dur-*` en ms ("320ms" → 320; "0.3s" → 300). */
export function readFxDurationMs(name: `--fx-dur-${string}`, fallback = 320): number {
  const raw = readFxColor(name as `--fx-${string}`);
  const n = parseFloat(raw);
  if (!Number.isFinite(n)) return fallback;
  return raw.endsWith("ms") ? n : raw.endsWith("s") ? n * 1000 : n;
}
