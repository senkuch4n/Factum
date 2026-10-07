"use client";

import { Tag } from "primereact/tag";
import { Ban, CheckCircle2, Clock, Lock } from "lucide-react";
import { accountState, formatClock, formatDateTime } from "@/lib/admin-accounts";
import type { AdminUser } from "@/types";

/**
 * Estado visible de la cuenta (abm-clientes §9.4): siempre texto + ícono,
 * nunca solo color. Prioridad: suspendida > bloqueada > pendiente > activa.
 */
export function AccountStatusBadge({ user }: { user: AdminUser }) {
  const state = accountState(user);
  const icon = "h-3 w-3 shrink-0";
  switch (state) {
    case "suspendida":
      return <Tag severity="danger" value="Suspendida" icon={<Ban className={icon} aria-hidden="true" />} />;
    case "bloqueada": {
      const until = user.locked_until ?? "";
      return (
        <span title={`Bloqueada por intentos fallidos hasta el ${formatDateTime(until)}`}>
          <Tag
            severity="warning"
            value={`Bloqueada hasta ${formatClock(until)}`}
            icon={<Lock className={icon} aria-hidden="true" />}
          />
        </span>
      );
    }
    case "pendiente":
      return (
        <Tag severity="info" value="Pendiente de primer ingreso" icon={<Clock className={icon} aria-hidden="true" />} />
      );
    default:
      return <Tag severity="success" value="Activa" icon={<CheckCircle2 className={icon} aria-hidden="true" />} />;
  }
}

/** Badge del rol: neutro para clientes, acento suave para superadmins. */
export function AccountRoleBadge({ role }: { role: AdminUser["role"] }) {
  return role === "superadmin"
    ? <Tag severity="secondary" value="Superadmin" className="border-fx-accent bg-fx-accent-soft text-fx-accent-text" />
    : <Tag severity="secondary" value="Cliente" />;
}
