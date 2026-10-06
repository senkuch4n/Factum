"use client";

import { useEffect, useState } from "react";
import { api, type AuthMode } from "@/lib/api";

export interface AuthModeState {
  /** `null` mientras carga o si `GET /api/auth/mode` falló. */
  mode: AuthMode | null;
  passwordMinLength: number;
  passwordMaxLength: number;
}

const DEFAULTS: AuthModeState = { mode: null, passwordMinLength: 10, passwordMaxLength: 128 };

/* Caché de módulo (mismo patrón que `usePublicConfig`): un fetch por carga de
   la app. Si falla queda `mode: null` y los largos por defecto. */
let cached: AuthModeState | null = null;
let pending: Promise<AuthModeState> | null = null;

function loadAuthMode(): Promise<AuthModeState> {
  if (cached) return Promise.resolve(cached);
  pending ??= api
    .getAuthModeInfo()
    .then((info): AuthModeState => ({
      mode: info.mode,
      passwordMinLength: Number.isInteger(info.password_min_length) ? info.password_min_length : DEFAULTS.passwordMinLength,
      passwordMaxLength: Number.isInteger(info.password_max_length) ? info.password_max_length : DEFAULTS.passwordMaxLength,
    }))
    .catch(() => DEFAULTS)
    .then((result) => {
      cached = result;
      return result;
    });
  return pending;
}

/**
 * Modo de autenticación del backend (`dev` / `external` / `local`) y los
 * largos de la política de contraseñas. Arranca en `{ null, 10, 128 }` (igual
 * en el render del servidor, sin desajuste de hidratación).
 */
export function useAuthMode(): AuthModeState {
  const [state, setState] = useState<AuthModeState>(() => cached ?? DEFAULTS);

  useEffect(() => {
    let active = true;
    loadAuthMode().then((s) => {
      if (active) setState(s);
    });
    return () => {
      active = false;
    };
  }, []);

  return state;
}
