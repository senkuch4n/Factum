"use client";

import { useState } from "react";
import { Button } from "primereact/button";
import { ShieldCheck } from "lucide-react";
import { agent } from "@/lib/agent";
import { iosDeveloperModeMessage } from "@/lib/agent-messages";
import { useAgentIosStatus } from "@/hooks/useAgentIosStatus";
import { FxBanner } from "@/components/feedback/FxBanner";

const NO_IPHONE_MESSAGE = 'Conectá el iPhone por USB, desbloqueado, y tocá "Confiar". Después volvé a tocar el botón.';
const OUTDATED_MESSAGE = "Actualizá Tatana para activar el Modo Desarrollador desde Factum.";

type Feedback = { tone: "success" | "info" | "error"; text: string };

/**
 * Paso "devmode" de la guía de iPhone (ios-herramientas-windows §9.5, D5): Tatana activa
 * el Modo Desarrollador del primer iPhone conectado, sin que el perito escriba comandos. Con un Tatana
 * viejo o caído (sin `ios_developer_mode_v1`) se muestra solo el texto de actualizar.
 */
export function DeveloperModeAction() {
  const { supportsDeveloperMode } = useAgentIosStatus();
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  if (!supportsDeveloperMode) {
    return <p className="m-0 mt-2 text-xs text-fx-text-2">{OUTDATED_MESSAGE}</p>;
  }

  async function activate() {
    setBusy(true);
    setFeedback(null);
    try {
      const iphone = (await agent.listDevices()).find(d => d.platform === "ios");
      if (!iphone) {
        setFeedback({ tone: "info", text: NO_IPHONE_MESSAGE });
        return;
      }
      const { status } = await agent.enableIosDeveloperMode(iphone.serial);
      setFeedback({
        tone: status === "manual_required" ? "info" : "success",
        text: iosDeveloperModeMessage(status, iphone.name || "el iPhone"),
      });
    } catch (e) {
      setFeedback({ tone: "error", text: e instanceof Error ? e.message : "No se pudo activar el Modo Desarrollador del iPhone" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-2 space-y-2">
      <Button
        type="button"
        size="small"
        icon={<ShieldCheck className="h-4 w-4" aria-hidden="true" />}
        label={busy ? "Activando…" : "Activar Modo Desarrollador"}
        loading={busy}
        disabled={busy}
        aria-busy={busy || undefined}
        onClick={() => { void activate(); }}
        className="min-h-11 sm:min-h-0"
      />
      <p aria-live="polite" className="m-0 min-h-4 text-xs text-fx-text-3">
        {busy ? "Puede tardar hasta un minuto; no desconectes el iPhone." : ""}
      </p>
      {feedback && (
        <FxBanner tone={feedback.tone} role={feedback.tone === "error" ? "alert" : "status"}>
          <p className="m-0 font-normal text-fx-text">{feedback.text}</p>
        </FxBanner>
      )}
    </div>
  );
}
