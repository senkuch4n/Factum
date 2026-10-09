"use client";

import { useCallback, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { UserMenu } from "@/components/UserMenu";
import { ExpertProfileDialog } from "@/components/ExpertProfileDialog";
import { ChangePasswordDialog } from "@/components/password/ChangePasswordDialog";
import { BrandingDialog } from "@/components/branding/BrandingDialog";
import { useAuthMode } from "@/hooks/useAuthMode";
import type { UserRole } from "@/lib/api";
import { ThemeSwitch } from "./ThemeSwitch";

interface AppNavbarProps {
  /** Contexto de navegación (breadcrumb); lo provee la página. */
  center?: React.ReactNode;
  /**
   * Selector de secciones de primer nivel ("Inspecciones" / "Métricas") en el
   * centro de la píldora. Si se pasa, tiene prioridad sobre `center` (el navbar
   * renderiza las pestañas en vez del breadcrumb): el dashboard lo usa solo en
   * modo historial y el wizard sigue con su `center`. Sin esta prop (admin,
   * wizard) el centro queda idéntico a antes.
   */
  sections?: { activeSection: "inspecciones" | "metricas" };
  /** Acciones de la página (AgentChip, CTA). */
  actions?: React.ReactNode;
  /** `role` habilita "Administrar cuentas" (superadmin en modo `local`). */
  user?: { name: string; sigla: string; dni: string; role?: UserRole } | null;
  /** Si hay `user` y `onLogout`, se renderiza UserMenu al final. */
  onLogout?: () => void;
  /** Default false: cada página decide si muestra el cambio de tema (el dashboard y el showcase lo muestran). */
  showThemeSwitch?: boolean;
  /** Si viene, la marca es un <Link>; si no, es estática. */
  brandHref?: string;
  /** Ancho máximo de la píldora (clase literal de Tailwind): igual a la columna de contenido de la página, así los bordes quedan alineados. */
  maxWidthClass?: string;
}

const SELF_TARGET = { kind: "self" } as const;

/* Marca dentro de la tapa clara izquierda: la "F" en degradé (PNG con fondo
   transparente) contrasta en los dos temas, porque la tapa es clara siempre. Decorativa: el nombre accesible lo pone
   el contenedor. width/height = proporción del SVG, para reservar el lugar. */
const BRAND_CAP = "fx-nav-cap flex h-10 w-10 shrink-0 items-center justify-center rounded-full";

/* Sin menú de usuario, el cambio de tema hace de tapa clara derecha para que
   la píldora siga enmarcada (utilidades explícitas: las de ThemeSwitch pisarían
   el fondo de `.fx-nav-cap`, que vive en @layer components). */
const THEME_AS_CAP =
  "h-10 w-10 rounded-full fx-nav-cap bg-[var(--fx-nav-cap-bg)] text-[color:var(--fx-nav-cap-text-2)] hover:bg-[var(--fx-nav-cap-hover)] hover:text-[color:var(--fx-nav-cap-text)]";

function BrandMark() {
  return <img src="/logo-mark-f.png" alt="" aria-hidden="true" width={256} height={256} className="h-7 w-7" />;
}

/* Secciones de primer nivel: dos <Link> tipo pestaña. La activa se distingue
   por color Y peso (no solo color) + `aria-current="page"`. Labels cortos:
   entran a 360px sin colapsar a ícono. */
const SECTIONS = [
  { key: "inspecciones", label: "Inspecciones", href: "/dashboard" },
  { key: "metricas", label: "Métricas", href: "/metricas" },
] as const;

const SECTION_BASE =
  "inline-flex min-h-9 items-center rounded-fx-pill px-3 text-sm no-underline transition-colors duration-fx-fast ease-fx fx-focus-ring";
const SECTION_ACTIVE = "bg-fx-surface-3 font-semibold text-fx-text";
const SECTION_IDLE = "font-medium text-fx-text-3 hover:text-fx-text";

function SectionTabs({ activeSection }: { activeSection: "inspecciones" | "metricas" }) {
  return (
    <nav aria-label="Secciones" className="flex min-w-0 items-center justify-center gap-1">
      {SECTIONS.map(({ key, label, href }) => {
        const active = key === activeSection;
        return (
          <Link
            key={key}
            href={href}
            aria-current={active ? "page" : undefined}
            className={`${SECTION_BASE} ${active ? SECTION_ACTIVE : SECTION_IDLE}`}
          >
            <span className="truncate">{label}</span>
          </Link>
        );
      })}
    </nav>
  );
}

/**
 * Navbar global: píldora flotante (rounded-full) despegada de los bordes, con
 * dos tapas claras (marca a la izquierda, usuario a la derecha) que enmarcan
 * la barra oscura. La barra es oscura en los dos temas: `.fx-nav-pill` hereda
 * la paleta oscura de tokens (globals.css), así el `center` y las `actions`
 * que pasa cada página se leen bien sin tocarlas.
 * `sticky` y no `fixed`: en el dashboard (h-screen con scroll interno) el
 * header queda fuera del contenedor que scrollea, y en páginas con scroll del
 * documento ocupa su lugar arriba sin compensar con padding. El header es
 * transparente y no captura clicks (pointer-events-none): solo la píldora.
 */
export function AppNavbar({
  center,
  sections,
  actions,
  user,
  onLogout,
  showThemeSwitch = false,
  brandHref,
  maxWidthClass = "max-w-[1400px]",
}: AppNavbarProps) {
  const showUserMenu = !!user && !!onLogout;
  const [profileOpen, setProfileOpen] = useState(false);
  const [passwordOpen, setPasswordOpen] = useState(false);
  const [brandingOpen, setBrandingOpen] = useState(false);
  const router = useRouter();
  // El cambio de contraseña y el panel de cuentas solo existen en modo `local`.
  const mode = useAuthMode().mode;
  const canChangePassword = mode === "local";
  const canAdmin = user?.role === "superadmin" && mode === "local";
  const openProfile = useCallback(() => setProfileOpen(true), []);
  const openPassword = useCallback(() => setPasswordOpen(true), []);
  const openBranding = useCallback(() => setBrandingOpen(true), []);
  const openAdmin = useCallback(() => router.push("/admin/cuentas"), [router]);

  return (
    <header className="pointer-events-none sticky top-0 z-fx-nav shrink-0 px-4 pt-3 sm:px-6 sm:pt-4">
      <div className={`fx-nav-pill pointer-events-auto mx-auto flex h-14 ${maxWidthClass} items-center gap-2 p-2 motion-safe:animate-[fx-nav-in_var(--fx-dur-slow)_var(--fx-ease-out)_backwards]`}>
        {/* Izquierda: marca en la tapa clara */}
        {brandHref ? (
          <Link href={brandHref} aria-label="Factum, ir al inicio" className={`${BRAND_CAP} no-underline fx-focus-ring`}>
            <BrandMark />
          </Link>
        ) : (
          <div className={BRAND_CAP} role="img" aria-label="Factum">
            <BrandMark />
          </div>
        )}

        {/* Centro: selector de secciones (prioridad) o contexto de navegación (trunca) */}
        <div className={`flex min-w-0 flex-1 items-center px-1 sm:px-2 ${sections ? "justify-center" : ""}`}>
          {sections ? <SectionTabs activeSection={sections.activeSection} /> : center}
        </div>

        {/* Derecha: acciones + tema + usuario (tapa clara) */}
        <div className="flex min-w-0 items-center gap-1 sm:gap-1.5">
          {actions}
          {actions && (showThemeSwitch || showUserMenu) && (
            <span aria-hidden="true" className="mx-1 hidden h-6 w-px shrink-0 bg-fx-border sm:block" />
          )}
          {showThemeSwitch && (
            <ThemeSwitch className={showUserMenu ? "h-10 w-10 rounded-full" : THEME_AS_CAP} />
          )}
          {showUserMenu && (
            <UserMenu
              user={user}
              onLogout={onLogout}
              onOpenProfile={openProfile}
              onOpenBranding={openBranding}
              onChangePassword={canChangePassword ? openPassword : undefined}
              onOpenAdmin={canAdmin ? openAdmin : undefined}
            />
          )}
        </div>
      </div>
      {showUserMenu && <ExpertProfileDialog visible={profileOpen} onHide={() => setProfileOpen(false)} />}
      {showUserMenu && (
        <BrandingDialog visible={brandingOpen} onHide={() => setBrandingOpen(false)} target={SELF_TARGET} />
      )}
      {showUserMenu && canChangePassword && (
        <ChangePasswordDialog visible={passwordOpen} onHide={() => setPasswordOpen(false)} dni={user.dni} />
      )}
    </header>
  );
}
