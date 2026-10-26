"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Loader2 } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { agent } from "@/lib/agent";
import { api, type Extraction } from "@/lib/api";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { FxBanner } from "@/components/feedback/FxBanner";
import { useExtractionLive } from "@/hooks/useExtractionLive";
import { useExtractionRegistration } from "@/hooks/useExtractionRegistration";
import { ExtractionProgress } from "@/components/extraction/ExtractionProgress";
import { ExtractionResult } from "@/components/extraction/ExtractionResult";
import { LinkCaseDialog } from "@/components/extraction/LinkCaseDialog";

export function ExtractionDetailScreen({ id }: { id: string }) {
  const { user, handleLogout } = useAuth({ withHistory: false });
  const { snapshot, loading: snapLoading, refresh: refreshSnap } = useExtractionLive(id);
  const { running: regRunning } = useExtractionRegistration();

  const [dto, setDto] = useState<Extraction | null>(null);
  const [dtoLoading, setDtoLoading] = useState(true);
  const [dtoMissing, setDtoMissing] = useState(false);
  const [canReveal, setCanReveal] = useState(true);
  const [revealing, setRevealing] = useState(false);
  const [linkOpen, setLinkOpen] = useState(false);
  const [unlinking, setUnlinking] = useState(false);

  const loadDto = useCallback(async () => {
    try {
      setDto(await api.getExtraction(id));
      setDtoMissing(false);
    } catch {
      setDtoMissing(true);
    } finally {
      setDtoLoading(false);
    }
  }, [id]);

  useEffect(() => { void loadDto(); }, [loadDto]);

  // Tras reconciliar el registro diferido, refrescamos el DTO (ya debería existir).
  useEffect(() => { if (!regRunning) void loadDto(); }, [regRunning, loadDto]);

  // `can_reveal` (Docker lo pone en false): best-effort.
  useEffect(() => {
    let alive = true;
    agent.getDestinations().then(d => { if (alive) setCanReveal(d.can_reveal); }).catch(() => {});
    return () => { alive = false; };
  }, []);

  async function reveal() {
    setRevealing(true);
    try { await agent.revealExtraction(id); }
    catch { setCanReveal(false); }
    finally { setRevealing(false); }
  }

  async function unlink() {
    setUnlinking(true);
    try { setDto(await api.unlinkExtraction(id)); }
    catch { /* el DTO queda como estaba */ }
    finally { setUnlinking(false); }
  }

  const running = snapshot?.status === "running";
  const bothLoading = snapLoading && dtoLoading;
  const nothing = !snapshot && dtoMissing && !dtoLoading && !snapLoading;

  return (
    <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
      <AppNavbar user={user} onLogout={user ? handleLogout : undefined} showThemeSwitch brandHref="/dashboard"
        maxWidthClass="max-w-3xl" sections={{ activeSection: "extracciones" }} />

      <main className="mx-auto w-full max-w-3xl flex-1 px-4 pb-16 pt-5 sm:px-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
        <Link
          href="/extracciones"
          className="mb-4 inline-flex items-center gap-1.5 text-fx-body-sm font-medium text-fx-text-2 no-underline hover:text-fx-text fx-focus-ring"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Extracciones
        </Link>

        <h1 className="m-0 mb-5 text-fx-h2 text-fx-text">{running ? "Extracción en curso" : "Extracción"}</h1>

        {dtoMissing && !running && !snapshot && (
          <FxBanner tone="warn" className="mb-4" role="status">
            El servidor de Factum no tiene esta extracción todavía. Si recién terminó, se registra sola al volver a la
            sección.
          </FxBanner>
        )}

        {bothLoading ? (
          <div role="status" className="flex justify-center py-16">
            <Loader2 className="h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
            <span className="sr-only">Cargando la extracción…</span>
          </div>
        ) : nothing ? (
          <FxBanner tone="error">
            No se encontró la extracción. Puede estar en otra PC o haber sido iniciada por otra persona.
          </FxBanner>
        ) : running && snapshot ? (
          <ExtractionProgress snapshot={snapshot} onChanged={() => { void refreshSnap(); void loadDto(); }} />
        ) : (
          <ExtractionResult
            id={id}
            snapshot={snapshot}
            dto={dto}
            canReveal={canReveal}
            onReveal={reveal}
            revealing={revealing}
            onOpenLink={() => setLinkOpen(true)}
            onUnlink={unlink}
            unlinking={unlinking}
          />
        )}
      </main>

      {dto && (
        <LinkCaseDialog
          open={linkOpen}
          extractionId={id}
          currentCaseId={dto.case_id}
          onClose={() => setLinkOpen(false)}
          onLinked={updated => setDto(updated)}
        />
      )}
    </div>
  );
}
