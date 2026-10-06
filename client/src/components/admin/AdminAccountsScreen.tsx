"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button } from "primereact/button";
import { ArrowLeft, UserPlus } from "lucide-react";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { FxBanner } from "@/components/feedback/FxBanner";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";
import { useFxToast } from "@/components/shell/FxToastProvider";
import { useAuth } from "@/hooks/useAuth";
import { useAuthMode } from "@/hooks/useAuthMode";
import { useAdminAccounts } from "@/hooks/useAdminAccounts";
import { setMyBranding } from "@/hooks/useMyBranding";
import { api } from "@/lib/api";
import {
  EMPTY_FILTERS, adminErrorCode, adminErrorMessage, hasActiveFilters, isAdminAccessError, matchesFilters,
  type AccountFilters,
} from "@/lib/admin-accounts";
import type { AdminUser, AdminUserWithPassword } from "@/types";
import type { AccountAction } from "./AccountActionsMenu";
import { BrandingDialog, type BrandingTarget } from "@/components/branding/BrandingDialog";
import { AccountDetailDialog } from "./AccountDetailDialog";
import { AccountFormDialog } from "./AccountFormDialog";
import { AccountsTable } from "./AccountsTable";
import { AccountsToolbar } from "./AccountsToolbar";
import { SuspendAccountDialog } from "./SuspendAccountDialog";
import { TemporaryPasswordDialog, type TemporaryPasswordReveal } from "./TemporaryPasswordDialog";

type PendingConfirm = { kind: "reactivate" | "reset"; user: AdminUser } | null;

function countLabel(total: number, suspended: number) {
  const accounts = `${total} ${total === 1 ? "cuenta" : "cuentas"}`;
  if (suspended === 0) return accounts;
  return `${accounts} · ${suspended} ${suspended === 1 ? "suspendida" : "suspendidas"}`;
}

/** Skeleton de la pantalla mientras se resuelven la sesión y el modo. */
function ScreenSkeleton() {
  return (
    <div role="status" aria-label="Cargando" className="mx-auto w-full max-w-[1400px] px-4 py-6 sm:px-6">
      <div className="h-8 w-48 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
      <div className="mt-6 h-10 w-full max-w-md rounded-fx-md bg-fx-surface-3 motion-safe:animate-pulse" />
      <div className="mt-6 h-64 w-full rounded-fx-lg bg-fx-surface-2 motion-safe:animate-pulse" />
      <span className="sr-only">Cargando…</span>
    </div>
  );
}

/**
 * Panel "Administrar cuentas" (`/admin/cuentas`, abm-clientes §9.4). Solo
 * para superadmins en modo `local`: con otro rol o modo vuelve al dashboard
 * sin llamar a la API de admin (la seguridad real es el 403/404 del backend).
 */
export function AdminAccountsScreen() {
  const router = useRouter();
  const toast = useFxToast();
  const { user, handleLogout } = useAuth({ withHistory: false });
  const { mode } = useAuthMode();

  const allowed = !!user && user.role === "superadmin" && mode === "local";
  const denied = !!user && (user.role !== "superadmin" || (mode !== null && mode !== "local"));

  useEffect(() => {
    if (denied) router.replace("/dashboard");
  }, [denied, router]);

  const accounts = useAdminAccounts(allowed);
  const { users, loading, error: loadError, reload, upsert, refreshOne } = accounts;

  useEffect(() => {
    if (isAdminAccessError(loadError)) router.replace("/dashboard");
  }, [loadError, router]);

  // ── Filtros ──
  const [filters, setFilters] = useState<AccountFilters>(EMPTY_FILTERS);
  const filtersActive = hasActiveFilters(filters);
  const filtered = useMemo(() => users.filter(u => matchesFilters(u, filters)), [users, filters]);
  const suspendedCount = useMemo(() => users.filter(u => u.status === "suspendido").length, [users]);
  const noClients = useMemo(() => !users.some(u => u.role === "cliente"), [users]);
  const clearFilters = useCallback(() => setFilters(EMPTY_FILTERS), []);

  // ── Diálogos ──
  const [formOpen, setFormOpen] = useState(false);
  const [formUser, setFormUser] = useState<AdminUser | null>(null);
  const [detailId, setDetailId] = useState<string | null>(null);
  const [historyKey, setHistoryKey] = useState(0);
  const [suspendTarget, setSuspendTarget] = useState<AdminUser | null>(null);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm>(null);
  const [reveal, setReveal] = useState<TemporaryPasswordReveal | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  /** Cuenta cuya marca se edita ("Editar marca"), o `null`. */
  const [brandingTarget, setBrandingTarget] = useState<Extract<BrandingTarget, { kind: "account" }> | null>(null);
  /** Aparte del target: así el diálogo se cierra con su animación y Prime devuelve el foco. */
  const [brandingOpen, setBrandingOpen] = useState(false);
  const inFlight = useRef(false);

  const detailUser = useMemo(() => users.find(u => u.id === detailId) ?? null, [users, detailId]);

  const openDetail = useCallback((u: AdminUser) => {
    setActionError(null);
    setDetailId(u.id);
  }, []);

  /** Después de una acción exitosa: fila nueva y, si el detalle está abierto, historial recargado. */
  const applied = useCallback((u: AdminUser) => {
    upsert(u);
    setActionError(null);
    setHistoryKey(k => k + 1);
  }, [upsert]);

  /** Error de una acción: texto de la API y la fila queda como estaba (o se refresca). */
  const failed = useCallback((err: unknown, target: AdminUser): string => {
    if (isAdminAccessError(err)) { router.replace("/dashboard"); return adminErrorMessage(err); }
    const code = adminErrorCode(err);
    if (code === "invalid_state") void refreshOne(target.id).then(() => setHistoryKey(k => k + 1));
    if (code === "user_not_found") { void reload(); setDetailId(null); }
    return adminErrorMessage(err);
  }, [refreshOne, reload, router]);

  const showReveal = useCallback((res: AdminUserWithPassword) => {
    setReveal({ name: res.user.name, dni: res.user.dni, password: res.temporary_password });
  }, []);

  async function runUnlock(target: AdminUser) {
    if (inFlight.current) return;
    inFlight.current = true;
    try {
      const u = await api.adminUnlockUser(target.id);
      applied(u);
      toast.success("Cuenta desbloqueada", u.name);
    } catch (err) {
      setActionError(failed(err, target));
    } finally {
      inFlight.current = false;
    }
  }

  async function runReactivate(target: AdminUser) {
    if (inFlight.current) return;
    inFlight.current = true;
    try {
      const u = await api.adminReactivateUser(target.id);
      applied(u);
      toast.success("Cuenta reactivada", u.name);
    } catch (err) {
      setActionError(failed(err, target));
    } finally {
      inFlight.current = false;
    }
  }

  async function runReset(target: AdminUser) {
    if (inFlight.current) return;
    inFlight.current = true;
    try {
      const res = await api.adminResetPassword(target.id);
      applied(res.user);
      toast.success("Contraseña reseteada", "Se cerraron sus sesiones abiertas.");
      showReveal(res);
    } catch (err) {
      setActionError(failed(err, target));
    } finally {
      inFlight.current = false;
    }
  }

  async function runSuspend(reason: string): Promise<string | null> {
    const target = suspendTarget;
    if (!target) return null;
    try {
      const u = await api.adminSuspendUser(target.id, reason || undefined);
      applied(u);
      setSuspendTarget(null);
      toast.success("Cuenta suspendida", u.name);
      return null;
    } catch (err) {
      return failed(err, target);
    }
  }

  function handleAction(action: AccountAction, target: AdminUser) {
    setActionError(null);
    switch (action) {
      case "edit":
        setFormUser(target);
        setFormOpen(true);
        break;
      case "branding":
        setBrandingTarget({ kind: "account", userId: target.id, name: target.name, dni: target.dni });
        setBrandingOpen(true);
        break;
      case "reset":
        setPendingConfirm({ kind: "reset", user: target });
        break;
      case "reactivate":
        setPendingConfirm({ kind: "reactivate", user: target });
        break;
      case "suspend":
        setSuspendTarget(target);
        break;
      case "unlock":
        void runUnlock(target);
        break;
    }
  }

  function openCreate() {
    setFormUser(null);
    setFormOpen(true);
  }

  function viewExisting(id: string) {
    setFormOpen(false);
    setActionError(null);
    if (users.some(u => u.id === id)) setDetailId(id);
    else void refreshOne(id).then(u => { if (u) setDetailId(u.id); });
  }

  if (!user || !allowed) {
    return (
      <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
        <AppNavbar user={user} showThemeSwitch brandHref="/dashboard" />
        <ScreenSkeleton />
      </div>
    );
  }

  const listErrorText = loadError && !isAdminAccessError(loadError) ? adminErrorMessage(loadError) : null;

  return (
    <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
      <AppNavbar
        user={user}
        onLogout={handleLogout}
        showThemeSwitch
        brandHref="/dashboard"
        center={
          <nav aria-label="Ruta" className="flex min-w-0 items-center gap-1.5 text-sm">
            <span aria-hidden="true" className="inline-block h-[5px] w-[5px] shrink-0 bg-fx-accent" />
            <span className="truncate font-medium text-fx-text">Administrar cuentas</span>
          </nav>
        }
      />

      <main className="mx-auto w-full max-w-[1400px] flex-1 px-4 pb-16 pt-5 sm:px-6">
        <Link
          href="/dashboard"
          className="-ml-1 inline-flex min-h-11 items-center gap-1.5 rounded-fx-md px-1 text-fx-body-sm font-medium text-fx-text-2 no-underline transition-colors duration-fx-fast ease-fx hover:text-fx-text fx-focus-ring md:min-h-8"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" /> Volver al dashboard
        </Link>

        <header className="mt-3 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div className="min-w-0">
            <h1 className="m-0 text-balance text-fx-h2 text-fx-text">Cuentas</h1>
            <p className="m-0 mt-1 text-fx-body-sm text-fx-text-2">
              {loading ? "Cargando…" : countLabel(users.length, suspendedCount)}
            </p>
          </div>
          <Button
            label="Nueva cuenta"
            icon={<UserPlus className="h-4 w-4" aria-hidden="true" />}
            onClick={openCreate}
            className="min-h-11 w-full sm:w-auto"
          />
        </header>

        <div className="mt-5">
          <AccountsToolbar filters={filters} onChange={setFilters} />
        </div>

        <p className="sr-only" role="status" aria-live="polite">
          {!loading && filtersActive ? `${filtered.length} de ${users.length} cuentas` : ""}
        </p>
        {!loading && filtersActive && filtered.length > 0 && (
          <p className="m-0 mt-3 text-xs text-fx-text-3" aria-hidden="true">
            Mostrando {filtered.length} de {users.length}
          </p>
        )}

        {actionError && detailId === null && (
          <div className="mt-4">
            <FxBanner tone="error" onClose={() => setActionError(null)}>{actionError}</FxBanner>
          </div>
        )}

        <div className="mt-4">
          <AccountsTable
            users={filtered}
            totalCount={users.length}
            noClients={noClients}
            filtersActive={filtersActive}
            loading={loading}
            error={listErrorText}
            selfDni={user.dni}
            onOpen={openDetail}
            onAction={handleAction}
            onCreate={openCreate}
            onClearFilters={clearFilters}
            onRetry={() => { void reload(); }}
          />
        </div>
      </main>

      <AccountDetailDialog
        user={detailUser}
        selfDni={user.dni}
        onHide={() => { setDetailId(null); setActionError(null); }}
        onAction={handleAction}
        historyKey={historyKey}
        actionError={detailId !== null ? actionError : null}
        onDismissError={() => setActionError(null)}
      />

      <AccountFormDialog
        visible={formOpen}
        user={formUser}
        onHide={() => setFormOpen(false)}
        onCreated={(res) => {
          setFormOpen(false);
          applied(res.user);
          toast.success("Cuenta creada", res.user.name);
          showReveal(res);
        }}
        onUpdated={applied}
        onViewExisting={viewExisting}
        onReload={refreshOne}
      />

      {brandingTarget && (
        <BrandingDialog
          visible={brandingOpen}
          target={brandingTarget}
          onHide={() => setBrandingOpen(false)}
          onSaved={(b) => {
            setHistoryKey(k => k + 1);
            // El superadmin editó su propia marca: el menú se actualiza sin recargar.
            if (brandingTarget.dni === user.dni) setMyBranding(b);
          }}
        />
      )}

      <SuspendAccountDialog
        user={suspendTarget}
        onCancel={() => setSuspendTarget(null)}
        onConfirm={runSuspend}
      />

      <ConfirmDialog
        open={pendingConfirm?.kind === "reactivate"}
        onOpenChange={(o) => { if (!o) setPendingConfirm(null); }}
        onConfirm={() => { if (pendingConfirm) void runReactivate(pendingConfirm.user); }}
        tone="neutral"
        title="Reactivar cuenta"
        description={pendingConfirm ? `¿Reactivar a ${pendingConfirm.user.name}? Va a poder entrar con su contraseña de siempre.` : undefined}
        confirmLabel="Reactivar"
      />

      <ConfirmDialog
        open={pendingConfirm?.kind === "reset"}
        onOpenChange={(o) => { if (!o) setPendingConfirm(null); }}
        onConfirm={() => { if (pendingConfirm) void runReset(pendingConfirm.user); }}
        tone="destructive"
        title={pendingConfirm ? `Resetear la contraseña de ${pendingConfirm.user.name}` : "Resetear contraseña"}
        description="La contraseña actual va a dejar de funcionar y se va a cerrar su sesión. Vas a ver una contraseña temporal nueva."
        confirmLabel="Resetear"
      />

      <TemporaryPasswordDialog reveal={reveal} onClose={() => setReveal(null)} />
    </div>
  );
}
