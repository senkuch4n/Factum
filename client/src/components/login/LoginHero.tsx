"use client";

import { useState } from "react";
import { usePublicConfig } from "@/hooks/usePublicConfig";

/* Fondo del panel: solo tokens --fx-* (cambian con .dark sin JS). El degradé
   va en todos los tamaños; la trama, solo desde lg y con opacidad baja. */
const GLOW_STYLE: React.CSSProperties = {
  backgroundImage: "radial-gradient(ellipse 80% 60% at 15% 85%, var(--fx-accent-soft), transparent 70%)",
};
const GRID_STYLE: React.CSSProperties = {
  backgroundImage:
    "linear-gradient(var(--fx-border) 1px, transparent 1px), linear-gradient(90deg, var(--fx-border) 1px, transparent 1px)",
  backgroundSize: "48px 48px",
};

/* Logo "Sello", mismo patrón que AppNavbar: la variante se elige con clases
   dark: (sin depender del estado de React, sin flash al cargar). Marca sola
   en < sm, horizontal desde sm. El nombre accesible lo pone el contenedor. */
function Brand() {
  return (
    <div role="img" aria-label="Factum" className="flex items-center">
      <img src="/logo-theme-dark.svg" alt="" aria-hidden="true" width={289} height={64} className="hidden h-7 w-auto sm:dark:block lg:h-8" />
      <img src="/logo-theme-white.svg" alt="" aria-hidden="true" width={289} height={64} className="hidden h-7 w-auto sm:block sm:dark:hidden lg:h-8" />
      <img src="/logo-mark-dark.svg" alt="" aria-hidden="true" width={64} height={64} className="hidden h-7 w-7 dark:block sm:dark:hidden" />
      <img src="/logo-mark-white.svg" alt="" aria-hidden="true" width={64} height={64} className="block h-7 w-7 dark:hidden sm:hidden" />
    </div>
  );
}

/**
 * Panel de marca del login: estático (sin cámara, canvas ni animación).
 * Escritorio: columna izquierda de 55 % con la organización arriba y la marca,
 * el titular y la bajada abajo. Mobile: franja compacta por contenido.
 */
export function LoginHero() {
  const { organizationName, organizationLogoSrc } = usePublicConfig();
  const [orgLogoFailed, setOrgLogoFailed] = useState(false);
  const showOrgLogo = !!organizationLogoSrc && !orgLogoFailed;
  const showOrg = !!organizationName || showOrgLogo;

  return (
    <div className="relative isolate flex shrink-0 flex-col gap-4 overflow-hidden bg-fx-bg px-5 pb-5 pr-16 pt-4 lg:min-h-dvh lg:w-[55%] lg:gap-8 lg:px-14 lg:pb-16 lg:pt-12">
      <div aria-hidden="true" className="pointer-events-none absolute inset-0 -z-10" style={GLOW_STYLE} />
      <div aria-hidden="true" className="pointer-events-none absolute inset-0 -z-10 hidden opacity-40 lg:block" style={GRID_STYLE} />

      {showOrg && (
        <div className="flex min-w-0">
          <div className="inline-flex max-w-full items-center gap-2.5 rounded-fx-md border border-fx-border bg-fx-surface-1 px-2.5 py-1.5 text-fx-body-sm font-semibold text-fx-text">
            {showOrgLogo && (
              <img
                src={organizationLogoSrc}
                alt={organizationName ? "" : "Logo de la organización"}
                className="h-6 w-auto max-w-[160px] shrink-0 object-contain"
                onError={() => setOrgLogoFailed(true)}
              />
            )}
            {organizationName && (
              <span translate="no" className="min-w-0 truncate" title={organizationName}>
                {organizationName}
              </span>
            )}
          </div>
        </div>
      )}

      <div className="flex flex-col gap-3 lg:mt-auto lg:gap-6">
        <Brand />
        <span aria-hidden="true" className="hidden h-1 w-12 rounded-full bg-fx-accent lg:block" />
        <p className="hidden max-w-[14ch] text-balance text-fx-display text-fx-text lg:block">
          Captura y preservación forense de evidencia digital.
        </p>
        <p className="max-w-md text-fx-body-sm text-fx-text-2 lg:text-fx-body">
          Adquisición forense de evidencia digital en dispositivos móviles
        </p>
      </div>
    </div>
  );
}
