"use client";

import { useRef, useState } from "react";
import { Menu } from "primereact/menu";
import type { MenuItem } from "primereact/menuitem";
import { Check, Tag } from "lucide-react";
import type { CaptureRoleValue } from "@/lib/api";
import { CAPTURE_ROLE_LABELS } from "@/lib/pericial";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";

/**
 * "Marcar como…": Menu popup de PrimeReact (teclado, Escape y foco de vuelta
 * los resuelve Prime). Una sola etiqueta por captura. Misma lógica de siempre;
 * solo cambia el disparador.
 */
export function CaptureRoleMenu({
  filename, role, onChange,
}: { filename: string; role?: CaptureRoleValue; onChange: (role: CaptureRoleValue | null) => void }) {
  const menuRef = useRef<Menu>(null);
  const [open, setOpen] = useState(false);
  const menuId = "capture-role-menu";

  const items: MenuItem[] = [
    ...(["imei_modelo", "nombre_dispositivo"] as const).map(r => ({
      label: CAPTURE_ROLE_LABELS[r],
      icon: role === r
        ? <Check className="h-4 w-4" aria-hidden="true" />
        : <span className="inline-block h-4 w-4" aria-hidden="true" />,
      command: () => onChange(r),
    })),
    { separator: true },
    {
      label: "Sin marca",
      icon: !role
        ? <Check className="h-4 w-4" aria-hidden="true" />
        : <span className="inline-block h-4 w-4" aria-hidden="true" />,
      command: () => onChange(null),
    },
  ];

  return (
    <>
      <button
        type="button"
        onClick={e => menuRef.current?.toggle(e)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? `${menuId}_list` : undefined}
        aria-label={`Marcar como… ${filename}${role ? ` (actual: ${CAPTURE_ROLE_LABELS[role]})` : ""}`}
        className={cn(
          "inline-flex min-h-8 shrink-0 items-center gap-1 rounded-fx-md border px-2 py-1 text-xs font-semibold",
          "transition-colors duration-fx-fast ease-fx",
          FOCUS_RING,
          role
            ? "border-fx-accent bg-fx-accent-soft text-fx-accent-text"
            : "border-fx-border-strong text-fx-text-2 hover:bg-fx-surface-3 hover:text-fx-text",
        )}
      >
        <Tag className="h-3 w-3" aria-hidden="true" />
        {role ? CAPTURE_ROLE_LABELS[role] : "Marcar como…"}
      </button>
      <Menu
        ref={menuRef}
        id={menuId}
        model={items}
        popup
        pt={{ menu: { "aria-label": "Marcar captura como" } }}
        onShow={() => setOpen(true)}
        onHide={() => setOpen(false)}
      />
    </>
  );
}
