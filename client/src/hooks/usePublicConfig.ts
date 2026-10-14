"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";

export interface PublicClientConfig {
  organizationName: string | null;
  /** URL absoluta del logo de la organización (lista para `<img src>`). */
  organizationLogoSrc: string | null;
  /** `true` solo si el backend tiene la integración de soporte habilitada. */
  supportEnabled: boolean;
  /** `true` si el backend cifra el ZIP de evidencia con AES-256. */
  encryptZip: boolean;
  /** Versión mínima de Tatana para capturar (D6); `null` = sin mínimo. */
  tatanaMinVersion: string | null;
  /** `true` si el backend sella con una autoridad RFC 3161 (sellado-tiempo); `false` si falta. */
  timestampingEnabled: boolean;
}

const EMPTY: PublicClientConfig = {
  organizationName: null,
  organizationLogoSrc: null,
  supportEnabled: false,
  encryptZip: false,
  tatanaMinVersion: null,
  timestampingEnabled: false,
};

/* Caché a nivel de módulo: un solo fetch por carga de la app, compartido por
   todos los componentes que usan el hook. Si falla, queda en "sin branding"
   y sin soporte (sin toast ni reintento: la UI simplemente no muestra la
   organización ni el acceso al soporte). */
let cached: PublicClientConfig | null = null;
let pending: Promise<PublicClientConfig> | null = null;

/** Config pública con caché de módulo. Exportada para usarla fuera de React (`useAgentIdentity`). */
export function loadPublicConfig(): Promise<PublicClientConfig> {
  if (cached) return Promise.resolve(cached);
  pending ??= api
    .getPublicConfig()
    .then((cfg): PublicClientConfig => {
      const name = cfg.organization_name?.trim() || null;
      const logoPath = cfg.organization_logo_url || null;
      return {
        organizationName: name,
        organizationLogoSrc: logoPath ? api.brandingLogoURL(logoPath) : null,
        // Un backend viejo sin el campo (o cualquier valor no booleano) = apagado.
        supportEnabled: cfg.support_enabled === true,
        // Idem: sin el campo, la UI no promete cifrado (texto neutro).
        encryptZip: cfg.encrypt_zip === true,
        // Un valor no string o vacío = sin mínimo (no bloquea).
        tatanaMinVersion:
          typeof cfg.tatana_min_version === "string" && cfg.tatana_min_version.trim()
            ? cfg.tatana_min_version.trim()
            : null,
        // Sin el campo (backend anterior a sellado-tiempo) = la UI no menciona el sellado.
        timestampingEnabled: cfg.timestamping_enabled === true,
      };
    })
    .catch(() => EMPTY)
    .then((result) => {
      cached = result;
      return result;
    });
  return pending;
}

/**
 * Config pública del backend (`GET /api/config/public`): nombre, logo de la
 * organización emisora y flags públicos (`supportEnabled`, `encryptZip`).
 * Arranca vacía (igual que en el render del servidor, así no hay desajuste de
 * hidratación) y se completa cuando llega la respuesta.
 */
export function usePublicConfig(): PublicClientConfig {
  const [config, setConfig] = useState<PublicClientConfig>(() => cached ?? EMPTY);

  useEffect(() => {
    let active = true;
    loadPublicConfig().then((c) => {
      if (active) setConfig(c);
    });
    return () => {
      active = false;
    };
  }, []);

  return config;
}
