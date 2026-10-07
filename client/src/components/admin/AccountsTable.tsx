"use client";

import { Button } from "primereact/button";
import { Building2, SearchX, UserPlus, Users } from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";
import { formatDateTime, formatRelative } from "@/lib/admin-accounts";
import { cn } from "@/lib/utils";
import type { AdminUser } from "@/types";
import { AccountActionsMenu, type AccountAction } from "./AccountActionsMenu";
import { AccountRoleBadge, AccountStatusBadge } from "./AccountStatusBadge";

interface Props {
  /** Cuentas ya filtradas y ordenadas. */
  users: AdminUser[];
  /** Total sin filtrar (para distinguir "vacío" de "sin resultados"). */
  totalCount: number;
  /** `true` si no hay ninguna cuenta con rol cliente (solo superadmins). */
  noClients: boolean;
  filtersActive: boolean;
  loading: boolean;
  /** Texto del error de carga, o `null`. */
  error: string | null;
  selfDni: string;
  onOpen: (user: AdminUser) => void;
  onAction: (action: AccountAction, user: AdminUser) => void;
  onCreate: () => void;
  onClearFilters: () => void;
  onRetry: () => void;
}

const SKELETON_ROWS = 5;

function LastLogin({ iso, now }: { iso: string | null; now: Date }) {
  if (!iso) return <span className="text-fx-text-3">Nunca</span>;
  return <time dateTime={iso} title={formatDateTime(iso)}>{formatRelative(iso, now)}</time>;
}

function NameCell({ user, isSelf }: { user: AdminUser; isSelf: boolean }) {
  return (
    <span className="flex min-w-0 flex-col">
      <span className="truncate font-semibold text-fx-text">
        {user.name}
        {isSelf && <span className="ml-1.5 font-normal text-fx-text-3">(vos)</span>}
      </span>
      {user.organization && (
        <span className="mt-0.5 flex min-w-0 items-center gap-1 text-xs font-normal text-fx-text-3">
          <Building2 className="h-3 w-3 shrink-0" aria-hidden="true" />
          <span className="truncate">{user.organization}</span>
        </span>
      )}
    </span>
  );
}

function caseCountLabel(n: number) {
  return `${n.toLocaleString("es-AR")} ${n === 1 ? "caso" : "casos"}`;
}

/**
 * Listado de cuentas (abm-clientes §9.4): `<table>` semántica desde `md` y
 * tarjetas apiladas en mobile, con los mismos datos. Click o Enter en la fila
 * abre el detalle; el menú "⋯" corta la propagación.
 */
export function AccountsTable({
  users, totalCount, noClients, filtersActive, loading, error, selfDni,
  onOpen, onAction, onCreate, onClearFilters, onRetry,
}: Props) {
  const now = new Date();

  if (loading) {
    return (
      <div role="status" aria-label="Cargando cuentas" className="overflow-hidden rounded-fx-lg border border-fx-border bg-fx-surface-1">
        {Array.from({ length: SKELETON_ROWS }, (_, i) => (
          <div key={i} className="flex items-center gap-4 border-b border-fx-border px-4 py-4 last:border-b-0">
            <div className="h-4 w-40 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
            <div className="hidden h-4 w-20 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse md:block" />
            <div className="hidden h-5 w-16 rounded-fx-pill bg-fx-surface-3 motion-safe:animate-pulse md:block" />
            <div className="ml-auto h-5 w-24 rounded-fx-pill bg-fx-surface-3 motion-safe:animate-pulse" />
          </div>
        ))}
        <span className="sr-only">Cargando cuentas…</span>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col items-start gap-3">
        <FxBanner tone="error">{error}</FxBanner>
        <Button label="Reintentar" severity="secondary" onClick={onRetry} />
      </div>
    );
  }

  if (users.length === 0 && totalCount > 0 && filtersActive) {
    return (
      <div className="flex flex-col items-center gap-3 rounded-fx-lg border border-dashed border-fx-border-strong bg-fx-surface-1 px-6 py-12 text-center">
        <SearchX className="h-8 w-8 text-fx-text-3" aria-hidden="true" />
        <p className="m-0 text-fx-body font-semibold text-fx-text" role="status">
          No hay cuentas que coincidan con la búsqueda
        </p>
        <p className="m-0 text-fx-body-sm text-fx-text-2">Probá con otra parte del nombre o del DNI, o cambiá los filtros.</p>
        <Button label="Limpiar filtros" severity="secondary" onClick={onClearFilters} />
      </div>
    );
  }

  const emptyClients = noClients && !filtersActive && (
    <div className="mb-4 flex flex-col items-center gap-3 rounded-fx-lg border border-dashed border-fx-border-strong bg-fx-surface-1 px-6 py-10 text-center">
      <Users className="h-8 w-8 text-fx-text-3" aria-hidden="true" />
      <p className="m-0 text-fx-body font-semibold text-fx-text">Todavía no hay clientes. Creá la primera cuenta.</p>
      <Button label="Nueva cuenta" icon={<UserPlus className="h-4 w-4" aria-hidden="true" />} onClick={onCreate} />
    </div>
  );

  if (users.length === 0) return <>{emptyClients}</>;

  return (
    <>
      {emptyClients}

      {/* Escritorio: tabla semántica */}
      <div className="hidden overflow-x-auto rounded-fx-lg border border-fx-border bg-fx-surface-1 md:block">
        <table className="w-full border-collapse text-left text-fx-body-sm">
          <caption className="sr-only">
            Cuentas de Factum ({users.length}). Elegí una fila para ver el detalle.
          </caption>
          <thead className="border-b border-fx-border bg-fx-surface-2">
            <tr>
              {["Nombre", "DNI", "Rol", "Estado", "Último ingreso", "Casos"].map(h => (
                <th key={h} scope="col" className={cn(
                  "whitespace-nowrap px-4 py-2.5 text-fx-label uppercase text-fx-text-3",
                  h === "Casos" && "text-right",
                )}>
                  {h}
                </th>
              ))}
              <th scope="col" className="w-12 px-2 py-2.5"><span className="sr-only">Acciones</span></th>
            </tr>
          </thead>
          <tbody>
            {users.map(u => {
              const isSelf = u.dni === selfDni;
              return (
                <tr
                  key={u.id}
                  tabIndex={0}
                  onClick={() => onOpen(u)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && e.target === e.currentTarget) { e.preventDefault(); onOpen(u); }
                  }}
                  className="cursor-pointer border-b border-fx-border transition-colors duration-fx-fast ease-fx last:border-b-0 hover:bg-fx-surface-2 focus-visible:bg-fx-surface-2 focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-fx-focus"
                >
                  <th scope="row" className="max-w-[18rem] px-4 py-3 font-normal">
                    <NameCell user={u} isSelf={isSelf} />
                  </th>
                  <td className="whitespace-nowrap px-4 py-3 tabular-nums text-fx-text-2" translate="no">{u.dni}</td>
                  <td className="px-4 py-3"><AccountRoleBadge role={u.role} /></td>
                  <td className="px-4 py-3"><AccountStatusBadge user={u} /></td>
                  <td className="whitespace-nowrap px-4 py-3 text-fx-text-2"><LastLogin iso={u.last_login_at} now={now} /></td>
                  <td className="px-4 py-3 text-right tabular-nums text-fx-text-2">{u.case_count.toLocaleString("es-AR")}</td>
                  <td className="px-2 py-2 text-right">
                    <AccountActionsMenu user={u} isSelf={isSelf} onAction={onAction} />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Mobile: tarjetas apiladas */}
      <ul className="m-0 flex list-none flex-col gap-2 p-0 md:hidden" aria-label={`Cuentas de Factum (${users.length})`}>
        {users.map(u => {
          const isSelf = u.dni === selfDni;
          return (
            <li key={u.id} className="flex items-start gap-1 rounded-fx-lg border border-fx-border bg-fx-surface-1">
              <button
                type="button"
                onClick={() => onOpen(u)}
                className="flex min-w-0 flex-1 flex-col gap-2 rounded-fx-lg px-4 py-3 text-left transition-colors duration-fx-fast ease-fx hover:bg-fx-surface-2 fx-focus-ring"
                aria-label={`Ver detalle de ${u.name}, DNI ${u.dni}`}
              >
                <NameCell user={u} isSelf={isSelf} />
                <span className="flex flex-wrap items-center gap-1.5">
                  <AccountRoleBadge role={u.role} />
                  <AccountStatusBadge user={u} />
                </span>
                <span className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-fx-text-2">
                  <span translate="no">DNI {u.dni}</span>
                  <span>Último ingreso: <LastLogin iso={u.last_login_at} now={now} /></span>
                  <span>{caseCountLabel(u.case_count)}</span>
                </span>
              </button>
              <AccountActionsMenu user={u} isSelf={isSelf} onAction={onAction} className="mr-1 mt-1" />
            </li>
          );
        })}
      </ul>
    </>
  );
}
