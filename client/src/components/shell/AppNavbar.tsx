"use client";

import { useCallback, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ChartColumn, ClipboardList, HardDriveDownload } from "lucide-react";
import { UserMenu } from "@/components/UserMenu";
import { ExpertProfileDialog } from "@/components/ExpertProfileDialog";
import { ChangePasswordDialog } from "@/components/password/ChangePasswordDialog";
import { BrandingDialog } from "@/components/branding/BrandingDialog";
import { useAuthMode } from "@/hooks/useAuthMode";
import type { UserRole } from "@/lib/api";
import { FxTip } from "@/components/overlay/FxTip";
import { ThemeSwitch } from "./ThemeSwitch";
import { SessionBanners } from "./SessionBanners";

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
  sections?: { activeSection: "inspecciones" | "extracciones" | "metricas" };
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
   por color Y peso (no solo color) + `aria-current="page"`.
   Por debajo de `md` (pulido-post-qa-final M2) los textos no entran junto al
   chip de Tatana, el tema y el usuario (a 360-390 px quedan ~120-150 px para el
   centro, y a 640 px el chip "N equipos conectados" ya no deja lugar): cada
   pestaña pasa a ser un botón de ícono de 36 px con `aria-label` y tooltip.
   Desde `md`, la pestaña con texto de siempre. (La SDD proponía `sm` y pedía
   subir a `md` si a 640 px se cortaban: se cortaban.) */
const SECTIONS = [
  { key: "inspecciones", label: "Inspecciones", href: "/dashboard", Icon: ClipboardList },
  { key: "extracciones", label: "Extracciones", href: "/extracciones", Icon: HardDriveDownload },
  { key: "metricas", label: "Métricas", href: "/metricas", Icon: ChartColumn },
] as const;

const SECTION_BASE =
  "items-center rounded-fx-pill text-sm no-underline transition-colors duration-fx-fast ease-fx fx-focus-ring";
const SECTION_TEXT = "hidden min-h-9 px-3 md:inline-flex";
const SECTION_ICON = "inline-flex h-9 w-9 shrink-0 justify-center md:hidden";
const SECTION_ACTIVE = "bg-fx-surface-3 font-semibold text-fx-text";
const SECTION_IDLE = "font-medium text-fx-text-3 hover:text-fx-text";

function SectionTabs({ activeSection }: { activeSection: "inspecciones" | "extracciones" | "metricas" }) {
  // Centrado "seguro": `mx-auto` dentro de un contenedor sin `justify-center`.
  // Si no entra, desborda hacia la derecha y nunca sobre la marca.
  return (
    <nav aria-label="Secciones" className="mx-auto flex min-w-0 items-center gap-1">
      {SECTIONS.map(({ key, label, href, Icon }) => {
        const active = key === activeSection;
        const state = active ? SECTION_ACTIVE : SECTION_IDLE;
        const current = active ? ("page" as const) : undefined;
        return (
          <span key={key} className="contents">
            {/* < md: solo ícono (el nombre accesible lo da `aria-label`; el tooltip lo muestra al tocar o con foco) */}
            <FxTip label={label} side="bottom">
              <Link href={href} aria-current={current} aria-label={label} className={`${SECTION_BASE} ${SECTION_ICON} ${state}`}>
                <Icon className="h-4 w-4" strokeWidth={active ? 2.25 : 2} aria-hidden="true" />
              </Link>
            </FxTip>
            {/* ≥ md: pestaña con texto (la de siempre) */}
            <Link href={href} aria-current={current} className={`${SECTION_BASE} ${SECTION_TEXT} ${state}`}>
              <span className="truncate">{label}</span>
            </Link>
          </span>
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
        <div className="flex min-w-0 flex-1 items-center px-1 sm:px-2">
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
      {/* Avisos de sesión (sesion-larga §8.7): debajo de la píldora, mismo ancho; empujan el contenido. */}
      <SessionBanners maxWidthClass={maxWidthClass} />
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
