"use client";

import { useEffect, useSyncExternalStore } from "react";
import { api } from "@/lib/api";
import type { AccountBranding } from "@/types";

/**
 * Marca del informe de la cuenta logueada (marca-por-cliente §9.3). Store a
 * nivel de módulo: un solo fetch compartido por todos los componentes (hoy el
 * `UserMenu`). El logo se pide con `Authorization` (blob → object URL), nunca
 * con `?token=`.
 *
 * - Si cambia el DNI (otro login en la misma pestaña) se descarta todo lo
 *   anterior y se revoca el object URL: nunca se muestra la marca de la
 *   sesión previa.
 * - `setMyBranding` lo llama el diálogo al guardar: el menú se actualiza sin
 *   recargar.
 * - `resetMyBranding` lo llama el logout.
 * Si la carga falla, queda `branding: null` y el menú cae a la config pública.
 */

export type MyBrandingStatus = "idle" | "loading" | "ready" | "error";

export interface MyBrandingState {
  dni: string | null;
  branding: AccountBranding | null;
  /** Object URL del logo de la cuenta, o `null`. */
  logoSrc: string | null;
  status: MyBrandingStatus;
}

const EMPTY: MyBrandingState = { dni: null, branding: null, logoSrc: null, status: "idle" };

let state: MyBrandingState = EMPTY;
/** Sube con cada reset: invalida las respuestas en vuelo de otra sesión. */
let generation = 0;
/** Sube con cada pedido de logo: solo vale el último. */
let logoSeq = 0;
/** Versión del logo que muestra `logoSrc` (para no repedirlo si no cambió). */
let logoVersion: string | null = null;
const listeners = new Set<() => void>();

function emit(next: MyBrandingState) {
  state = next;
  listeners.forEach(l => l());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

const getSnapshot = () => state;
const getServerSnapshot = () => EMPTY;

function revokeLogo() {
  if (state.logoSrc) URL.revokeObjectURL(state.logoSrc);
  logoVersion = null;
}

/** Trae el logo (si cambió de versión) y lo publica; si no hay, lo saca. */
function syncLogo(branding: AccountBranding | null) {
  const logo = branding?.exists ? branding.logo : null;
  if (!logo) {
    logoSeq++;
    if (state.logoSrc) { revokeLogo(); emit({ ...state, logoSrc: null }); }
    return;
  }
  if (logo.version === logoVersion && state.logoSrc) return;
  const mySeq = ++logoSeq;
  const myGen = generation;
  api.getBrandingImage(logo.url)
    .then(blob => {
      const url = URL.createObjectURL(blob);
      if (mySeq !== logoSeq || myGen !== generation) { URL.revokeObjectURL(url); return; }
      revokeLogo();
      logoVersion = logo.version;
      emit({ ...state, logoSrc: url });
    })
    .catch(() => {
      if (mySeq !== logoSeq || myGen !== generation) return;
      revokeLogo();
      emit({ ...state, logoSrc: null });
    });
}

/** Descarta la marca en memoria (logout o cambio de cuenta). */
export function resetMyBranding() {
  generation++;
  logoSeq++;
  revokeLogo();
  emit(EMPTY);
}

/** Actualiza la marca propia después de guardarla (diálogo propio o panel sobre la cuenta propia). */
export function setMyBranding(next: AccountBranding) {
  if (!state.dni) return;
  emit({ ...state, branding: next, status: "ready" });
  syncLogo(next);
}

function ensureLoaded(dni: string | null) {
  if (state.dni === dni && state.status !== "idle") return;
  if (state.dni !== dni) resetMyBranding();
  if (!dni) return;
  const myGen = generation;
  emit({ ...EMPTY, dni, status: "loading" });
  api.getMyBranding()
    .then(b => {
      if (myGen !== generation) return;
      emit({ ...state, branding: b, status: "ready" });
      syncLogo(b);
    })
    .catch(() => {
      if (myGen !== generation) return;
      emit({ ...state, branding: null, status: "error" });
    });
}

/** Marca de la cuenta `dni` (la de la sesión). Con `null` no carga nada. */
export function useMyBranding(dni: string | null): MyBrandingState {
  const snap = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  useEffect(() => { ensureLoaded(dni); }, [dni]);
  // Hasta que el efecto corra, nunca devolver la marca de otra cuenta.
  return snap.dni === dni ? snap : { ...EMPTY, dni };
}
