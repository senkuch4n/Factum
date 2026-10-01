"use client";

import { useState } from "react";
import Link from "next/link";
import { UserMenu } from "@/components/UserMenu";
import { ExpertProfileDialog } from "@/components/ExpertProfileDialog";
import { ThemeSwitch } from "./ThemeSwitch";

interface AppNavbarProps {
  /** Contexto de navegación (breadcrumb); lo provee la página. */
  center?: React.ReactNode;
  /** Acciones de la página (AgentChip, CTA). */
  actions?: React.ReactNode;
  user?: { name: string; sigla: string; dni: string } | null;
  /** Si hay `user` y `onLogout`, se renderiza UserMenu al final. */
  onLogout?: () => void;
  /** Default false: cada página decide si muestra el cambio de tema (el dashboard y el showcase lo muestran). */
  showThemeToggle?: boolean;
  /** Si viene, la marca es un <Link>; si no, es estática. */
  brandHref?: string;
}

/* Logo "Sello": horizontal (marca + "Factum" en trazos) desde sm; en móvil,
   solo la marca. Las imágenes son decorativas: el nombre accesible lo pone
   el contenedor. width/height = proporción del SVG, para reservar el lugar. */
function Brand() {
  return (
    <>
      <img src="/logo-theme-dark.svg" alt="" aria-hidden="true" width={289} height={64} className="hidden h-6 w-auto sm:dark:block" />
      <img src="/logo-theme-white.svg" alt="" aria-hidden="true" width={289} height={64} className="hidden h-6 w-auto sm:block sm:dark:hidden" />
      <img src="/logo-mark-dark.svg" alt="" aria-hidden="true" width={64} height={64} className="hidden h-6 w-6 dark:block sm:dark:hidden" />
      <img src="/logo-mark-white.svg" alt="" aria-hidden="true" width={64} height={64} className="block h-6 w-6 dark:hidden sm:hidden" />
    </>
  );
}

/**
 * Navbar global del sistema de diseño nuevo: sólida, rectangular y siguiendo
 * al tema (sin el `.dark` forzado ni el clip-path de la barra anterior).
 * `sticky` y no `fixed`: en el dashboard (h-screen con scroll interno) el
 * header ya está fuera del contenedor que scrollea, y en páginas con scroll
 * del documento queda arriba sin compensar con padding. Alto 56px (h-14),
 * igual que la barra anterior.
 */
export function AppNavbar({
  center,
  actions,
  user,
  onLogout,
  showThemeToggle = false,
  brandHref,
}: AppNavbarProps) {
  const showUserMenu = !!user && !!onLogout;
  const [profileOpen, setProfileOpen] = useState(false);

  return (
    <header className="sticky top-0 z-fx-nav h-14 shrink-0 border-b border-fx-border bg-fx-nav-bg shadow-fx-1">
      <div className="mx-auto grid h-full max-w-[1400px] grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 px-4 sm:gap-3 sm:px-6">
        {/* Izquierda: marca */}
        {brandHref ? (
          <Link
            href={brandHref}
            aria-label="Factum, ir al inicio"
            className="flex min-w-0 items-center gap-2.5 rounded-fx-sm no-underline fx-focus-ring"
          >
            <Brand />
          </Link>
        ) : (
          <div className="flex min-w-0 items-center gap-2.5" role="img" aria-label="Factum">
            <Brand />
          </div>
        )}

        {/* Centro: contexto de navegación (trunca) */}
        <div className="flex min-w-0 items-center justify-center">{center}</div>

        {/* Derecha: acciones + tema + usuario */}
        <div className="flex min-w-0 items-center gap-2 justify-self-end">
          {actions}
          {(showThemeToggle || showUserMenu) && (
            <span aria-hidden="true" className="h-5 w-px shrink-0 bg-fx-border" />
          )}
          {showThemeToggle && <ThemeSwitch />}
          {showUserMenu && <UserMenu user={user} onLogout={onLogout} onOpenProfile={() => setProfileOpen(true)} />}
        </div>
      </div>
      {showUserMenu && <ExpertProfileDialog visible={profileOpen} onHide={() => setProfileOpen(false)} />}
    </header>
  );
}
