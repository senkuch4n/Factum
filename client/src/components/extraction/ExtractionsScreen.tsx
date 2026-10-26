"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button } from "primereact/button";
import { ChevronRight, HardDriveDownload, Loader2, Plus, Smartphone } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { agent, type AgentExtractionListItem } from "@/lib/agent";
import { api, type Extraction } from "@/lib/api";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { FxBanner } from "@/components/feedback/FxBanner";
import { StatusChip, ProgressBar } from "@/components/extraction/common";
import { useExtractionLive } from "@/hooks/useExtractionLive";
import { useExtractionRegistration } from "@/hooks/useExtractionRegistration";
import { extractionStatusView, globalProgress, phaseLabel } from "@/lib/extraction";
import { formatBytes, formatDate, formatTime } from "@/lib/format";

/** Capability que habilita la sección (§8.1). */
const CAPABILITY = "extraction_android_v1";

export function ExtractionsScreen() {
  const router = useRouter();
  const { user, handleLogout } = useAuth({ withHistory: false });

  const [capable, setCapable] = useState<boolean | null>(null);
  const [list, setList] = useState<Extraction[] | null>(null);
  const [localList, setLocalList] = useState<AgentExtractionListItem[] | null>(null);
  const [listError, setListError] = useState(false);
  const [listLoading, setListLoading] = useState(true);

  const { snapshot: current } = useExtractionLive(null);
  const { pending } = useExtractionRegistration();

  useEffect(() => {
    let alive = true;
    agent.hasCapability(CAPABILITY).then(v => { if (alive) setCapable(v); }).catch(() => { if (alive) setCapable(false); });
    return () => { alive = false; };
  }, []);

  const loadList = useCallback(async () => {
    setListLoading(true);
    setListError(false);
    try {
      const r = await api.listExtractions();
      setList(r.extractions);
      setLocalList(null);
    } catch {
      // Backend sin extracciones todavía o caído: mostramos el índice local de Tatana (§10.3).
      setListError(true);
      try { setLocalList(await agent.listExtractions()); } catch { setLocalList([]); }
    } finally {
      setListLoading(false);
    }
  }, []);

  useEffect(() => { if (user) void loadList(); }, [user, loadList]);

  const startDisabled = capable === false;
  const showCurrent = current && current.status === "running";

  if (!user) {
    return (
      <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
        <AppNavbar user={null} showThemeSwitch brandHref="/dashboard" maxWidthClass="max-w-5xl"
          sections={{ activeSection: "extracciones" }} />
        <div role="status" aria-label="Cargando" className="mx-auto w-full max-w-5xl flex-1 px-4 pt-10 sm:px-6">
          <Loader2 className="mx-auto h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
          <span className="sr-only">Cargando…</span>
        </div>
      </div>
    );
  }

  return (
    <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
      <AppNavbar user={user} onLogout={handleLogout} showThemeSwitch brandHref="/dashboard" maxWidthClass="max-w-5xl"
        sections={{ activeSection: "extracciones" }} />

      <main className="mx-auto w-full max-w-5xl flex-1 px-4 pb-16 pt-5 sm:px-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
        <header className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div className="min-w-0">
            <h1 className="m-0 text-balance text-fx-h2 text-fx-text">Extracciones</h1>
            <p className="m-0 mt-1 text-fx-body-sm text-fx-text-2">Extracción lógica de dispositivos Android</p>
          </div>
          <Button
            size="large"
            icon={<Plus className="h-5 w-5" aria-hidden="true" />}
            label="Nueva extracción"
            onClick={() => router.push("/extracciones/nueva")}
            disabled={startDisabled}
            className="w-full min-h-11 shrink-0 sm:w-auto"
          />
        </header>

        {startDisabled && (
          <FxBanner tone="warn" className="mt-4">
            Actualizá Tatana para hacer extracciones. Esta versión no admite la extracción lógica de Android.
          </FxBanner>
        )}

        {pending > 0 && (
          <FxBanner tone="info" className="mt-4" role="status">
            {pending === 1
              ? "Hay 1 extracción pendiente de registro en Factum. Se reintenta sola."
              : `Hay ${pending} extracciones pendientes de registro en Factum. Se reintentan solas.`}
          </FxBanner>
        )}

        {showCurrent && current && (
          <section aria-labelledby="current-title" className="mt-6">
            <h2 id="current-title" className="mb-2 text-fx-label uppercase text-fx-text-2">Extracción en curso</h2>
            <Link
              href={`/extracciones/${current.id}`}
              className="fx-card fx-card-interactive block space-y-3 p-4 no-underline sm:p-5"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="flex items-center gap-2 text-fx-body-sm font-semibold text-fx-text">
                  <Smartphone className="h-4 w-4 text-fx-text-2" aria-hidden="true" />
                  {current.manufacturer} {current.model}
                </span>
                <StatusChip view={extractionStatusView(current.status)} />
              </div>
              <CurrentProgress snapshot={current} />
              <span className="inline-flex items-center gap-1 text-fx-body-sm font-semibold text-fx-accent-text">
                Ver progreso
                <ChevronRight className="h-4 w-4" aria-hidden="true" />
              </span>
            </Link>
          </section>
        )}

        <section aria-labelledby="list-title" className="mt-6">
          <h2 id="list-title" className="mb-2 text-fx-label uppercase text-fx-text-2">Historial</h2>

          {listError && (
            <FxBanner tone="warn" className="mb-3" role="status">
              El servidor de Factum no respondió. Se muestran las extracciones de esta PC, con la marca
              &ldquo;Pendiente de registro&rdquo;.
            </FxBanner>
          )}

          {listLoading ? (
            <div role="status" className="flex justify-center py-10">
              <Loader2 className="h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
              <span className="sr-only">Cargando extracciones…</span>
            </div>
          ) : listError ? (
            <LocalList items={localList ?? []} />
          ) : (
            <ApiList items={list ?? []} />
          )}
        </section>
      </main>
    </div>
  );
}

function CurrentProgress({ snapshot }: { snapshot: NonNullable<ReturnType<typeof useExtractionLive>["snapshot"]> }) {
  const { pct, detail } = globalProgress(snapshot);
  return (
    <div className="space-y-1.5">
      <ProgressBar value={pct} label="Progreso de la extracción" valueText={detail} />
      <p className="m-0 flex flex-wrap justify-between gap-2 text-xs text-fx-text-2">
        <span>{phaseLabel(snapshot.phase)}</span>
        <span className="tabular-nums">{detail}</span>
      </p>
    </div>
  );
}

function RowShell({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <li>
      <Link
        href={href}
        className="fx-card fx-card-interactive flex min-h-11 items-center gap-3 p-3.5 no-underline sm:gap-4 sm:p-4"
      >
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-fx-md border border-fx-border bg-fx-surface-2">
          <HardDriveDownload className="h-5 w-5 text-fx-text-2" strokeWidth={1.5} aria-hidden="true" />
        </span>
        {children}
        <ChevronRight className="h-4 w-4 shrink-0 text-fx-text-3" aria-hidden="true" />
      </Link>
    </li>
  );
}

function ApiList({ items }: { items: Extraction[] }) {
  if (items.length === 0) {
    return (
      <EmptyState />
    );
  }
  return (
    <ul className="m-0 list-none space-y-2.5 p-0">
      {items.map(e => (
        <RowShell key={e.id} href={`/extracciones/${e.id}`}>
          <span className="min-w-0 flex-1">
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span className="text-fx-body-sm font-semibold text-fx-text">
                {e.device.manufacturer} {e.device.model}
              </span>
              <StatusChip view={extractionStatusView(e.status)} />
            </span>
            <span className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-fx-text-3">
              <span>{formatDate(e.started_at_pc)} · {formatTime(e.started_at_pc)}</span>
              <span className="tabular-nums">{formatBytes(e.totals.bytes)}</span>
              <span>{e.case_ref ? `Causa ${e.case_ref}` : "Sin caso"}</span>
            </span>
          </span>
        </RowShell>
      ))}
    </ul>
  );
}

function LocalList({ items }: { items: AgentExtractionListItem[] }) {
  if (items.length === 0) return <EmptyState />;
  return (
    <ul className="m-0 list-none space-y-2.5 p-0">
      {items.map(e => (
        <RowShell key={e.id} href={`/extracciones/${e.id}`}>
          <span className="min-w-0 flex-1">
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span className="text-fx-body-sm font-semibold text-fx-text">{e.manufacturer} {e.model}</span>
              <StatusChip view={extractionStatusView(e.status)} />
              {!e.registered && (
                <span className="rounded-fx-pill bg-fx-warning-soft px-2 py-0.5 text-xs font-semibold text-fx-warning">
                  Pendiente de registro
                </span>
              )}
            </span>
            <span className="mt-1 block text-xs text-fx-text-3">{formatDate(e.started_at)} · {formatTime(e.started_at)}</span>
          </span>
        </RowShell>
      ))}
    </ul>
  );
}

function EmptyState() {
  return (
    <div className="fx-card border-dashed border-fx-border-strong px-6 py-14 text-center">
      <span className="mx-auto flex h-16 w-16 items-center justify-center rounded-fx-xl bg-fx-surface-2">
        <HardDriveDownload className="h-8 w-8 text-fx-text-3" strokeWidth={1.5} aria-hidden="true" />
      </span>
      <p className="mt-4 text-fx-h3 text-fx-text">Todavía no hiciste ninguna extracción</p>
      <p className="mx-auto mt-1 max-w-prose text-fx-body-sm text-fx-text-2">
        Una extracción copia contactos, mensajes, llamadas, calendario, apps y archivos del celular a una carpeta, con
        acta y hashes. Empezá con &ldquo;Nueva extracción&rdquo;.
      </p>
    </div>
  );
}
