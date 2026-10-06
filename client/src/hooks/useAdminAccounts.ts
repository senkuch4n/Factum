"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { sortAccounts } from "@/lib/admin-accounts";
import type { AdminUser } from "@/types";

export interface AdminAccountsState {
  users: AdminUser[];
  /** `true` mientras carga el listado completo (no en `refreshOne`). */
  loading: boolean;
  /** Error de la última carga del listado; `null` si salió bien. */
  error: unknown;
  /** Vuelve a pedir el listado completo (E1). */
  reload: () => Promise<void>;
  /** Reemplaza o agrega la cuenta por `id` y reordena por nombre. */
  upsert: (user: AdminUser) => void;
  /** Vuelve a pedir una cuenta (E2) y la reemplaza en la lista. Devuelve la cuenta o `null` si falló. */
  refreshOne: (id: string) => Promise<AdminUser | null>;
}

/**
 * Listado del panel de cuentas (abm-clientes §9.4). Con `enabled: false` no
 * llama a la API: la pantalla lo prende recién cuando confirmó que el usuario
 * es superadmin en modo `local`. Después de cada acción solo cambia esa fila.
 */
export function useAdminAccounts(enabled: boolean): AdminAccountsState {
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  // Descarta respuestas de cargas viejas si se pidió otra en el medio.
  const requestSeq = useRef(0);

  const reload = useCallback(async () => {
    const seq = ++requestSeq.current;
    setLoading(true);
    setError(null);
    try {
      const list = await api.adminListUsers();
      if (seq === requestSeq.current) setUsers(sortAccounts(list));
    } catch (err) {
      if (seq === requestSeq.current) setError(err);
    } finally {
      if (seq === requestSeq.current) setLoading(false);
    }
  }, []);

  const upsert = useCallback((user: AdminUser) => {
    setUsers(prev => {
      const i = prev.findIndex(u => u.id === user.id);
      const next = i === -1 ? [...prev, user] : prev.map(u => (u.id === user.id ? user : u));
      return sortAccounts(next);
    });
  }, []);

  const refreshOne = useCallback(async (id: string) => {
    try {
      const user = await api.adminGetUser(id);
      upsert(user);
      return user;
    } catch {
      return null;
    }
  }, [upsert]);

  useEffect(() => {
    if (enabled) void reload();
  }, [enabled, reload]);

  return { users, loading, error, reload, upsert, refreshOne };
}
