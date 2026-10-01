"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";

export interface PublicBranding {
  organizationName: string | null;
  /** URL absoluta del logo de la organización (lista para `<img src>`). */
  organizationLogoSrc: string | null;
}

const EMPTY: PublicBranding = { organizationName: null, organizationLogoSrc: null };

/* Caché a nivel de módulo: un solo fetch por carga de la app, compartido por
   todos los componentes que usan el hook. Si falla, queda en "sin branding"
   (sin toast ni reintento: la UI simplemente no muestra la organización). */
let cached: PublicBranding | null = null;
let pending: Promise<PublicBranding> | null = null;

function loadPublicBranding(): Promise<PublicBranding> {
  if (cached) return Promise.resolve(cached);
  pending ??= api
    .getPublicConfig()
    .then((cfg): PublicBranding => {
      const name = cfg.organization_name?.trim() || null;
      const logoPath = cfg.organization_logo_url || null;
      return {
        organizationName: name,
        organizationLogoSrc: logoPath ? api.brandingLogoURL(logoPath) : null,
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
 * Nombre y logo de la organización emisora (`GET /api/config/public`).
 * Arranca en `null` (igual que en el render del servidor, así no hay
 * desajuste de hidratación) y se completa cuando llega la respuesta.
 */
export function usePublicConfig(): PublicBranding {
  const [branding, setBranding] = useState<PublicBranding>(() => cached ?? EMPTY);

  useEffect(() => {
    let active = true;
    loadPublicBranding().then((b) => {
      if (active) setBranding(b);
    });
    return () => {
      active = false;
    };
  }, []);

  return branding;
}
