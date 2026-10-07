/**
 * Versión mínima de Tatana (tatana-instalador-autoupdate, D6 y D-T2). Puro.
 */
import type { AgentHealth } from "./agent";

/** `X.Y.Z` o `X.Y.Z-pre` (sin build). `null` si no parsea. */
function parseVersion(v: string): { core: [number, number, number]; pre: string[] } | null {
  const m = /^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$/.exec(v.trim());
  if (!m) return null;
  return {
    core: [Number(m[1]), Number(m[2]), Number(m[3])],
    pre: m[4] ? m[4].split(".") : [],
  };
}

/** Compara semver (sin build metadata). Negativo si a < b. */
function compareVersions(
  a: NonNullable<ReturnType<typeof parseVersion>>,
  b: NonNullable<ReturnType<typeof parseVersion>>,
): number {
  for (let i = 0; i < 3; i++) {
    if (a.core[i] !== b.core[i]) return a.core[i] - b.core[i];
  }
  // Una prerelease es menor que la versión final.
  if (!a.pre.length || !b.pre.length) return (a.pre.length ? -1 : 0) - (b.pre.length ? -1 : 0);
  for (let i = 0; i < Math.max(a.pre.length, b.pre.length); i++) {
    const x = a.pre[i];
    const y = b.pre[i];
    if (x === undefined) return -1;
    if (y === undefined) return 1;
    const nx = /^\d+$/.test(x);
    const ny = /^\d+$/.test(y);
    if (nx && ny) {
      if (Number(x) !== Number(y)) return Number(x) - Number(y);
    } else if (nx !== ny) {
      return nx ? -1 : 1;
    } else if (x !== y) {
      return x < y ? -1 : 1;
    }
  }
  return 0;
}

/** `true` si `version` es la real de Tatana (capability `real_version_v1`, D-T2). */
export function hasRealVersion(health: Pick<AgentHealth, "capabilities">): boolean {
  return health.capabilities?.includes("real_version_v1") ?? false;
}

/**
 * `true` si el Tatana de esta PC cumple la versión mínima. Sin
 * `real_version_v1` da `false` (un Tatana anterior dice siempre "2.0.0"); si
 * alguna de las dos versiones no parsea, también `false`.
 */
export function meetsMinVersion(health: Pick<AgentHealth, "capabilities" | "version">, min: string): boolean {
  if (!hasRealVersion(health)) return false;
  const have = parseVersion(health.version ?? "");
  const need = parseVersion(min);
  if (!have || !need) return false;
  return compareVersions(have, need) >= 0;
}
