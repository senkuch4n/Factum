"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Button } from "primereact/button";
import {
  Ban, ChevronDown, History, KeyRound, Loader2, LockOpen, Pencil, RotateCcw, UserPlus,
} from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";
import { api } from "@/lib/api";
import {
  ACTION_LABELS, FIELD_LABELS, adminErrorMessage, formatChangeValue, formatDateTime,
} from "@/lib/admin-accounts";
import { cn } from "@/lib/utils";
import type { AdminAction, AdminUserEvent } from "@/types";

const PAGE_SIZE = 20;

const ACTION_ICONS: Record<AdminAction, React.ElementType> = {
  create: UserPlus,
  update: Pencil,
  suspend: Ban,
  reactivate: RotateCcw,
  reset_password: KeyRound,
  unlock: LockOpen,
};

/** Concatena sin repetir ids (pudo entrar un evento nuevo entre páginas). */
function mergeEvents(prev: AdminUserEvent[], next: AdminUserEvent[]): AdminUserEvent[] {
  const seen = new Set(prev.map(e => e.id));
  return [...prev, ...next.filter(e => !seen.has(e.id))];
}

function EventItem({ evt }: { evt: AdminUserEvent }) {
  const [open, setOpen] = useState(false);
  const changesId = `${useId()}-changes`;
  const Icon = ACTION_ICONS[evt.action] ?? History;
  const expandable = (evt.action === "update" || evt.action === "create") && evt.changes.length > 0;

  return (
    <li className="relative flex gap-3 pb-4 last:pb-0">
      <span
        aria-hidden="true"
        className="relative z-[1] mt-0.5 inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full border border-fx-border bg-fx-surface-2 text-fx-text-2"
      >
        <Icon className="h-3.5 w-3.5" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="m-0 text-fx-body-sm font-semibold text-fx-text">{ACTION_LABELS[evt.action] ?? evt.action}</p>
        <p className="m-0 mt-0.5 text-xs text-fx-text-3">
          <time dateTime={evt.at}>{formatDateTime(evt.at)}</time>
          {" · "}
          {evt.actor_name} (DNI <span translate="no">{evt.actor_dni}</span>)
        </p>
        {evt.action === "suspend" && evt.reason && (
          <p className="m-0 mt-1.5 whitespace-pre-wrap text-fx-body-sm text-fx-text-2">
            <span className="text-fx-text-3">Motivo:</span> {evt.reason}
          </p>
        )}
        {expandable && (
          <>
            <button
              type="button"
              onClick={() => setOpen(o => !o)}
              aria-expanded={open}
              aria-controls={changesId}
              className="-ml-1 mt-1 inline-flex min-h-8 items-center gap-1 rounded-fx-sm px-1 text-xs font-semibold text-fx-accent-text transition-colors duration-fx-fast ease-fx hover:text-fx-text fx-focus-ring max-md:min-h-11"
            >
              <ChevronDown
                className={cn("h-3.5 w-3.5 transition-transform duration-fx-fast ease-fx motion-reduce:transition-none", open && "rotate-180")}
                aria-hidden="true"
              />
              {open ? "Ocultar cambios" : `Ver cambios (${evt.changes.length})`}
            </button>
            <dl id={changesId} hidden={!open} className="m-0 mt-1.5 flex flex-col gap-1.5 rounded-fx-md bg-fx-surface-1 px-3 py-2 text-xs">
              {evt.changes.map((c, i) => (
                <div key={`${c.field}-${i}`} className="min-w-0">
                  <dt className="inline font-semibold text-fx-text-2">{FIELD_LABELS[c.field] ?? c.field}: </dt>
                  <dd className="m-0 inline break-words text-fx-text-2">
                    {evt.action === "create" ? (
                      <span className="whitespace-pre-wrap">{formatChangeValue(c.field, c.to)}</span>
                    ) : (
                      <>
                        <span className="whitespace-pre-wrap text-fx-text-3">{formatChangeValue(c.field, c.from)}</span>
                        <span aria-hidden="true"> → </span>
                        <span className="sr-only"> cambió a </span>
                        <span className="whitespace-pre-wrap text-fx-text">{formatChangeValue(c.field, c.to)}</span>
                      </>
                    )}
                  </dd>
                </div>
              ))}
            </dl>
          </>
        )}
      </div>
    </li>
  );
}

interface Props {
  userId: string;
  /** Al cambiar, se recarga la primera página (después de una acción). */
  refreshKey: number;
}

/**
 * Historial de acciones administrativas de una cuenta (E9), de la más
 * reciente a la más vieja, en páginas de 20 con "Ver más". Solo lectura.
 */
export function AccountHistory({ userId, refreshKey }: Props) {
  const [events, setEvents] = useState<AdminUserEvent[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const seq = useRef(0);

  const loadFirst = useCallback(async () => {
    const mine = ++seq.current;
    setLoading(true);
    setError(null);
    try {
      const page = await api.adminListUserEvents(userId, 0, PAGE_SIZE);
      if (mine !== seq.current) return;
      setEvents(mergeEvents([], page.events));
      setHasMore(page.has_more);
    } catch (err) {
      if (mine === seq.current) setError(adminErrorMessage(err));
    } finally {
      if (mine === seq.current) setLoading(false);
    }
  }, [userId]);

  useEffect(() => { void loadFirst(); }, [loadFirst, refreshKey]);

  async function loadMore() {
    const mine = seq.current;
    setLoadingMore(true);
    setError(null);
    try {
      const page = await api.adminListUserEvents(userId, events.length, PAGE_SIZE);
      if (mine !== seq.current) return;
      setEvents(prev => mergeEvents(prev, page.events));
      setHasMore(page.has_more);
    } catch (err) {
      if (mine === seq.current) setError(adminErrorMessage(err));
    } finally {
      setLoadingMore(false);
    }
  }

  return (
    <section aria-labelledby={`history-${userId}`} className="flex flex-col gap-3">
      <h3 id={`history-${userId}`} className="m-0 flex items-center gap-2 text-fx-label uppercase text-fx-text-3">
        <History className="h-3.5 w-3.5" aria-hidden="true" /> Historial
      </h3>
      {loading ? (
        <p className="m-0 flex items-center gap-2 text-fx-body-sm text-fx-text-2" role="status">
          <Loader2 className="h-4 w-4 motion-safe:animate-spin" aria-hidden="true" /> Cargando historial…
        </p>
      ) : error && events.length === 0 ? (
        <div className="flex flex-col items-start gap-2">
          <FxBanner tone="error">{error}</FxBanner>
          <Button size="small" severity="secondary" label="Reintentar" onClick={() => { void loadFirst(); }} />
        </div>
      ) : events.length === 0 ? (
        <p className="m-0 text-fx-body-sm text-fx-text-3">Sin acciones registradas.</p>
      ) : (
        <>
          <ol className="relative m-0 list-none p-0 before:absolute before:bottom-2 before:left-[13px] before:top-2 before:w-px before:bg-fx-border">
            {events.map(e => <EventItem key={e.id} evt={e} />)}
          </ol>
          {error && (
            <div className="flex flex-col items-start gap-2">
              <FxBanner tone="error">{error}</FxBanner>
            </div>
          )}
          {hasMore && (
            <Button
              size="small"
              severity="secondary"
              label={loadingMore ? "Cargando…" : "Ver más"}
              loading={loadingMore}
              onClick={() => { void loadMore(); }}
              className="self-start"
            />
          )}
        </>
      )}
    </section>
  );
}
