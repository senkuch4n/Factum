"use client";

import { useId, useMemo, useRef, useState } from "react";
import { Menu } from "primereact/menu";
import type { MenuItem } from "primereact/menuitem";
import { ChevronDown, KeyRound, LogOut, Palette, UserCog, Users } from "lucide-react";
import { useMyBranding } from "@/hooks/useMyBranding";
import { usePublicConfig } from "@/hooks/usePublicConfig";
import { hasRealSigla } from "@/lib/format";

interface Props {
  user: { name: string; sigla: string; dni: string } | null;
  onLogout: () => void;
  /** Abre el diálogo "Mi perfil de perito". */
  onOpenProfile?: () => void;
  /** Abre el diálogo "Cambiar contraseña" (solo modo `local`). */
  onChangePassword?: () => void;
  /** Abre el diálogo "Marca del informe" (todos los modos, D7-A). */
  onOpenBranding?: () => void;
  /** Lleva al panel "Administrar cuentas" (solo superadmin en modo `local`). Sin la prop, no hay ítem. */
  onOpenAdmin?: () => void;
}

/**
 * Menú de usuario sobre el `Menu` popup de PrimeReact (estilo en
 * src/lib/prime/pt/menu.ts). Escape, click afuera, flechas (que saltean los
 * ítems informativos `disabled`) y el foco que vuelve al disparador los
 * resuelve Prime.
 */
export function UserMenu({ user, onLogout, onOpenProfile, onOpenBranding, onChangePassword, onOpenAdmin }: Props) {
  const menuRef = useRef<Menu>(null);
  const [open, setOpen] = useState(false);
  const menuId = `user-menu-${useId().replace(/:/g, "")}`;
  const publicConfig = usePublicConfig();
  const mine = useMyBranding(user?.dni ?? null);
  /** Logo que no cargó (por src: si cambia el logo, se vuelve a intentar). */
  const [failedLogoSrc, setFailedLogoSrc] = useState<string | null>(null);
  /*
   * Organización que se muestra (D5 / DT8): la marca de la cuenta si alguna vez
   * la guardó (aunque esté vacía: sus informes tampoco usan la instalación); si
   * no, la de la instalación (el fallback de sus informes). Mientras carga, nada,
   * para no mostrar una marca que no es la suya.
   */
  let organizationName: string | null = null;
  let organizationLogoSrc: string | null = null;
  if (mine.status === "ready" && mine.branding?.exists) {
    organizationName = mine.branding.organization_name.trim() || null;
    organizationLogoSrc = mine.logoSrc;
  } else if (mine.status === "ready" || mine.status === "error") {
    organizationName = publicConfig.organizationName;
    organizationLogoSrc = publicConfig.organizationLogoSrc;
  }

  const items = useMemo<MenuItem[]>(() => {
    if (!user) return [];
    const showOrgLogo = !!organizationLogoSrc && organizationLogoSrc !== failedLogoSrc;
    /* Organización emisora (Branding del backend): solo si hay nombre o logo. */
    const orgItem: MenuItem[] = organizationName || showOrgLogo
      ? [{
          disabled: true,
          className: "fx-menu-static",
          template: () => (
            <div className="flex min-w-0 items-center gap-2 border-t border-fx-border px-3.5 py-2.5">
              {showOrgLogo && (
                <img
                  src={organizationLogoSrc}
                  alt={organizationName ? "" : "Logo de la organización"}
                  // Fondo blanco: el logo está pensado para el informe (hoja blanca) y en tema oscuro se perdería.
                  className="h-5 w-auto max-w-[96px] shrink-0 rounded-fx-sm bg-white object-contain p-0.5"
                  onError={() => setFailedLogoSrc(organizationLogoSrc)}
                />
              )}
              {organizationName && (
                <p translate="no" className="min-w-0 truncate text-[11px] font-medium uppercase tracking-wide text-fx-text-3" title={organizationName}>
                  {organizationName}
                </p>
              )}
            </div>
          ),
        }]
      : [];
    return [
      {
        disabled: true,
        className: "fx-menu-static",
        template: () => (
          <div className="px-3.5 py-3">
            <p className="truncate text-[13px] font-semibold text-fx-text">{user.name}</p>
            <p className="mt-0.5 text-xs text-fx-text-3">
              DNI {user.dni}
              {hasRealSigla(user.sigla) && ` · ${user.sigla.trim()}`}
            </p>
          </div>
        ),
      },
      ...orgItem,
      { separator: true },
      ...(onOpenAdmin
        ? [{
            label: "Administrar cuentas",
            icon: <Users className="h-4 w-4" aria-hidden="true" />,
            command: () => onOpenAdmin(),
          }]
        : []),
      ...(onOpenProfile
        ? [{
            label: "Mi perfil de perito",
            icon: <UserCog className="h-4 w-4" aria-hidden="true" />,
            command: () => onOpenProfile(),
          }]
        : []),
      ...(onOpenBranding
        ? [{
            label: "Marca del informe",
            icon: <Palette className="h-4 w-4" aria-hidden="true" />,
            command: () => onOpenBranding(),
          }]
        : []),
      ...(onChangePassword
        ? [{
            label: "Cambiar contraseña",
            icon: <KeyRound className="h-4 w-4" aria-hidden="true" />,
            command: () => onChangePassword(),
          }]
        : []),
      {
        label: "Cerrar sesión",
        icon: <LogOut className="h-4 w-4" aria-hidden="true" />,
        className: "text-fx-danger",
        command: () => onLogout(),
      },
    ];
  }, [user, onLogout, onOpenProfile, onOpenBranding, onChangePassword, onOpenAdmin, organizationName, organizationLogoSrc, failedLogoSrc]);

  if (!user) return null;

  const initials = user.name.split(" ").filter(Boolean).map(w => w[0]).slice(0, 2).join("").toUpperCase();

  return (
    <>
      {/* Tapa clara derecha de la navbar flotante (ver AppNavbar): iniciales
          siempre; nombre y chevron desde lg, donde hay lugar. */}
      <button
        type="button"
        onClick={(e) => menuRef.current?.toggle(e)}
        className="fx-nav-cap group flex h-10 min-w-0 shrink-0 items-center gap-2 rounded-full p-1 lg:pr-3 fx-focus-ring"
        title={user.name}
        aria-label={`Menú de usuario: ${user.name}`}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? `${menuId}_list` : undefined}
      >
        <span
          aria-hidden="true"
          className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[var(--fx-nav-cap-avatar)] text-[11px] font-semibold"
        >
          {initials}
        </span>
        <span aria-hidden="true" className="hidden max-w-[12rem] truncate text-sm font-medium lg:block">
          {user.name}
        </span>
        <ChevronDown
          aria-hidden="true"
          className="hidden h-3.5 w-3.5 shrink-0 text-[color:var(--fx-nav-cap-text-2)] transition-transform duration-fx-fast ease-fx group-aria-expanded:rotate-180 motion-reduce:transition-none lg:block"
        />
      </button>
      <Menu
        ref={menuRef}
        id={menuId}
        model={items}
        popup
        popupAlignment="right"
        pt={{ menu: { "aria-label": "Menú de usuario" } }}
        onShow={() => setOpen(true)}
        onHide={() => setOpen(false)}
      />
    </>
  );
}
