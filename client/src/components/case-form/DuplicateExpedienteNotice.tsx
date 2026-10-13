"use client";

import { Button } from "primereact/button";
import { FxBanner } from "@/components/feedback/FxBanner";
import { formatDate } from "@/lib/format";
import type { Case } from "@/types";

export const DUP_NOTICE_ID = "case-nro_referencia-dup";

function equipo(c: Case): string {
  return [c.device?.manufacturer, c.device?.model].filter(Boolean).join(" ").trim();
}

/**
 * Aviso no bloqueante de expediente repetido (§6.12). Aparece al salir del
 * campo, nunca mientras se tipea. La acción precarga desde esa causa.
 */
export function DuplicateExpedienteNotice({ cases, onUse, onDismiss }: {
  /** Casos con el mismo expediente, el más reciente primero (≥ 1). */
  cases: Case[];
  /** Solo en un caso nuevo y si el último es pericial. */
  onUse?: (cas: Case) => void;
  onDismiss: () => void;
}) {
  const last = cases[0];
  const detail = [formatDate(last.created_at), equipo(last)].filter(Boolean).join(", ");
  return (
    <FxBanner tone="info" role="status" onClose={onDismiss} className="mt-2">
      <p id={DUP_NOTICE_ID} className="m-0">
        {cases.length === 1
          ? <>Ya tenés un caso con este expediente ({detail}).</>
          : <>Ya tenés {cases.length} casos con este expediente (el último: {detail}).</>}
      </p>
      {onUse && (
        <Button
          type="button" text size="small"
          label="Usar los datos de esa causa"
          onClick={() => onUse(last)}
          className="-ml-2 mt-1 [@media(pointer:coarse)]:min-h-11"
        />
      )}
    </FxBanner>
  );
}
