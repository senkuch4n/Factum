"use client";

import { useId } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { Ban, Info, KeyRound, LockOpen, Pencil, RotateCcw } from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";
import { formatDateTime, formatRelative } from "@/lib/admin-accounts";
import { hasRealSigla } from "@/lib/format";
import type { AdminUser } from "@/types";
import { accountActions, type AccountAction } from "./AccountActionsMenu";
import { AccountHistory } from "./AccountHistory";
import { AccountRoleBadge, AccountStatusBadge } from "./AccountStatusBadge";

interface Props {
  /** `null` = cerrado. */
  user: AdminUser | null;
  selfDni: string;
  onHide: () => void;
  onAction: (action: AccountAction, user: AdminUser) => void;
  /** Cambia después de cada acción para recargar el historial. */
  historyKey: number;
  /** Error de la última acción sobre esta cuenta (`role="alert"`), o `null`. */
  actionError: string | null;
  onDismissError: () => void;
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[minmax(0,9rem)_minmax(0,1fr)] gap-3 py-2">
      <dt className="text-xs font-medium text-fx-text-3">{label}</dt>
      <dd className="m-0 min-w-0 break-words text-fx-body-sm text-fx-text">{children}</dd>
    </div>
  );
}

const Empty = () => <span className="text-fx-text-3">—</span>;

/** "06/10/2026, 14:32 · por Leo (DNI 20222222)". */
function WhenBy({ iso, name, dni }: { iso: string; name: string | null; dni: string | null }) {
  const who = dni === "bootstrap"
    ? "configuración inicial"
    : name ? `${name}${dni ? ` (DNI ${dni})` : ""}` : dni ? `DNI ${dni}` : null;
  return (
    <>
      <time dateTime={iso}>{formatDateTime(iso)}</time>
      {who && <span className="text-fx-text-2"> · por {who}</span>}
    </>
  );
}

/**
 * Detalle de una cuenta (panel lateral; pantalla completa en mobile): datos,
 * contacto, notas, fechas, suspensión vigente, las mismas acciones del menú
 * y el historial (abm-clientes §9.4).
 */
export function AccountDetailDialog({ user, selfDni, onHide, onAction, historyKey, actionError, onDismissError }: Props) {
  const noteId = `${useId()}-self-note`;
  const isSelf = !!user && user.dni === selfDni;
  const a = user ? accountActions(user, isSelf) : null;

  return (
    <Dialog
      visible={user !== null}
      onHide={onHide}
      position="right"
      modal
      dismissableMask
      draggable={false}
      resizable={false}
      header={user ? <span className="block truncate">{user.name}{isSelf && <span className="ml-1.5 font-normal text-fx-text-3">(vos)</span>}</span> : ""}
    >
      {user && a && (
        <div className="flex flex-col gap-6">
          <div className="flex flex-wrap items-center gap-1.5">
            <AccountRoleBadge role={user.role} />
            <AccountStatusBadge user={user} />
          </div>

          {actionError && <FxBanner tone="error" onClose={onDismissError}>{actionError}</FxBanner>}

          <div className="flex flex-col gap-2">
            <div className="flex flex-wrap gap-2">
              <Button
                size="small" severity="secondary" label="Editar"
                icon={<Pencil className="h-3.5 w-3.5" aria-hidden="true" />}
                onClick={() => onAction("edit", user)}
              />
              <Button
                size="small" severity="secondary" label="Resetear contraseña"
                icon={<KeyRound className="h-3.5 w-3.5" aria-hidden="true" />}
                disabled={!a.canReset}
                aria-describedby={a.selfNote ? noteId : undefined}
                onClick={() => onAction("reset", user)}
              />
              {a.canUnlock && (
                <Button
                  size="small" severity="secondary" label="Desbloquear"
                  icon={<LockOpen className="h-3.5 w-3.5" aria-hidden="true" />}
                  onClick={() => onAction("unlock", user)}
                />
              )}
              {a.canReactivate ? (
                <Button
                  size="small" severity="secondary" label="Reactivar"
                  icon={<RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
                  onClick={() => onAction("reactivate", user)}
                />
              ) : (
                <Button
                  size="small" severity="danger" outlined label="Suspender"
                  icon={<Ban className="h-3.5 w-3.5" aria-hidden="true" />}
                  disabled={!a.canSuspend}
                  aria-describedby={a.selfNote ? noteId : undefined}
                  onClick={() => onAction("suspend", user)}
                />
              )}
            </div>
            {a.selfNote && (
              <p id={noteId} className="m-0 flex items-start gap-1.5 text-xs leading-snug text-fx-text-3">
                <Info className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                {a.selfNote}
              </p>
            )}
          </div>

          {user.status === "suspendido" && (
            <section aria-label="Suspensión" className="rounded-fx-lg border border-fx-danger bg-fx-danger-soft px-4 py-3">
              <p className="m-0 flex items-center gap-1.5 text-fx-body-sm font-semibold text-fx-danger">
                <Ban className="h-4 w-4" aria-hidden="true" /> Cuenta suspendida
              </p>
              <dl className="m-0 mt-1">
                {user.suspended_at && (
                  <Row label="Cuándo y quién">
                    <WhenBy iso={user.suspended_at} name={user.suspended_by_name} dni={user.suspended_by} />
                  </Row>
                )}
                <Row label="Motivo">
                  {user.suspension_reason
                    ? <span className="whitespace-pre-wrap">{user.suspension_reason}</span>
                    : <span className="text-fx-text-3">Sin motivo</span>}
                </Row>
              </dl>
            </section>
          )}

          <section aria-labelledby={`${noteId}-data`}>
            <h3 id={`${noteId}-data`} className="m-0 mb-1 text-fx-label uppercase text-fx-text-3">Datos</h3>
            <dl className="m-0 divide-y divide-fx-border">
              <Row label="DNI"><span translate="no" className="tabular-nums">{user.dni}</span></Row>
              <Row label="Sigla">{hasRealSigla(user.sigla) ? user.sigla : <Empty />}</Row>
              <Row label="Organización">{user.organization || <Empty />}</Row>
              <Row label="Teléfono">
                {user.contact_phone ? <a href={`tel:${user.contact_phone.replace(/[^\d+]/g, "")}`} className="text-fx-accent-text underline-offset-2 hover:underline fx-focus-ring">{user.contact_phone}</a> : <Empty />}
              </Row>
              <Row label="Email de contacto">
                {user.contact_email ? <a href={`mailto:${user.contact_email}`} className="break-all text-fx-accent-text underline-offset-2 hover:underline fx-focus-ring">{user.contact_email}</a> : <Empty />}
              </Row>
              <Row label="Casos">{user.case_count.toLocaleString("es-AR")}</Row>
            </dl>
          </section>

          <section aria-labelledby={`${noteId}-notes`}>
            <h3 id={`${noteId}-notes`} className="m-0 mb-1 text-fx-label uppercase text-fx-text-3">Notas internas</h3>
            {user.notes
              ? <p className="m-0 whitespace-pre-wrap break-words rounded-fx-md bg-fx-surface-2 px-3 py-2 text-fx-body-sm text-fx-text">{user.notes}</p>
              : <p className="m-0 text-fx-body-sm text-fx-text-3">Sin notas.</p>}
          </section>

          <section aria-labelledby={`${noteId}-dates`}>
            <h3 id={`${noteId}-dates`} className="m-0 mb-1 text-fx-label uppercase text-fx-text-3">Fechas</h3>
            <dl className="m-0 divide-y divide-fx-border">
              <Row label="Alta"><WhenBy iso={user.created_at} name={user.created_by_name} dni={user.created_by} /></Row>
              <Row label="Último ingreso">
                {user.last_login_at
                  ? <><time dateTime={user.last_login_at}>{formatDateTime(user.last_login_at)}</time> <span className="text-fx-text-2">({formatRelative(user.last_login_at)})</span></>
                  : <span className="text-fx-text-3">Nunca</span>}
              </Row>
              <Row label="Última modificación"><time dateTime={user.updated_at}>{formatDateTime(user.updated_at)}</time></Row>
            </dl>
          </section>

          <AccountHistory userId={user.id} refreshKey={historyKey} />
        </div>
      )}
    </Dialog>
  );
}
