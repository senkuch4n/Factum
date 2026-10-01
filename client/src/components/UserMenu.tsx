"use client";

import { useId, useMemo, useRef, useState } from "react";
import { Menu } from "primereact/menu";
import type { MenuItem } from "primereact/menuitem";
import { LogOut } from "lucide-react";

interface Props {
  user: { name: string; sigla: string; dni: string } | null;
  onLogout: () => void;
}

/**
 * Menú de usuario sobre el `Menu` popup de PrimeReact (estilo en
 * src/lib/prime/pt/menu.ts). Escape, click afuera, flechas (que saltean los
 * ítems informativos `disabled`) y el foco que vuelve al disparador los
 * resuelve Prime.
 */
export function UserMenu({ user, onLogout }: Props) {
  const menuRef = useRef<Menu>(null);
  const [open, setOpen] = useState(false);
  const menuId = `user-menu-${useId().replace(/:/g, "")}`;

  const items = useMemo<MenuItem[]>(() => {
    if (!user) return [];
    return [
      {
        disabled: true,
        className: "fx-menu-static",
        template: () => (
          <div className="px-3.5 py-3">
            <p className="truncate text-[13px] font-semibold text-fx-text">{user.name}</p>
            <p className="mt-0.5 text-xs text-fx-text-3">
              DNI {user.dni} · {user.sigla}
            </p>
          </div>
        ),
      },
      {
        disabled: true,
        className: "fx-menu-static",
        template: () => (
          <div className="flex items-center gap-2 border-t border-fx-border px-3.5 py-2.5">
            <img src="/mpfs.png" alt="MPF" className="h-4 w-auto shrink-0 opacity-70" />
            <p className="text-[11px] font-medium uppercase tracking-wide text-fx-text-3">
              Min. Público Fiscal · Salta
            </p>
          </div>
        ),
      },
      { separator: true },
      {
        label: "Cerrar sesión",
        icon: <LogOut className="h-4 w-4" aria-hidden="true" />,
        className: "text-fx-danger",
        command: () => onLogout(),
      },
    ];
  }, [user, onLogout]);

  if (!user) return null;

  const initials = user.name.split(" ").filter(Boolean).map(w => w[0]).slice(0, 2).join("").toUpperCase();

  return (
    <>
      <button
        type="button"
        onClick={(e) => menuRef.current?.toggle(e)}
        className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-fx-border-strong bg-fx-surface-2 text-[11px] font-semibold text-fx-text-2 transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring"
        title={user.name}
        aria-label={`Menú de usuario: ${user.name}`}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? `${menuId}_list` : undefined}
      >
        {initials}
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
