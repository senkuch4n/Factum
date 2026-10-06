"use client";

import { useState } from "react";
import { AlertCircle, Download, FolderSearch, Loader2 } from "lucide-react";
import { agent } from "@/lib/agent";
import { agentErrorMessage } from "@/lib/agent-messages";
import { cn } from "@/lib/utils";
import { FX_BUTTON_PRIMARY, FX_BUTTON_SECONDARY } from "@/lib/prime/pt/shared";

/**
 * "Mostrar en carpeta" y "Guardar una copia…" del ZIP que quedó en esta PC
 * (zip-local-informe-servidor D6 A, SDD §7.7). Solo se monta si Tatana es la
 * PC del caso. La lógica es la misma en el resultado y en el historial.
 *
 * "Guardar una copia…" consulta primero el estado del ZIP: si ya no está, avisa
 * en lugar de dejar que el navegador baje un 404.
 */
export function ZipLocalActions({
  caseId, caseRef, zipFilename, size = "regular", label,
}: {
  caseId: string;
  caseRef: string;
  zipFilename: string;
  /** `compact` en la tarjeta de cuadrícula. */
  size?: "regular" | "compact";
  /** Para el `aria-label` en listas (p. ej. "de la causa 1234"). */
  label?: string;
}) {
  const [busy, setBusy] = useState<"reveal" | "copy" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const errorId = `zip-actions-error-${caseId}`;

  async function reveal() {
    if (busy) return;
    setBusy("reveal"); setError(null);
    try {
      await agent.revealZip(caseId, caseRef, zipFilename);
    } catch (e) {
      setError(agentErrorMessage(e, { action: "reveal" }));
    } finally {
      setBusy(null);
    }
  }

  async function saveCopy() {
    if (busy) return;
    setBusy("copy"); setError(null);
    try {
      const st = await agent.zipStatus(caseId, caseRef, zipFilename);
      if (st.state !== "final") {
        setError("El ZIP no está en esta PC.");
        return;
      }
      // Tatana lo sirve como `attachment`: el navegador lo guarda donde elija el perito.
      const a = document.createElement("a");
      a.href = agent.zipFileURL(caseId, caseRef, zipFilename);
      a.download = zipFilename;
      a.rel = "noopener";
      document.body.appendChild(a);
      a.click();
      a.remove();
    } catch (e) {
      setError(agentErrorMessage(e, { action: "copy" }));
    } finally {
      setBusy(null);
    }
  }

  const compact = size === "compact";
  const btn = compact ? "flex-1 min-h-11 px-3 py-1.5 text-xs" : "min-h-11";
  const icon = compact ? "h-3.5 w-3.5" : "h-4 w-4";
  const suffix = label ? ` ${label}` : "";

  return (
    <div className="space-y-2">
      <div className={cn("flex gap-2", compact ? "" : "flex-col sm:flex-row")}>
        <button
          type="button"
          onClick={reveal}
          aria-busy={busy === "reveal" || undefined}
          aria-label={compact ? `Mostrar en carpeta el ZIP${suffix}` : undefined}
          aria-describedby={error ? errorId : undefined}
          className={cn(FX_BUTTON_PRIMARY, btn, busy === "reveal" && "cursor-wait opacity-70")}
        >
          {busy === "reveal"
            ? <Loader2 className={cn(icon, "motion-safe:animate-spin")} aria-hidden="true" />
            : <FolderSearch className={icon} aria-hidden="true" />}
          {compact ? "Carpeta" : "Mostrar en carpeta"}
        </button>
        <button
          type="button"
          onClick={saveCopy}
          aria-busy={busy === "copy" || undefined}
          aria-label={compact ? `Guardar una copia del ZIP${suffix}` : undefined}
          aria-describedby={error ? errorId : undefined}
          className={cn(FX_BUTTON_SECONDARY, btn, busy === "copy" && "cursor-wait opacity-70")}
        >
          {busy === "copy"
            ? <Loader2 className={cn(icon, "motion-safe:animate-spin")} aria-hidden="true" />
            : <Download className={icon} aria-hidden="true" />}
          {compact ? "Copia…" : "Guardar una copia…"}
        </button>
      </div>
      {error && (
        <p id={errorId} role="alert" className="m-0 flex items-start gap-1.5 text-left text-xs text-fx-danger">
          <AlertCircle className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          {error}
        </p>
      )}
    </div>
  );
}
