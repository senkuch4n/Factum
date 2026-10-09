"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import dynamic from "next/dynamic";
import { BarChart3, TrendingUp } from "lucide-react";
import { Button } from "primereact/button";
import { api, type CaseStats } from "@/lib/api";
import { useAuth } from "@/hooks/useAuth";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { FxBanner } from "@/components/feedback/FxBanner";
import { DashboardStats } from "@/components/dashboard/DashboardStats";
import { DashboardBreakdown } from "@/components/dashboard/DashboardBreakdown";

// Recharts fuera del bundle inicial (igual que en el dashboard). `ssr: false`:
// el gráfico depende del tamaño del contenedor, que no existe en el servidor.
const CasesByMonthChart = dynamic(
  () => import("@/components/dashboard/CasesByMonthChart").then((m) => m.CasesByMonthChart),
  {
    ssr: false,
    loading: () => <div aria-hidden className="h-64 rounded-fx-xl bg-fx-surface-2 motion-safe:animate-pulse" />,
  },
);

/** Skeleton mientras se resuelve la sesión (patrón `ScreenSkeleton` de admin,
    adaptado a la grilla de métricas: título + 4 tarjetas + bloque de gráfico). */
function ScreenSkeleton() {
  return (
    <div role="status" aria-label="Cargando" className="mx-auto w-full max-w-5xl px-4 pb-16 pt-5 sm:px-6">
      <div className="h-8 w-40 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
      <div className="mt-2 h-4 w-56 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
      <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4 sm:gap-4">
        <div className="h-28 rounded-fx-xl bg-fx-surface-2 motion-safe:animate-pulse" />
        <div className="h-28 rounded-fx-xl bg-fx-surface-2 motion-safe:animate-pulse" />
        <div className="h-28 rounded-fx-xl bg-fx-surface-2 motion-safe:animate-pulse" />
        <div className="h-28 rounded-fx-xl bg-fx-surface-2 motion-safe:animate-pulse" />
      </div>
      <div className="mt-6 h-64 w-full rounded-fx-xl bg-fx-surface-2 motion-safe:animate-pulse" />
      <span className="sr-only">Cargando…</span>
    </div>
  );
}

/**
 * Vista "Métricas" (`/metricas`, separar-metricas-vista). Monta la analítica
 * que antes vivía en `/dashboard` (modo historial): KPIs (`DashboardStats`),
 * tendencia (`CasesByMonthChart`) y desglose (`DashboardBreakdown`). Reusa los
 * mismos endpoints (`GET /api/cases/stats` y `/api/cases/breakdown`), filtrados
 * por el token del perito. `withHistory: false`: no necesita el array de casos.
 */
export function MetricsScreen() {
  const router = useRouter();
  const { user, handleLogout } = useAuth({ withHistory: false });

  // Fetch propio del endpoint de stats (igual que antes en el dashboard): el
  // desglose (`DashboardBreakdown`) tiene su fetch autónomo aparte.
  const [stats, setStats] = useState<CaseStats | null>(null);
  const [statsLoading, setStatsLoading] = useState(true);
  const [statsError, setStatsError] = useState(false);

  const loadStats = useCallback(() => {
    setStatsLoading(true);
    setStatsError(false);
    api.caseStats()
      .then(setStats)
      .catch(() => setStatsError(true))
      .finally(() => setStatsLoading(false));
  }, []);

  useEffect(() => { if (user) loadStats(); }, [user, loadStats]);

  if (!user) {
    return (
      <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
        <AppNavbar
          user={null}
          showThemeSwitch
          brandHref="/dashboard"
          maxWidthClass="max-w-5xl"
          sections={{ activeSection: "metricas" }}
        />
        <ScreenSkeleton />
      </div>
    );
  }

  const hasCases = !!stats && stats.total > 0;
  const emptyState = !!stats && stats.total === 0 && !statsLoading && !statsError;

  return (
    <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
      <AppNavbar
        user={user}
        onLogout={handleLogout}
        showThemeSwitch
        brandHref="/dashboard"
        maxWidthClass="max-w-5xl"
        sections={{ activeSection: "metricas" }}
      />

      <main className="mx-auto w-full max-w-5xl flex-1 px-4 pb-16 pt-5 sm:px-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
        <header className="min-w-0">
          <h1 className="m-0 text-balance text-fx-h2 text-fx-text">Métricas</h1>
          <p className="m-0 mt-1 text-fx-body-sm text-fx-text-2">Tus inspecciones en números</p>
        </header>

        <div className="mt-6">
          {statsError ? (
            <FxBanner tone="error">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span>No se pudieron cargar las estadísticas.</span>
                <Button text size="small" label="Reintentar" onClick={loadStats} className="min-h-11" />
              </div>
            </FxBanner>
          ) : emptyState ? (
            <div className="fx-card border-dashed border-fx-border-strong py-14 px-6 text-center space-y-4">
              <span className="mx-auto flex h-20 w-20 items-center justify-center rounded-fx-xl bg-fx-surface-2">
                <BarChart3 className="h-10 w-10 text-fx-text-3" strokeWidth={1.5} aria-hidden="true" />
              </span>
              <div>
                <p className="text-fx-h3 text-fx-text">Todavía no hay nada para medir</p>
                <p className="mt-1 text-fx-body-sm text-fx-text-2">
                  Cuando registres tu primera inspección, acá vas a ver tus números.
                </p>
              </div>
              <Button
                label="Crear la primera inspección"
                onClick={() => router.push("/dashboard")}
                className="min-h-11"
              />
            </div>
          ) : (
            <div className="space-y-6" aria-busy={statsLoading}>
              <DashboardStats stats={stats} loading={statsLoading} />
              {hasCases && (
                <section aria-labelledby="trend-title" className="space-y-3">
                  <h2 id="trend-title" className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
                    <TrendingUp className="h-3.5 w-3.5" aria-hidden="true" />
                    Casos creados por mes
                  </h2>
                  <div className="fx-card rounded-fx-xl p-4 sm:p-5">
                    <CasesByMonthChart data={stats.monthly} />
                  </div>
                </section>
              )}
              {hasCases && <DashboardBreakdown />}
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
