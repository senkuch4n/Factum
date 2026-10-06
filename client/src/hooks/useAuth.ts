"use client";

import { useState, useEffect, useCallback } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import type { User, Case } from "@/types";

export function useAuth() {
  const router = useRouter();
  const [user, setUser] = useState<User | null>(null);
  const [historyCases, setHistory] = useState<Case[]>([]);
  const [historyLoading, setHisLoad] = useState(false);

  const loadHistory = useCallback(() => {
    setHisLoad(true);
    api.listCases()
      .then(r => setHistory(r.cases))
      .catch(() => {})
      .finally(() => setHisLoad(false));
  }, []);

  useEffect(() => {
    api.me()
      .then(u => {
        // Cambio obligatorio pendiente: sin historial (el backend respondería 403).
        if (u.must_change_password) { router.replace("/cambiar-contrasena"); return; }
        setUser(u);
        loadHistory();
      })
      // Si fue un 401, el interceptor de lib/api.ts ya dejó el aviso para el login.
      .catch(() => router.push("/"));
  }, [router, loadHistory]);

  function handleLogout() {
    api.logout();
    router.push("/");
  }

  return { user, historyCases, historyLoading, loadHistory, handleLogout };
}
