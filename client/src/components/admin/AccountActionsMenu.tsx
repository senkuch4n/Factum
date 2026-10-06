"use client";

import { useId, useMemo, useRef, useState } from "react";
import { Menu } from "primereact/menu";
import type { MenuItem } from "primereact/menuitem";
import { Ban, KeyRound, LockOpen, MoreHorizontal, Pencil, RotateCcw } from "lucide-react";
import { cn } from "@/lib/utils";
import type { AdminUser } from "@/types";

export type AccountAction = "edit" | "reset" | "unlock" | "suspend" | "reactivate";

/** Acciones disponibles para una cuenta, con las protecciones de D4 (abm-clientes §6.2). */
export interface AccountActionAvailability {
  canReset: boolean;
  canUnlock: boolean;
  canSuspend: boolean;
  canReactivate: boolean;
  /** Explicación cuando una acción está deshabilitada por ser la propia cuenta. */
  selfNote: string | null;
}

export const SELF_ACTIONS_NOTE =
  "No podés suspender ni resetear la contraseña de tu propia cuenta. Para cambiarla usá “Cambiar contraseña” en tu menú.";

export function accountActions(user: AdminUser, isSelf: boolean): AccountActionAvailability {
  const suspended = user.status === "suspendido";
  return {
    canReset: !isSelf,
    canUnlock: user.locked_until !== null,
    canSuspend: !suspended && !isSelf,
    canReactivate: suspended,
    selfNote: isSelf ? SELF_ACTIONS_NOTE : null,
  };
}

interface Props {
  user: AdminUser;
  isSelf: boolean;
  onAction: (action: AccountAction, user: AdminUser) => void;
  className?: string;
}

/**
 * Menú "⋯" de una cuenta (`Menu` popup de Prime: teclado, Escape y foco que
 * vuelve al disparador). En la fila propia "Resetear contraseña" y
 * "Suspender" quedan deshabilitados con la explicación al pie (D4).
 * "Desbloquear" solo aparece con un bloqueo vigente.
 */
export function AccountActionsMenu({ user, isSelf, onAction, className }: Props) {
  const menuRef = useRef<Menu>(null);
  const [open, setOpen] = useState(false);
  const menuId = `account-menu-${useId().replace(/:/g, "")}`;

  const items = useMemo<MenuItem[]>(() => {
    const a = accountActions(user, isSelf);
    const icon = (I: React.ElementType) => <I className="h-4 w-4" aria-hidden="true" />;
    const list: MenuItem[] = [
      { label: "Editar", icon: icon(Pencil), command: () => onAction("edit", user) },
      {
        label: "Resetear contraseña",
        icon: icon(KeyRound),
        disabled: !a.canReset,
        command: () => onAction("reset", user),
      },
    ];
    if (a.canUnlock) list.push({ label: "Desbloquear", icon: icon(LockOpen), command: () => onAction("unlock", user) });
    if (a.canReactivate) {
      list.push({ label: "Reactivar", icon: icon(RotateCcw), command: () => onAction("reactivate", user) });
    } else {
      list.push({
        label: "Suspender",
        icon: icon(Ban),
        className: a.canSuspend ? "text-fx-danger" : undefined,
        disabled: !a.canSuspend,
        command: () => onAction("suspend", user),
      });
    }
    if (a.selfNote) {
      list.push(
        { separator: true },
        {
          disabled: true,
          className: "fx-menu-static",
          template: () => (
            <p className="m-0 max-w-[16rem] px-3.5 py-2 text-xs leading-snug text-fx-text-3">{a.selfNote}</p>
          ),
        },
      );
    }
    return list;
  }, [user, isSelf, onAction]);

  // El wrapper corta la propagación: los eventos del popup (portal) también
  // burbujean por el árbol de React y abrirían el detalle de la fila.
  return (
    <span className="inline-flex" onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()}>
      <button
        type="button"
        onClick={(e) => menuRef.current?.toggle(e)}
        className={cn(
          "inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-fx-md text-fx-text-2 md:h-8 md:w-8",
          "transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-3 hover:text-fx-text fx-focus-ring",
          className,
        )}
        aria-label={`Acciones para ${user.name}`}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? `${menuId}_list` : undefined}
      >
        <MoreHorizontal className="h-4 w-4" aria-hidden="true" />
      </button>
      <Menu
        ref={menuRef}
        id={menuId}
        model={items}
        popup
        popupAlignment="right"
        pt={{ menu: { "aria-label": `Acciones para ${user.name}` } }}
        onShow={() => setOpen(true)}
        onHide={() => setOpen(false)}
      />
    </span>
  );
}
