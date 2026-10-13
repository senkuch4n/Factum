"use client";

import { FxBanner } from "@/components/feedback/FxBanner";

/** Aviso "IMEI no detectado" (extraído tal cual de CaseFormStep, #52). */
export function ImeiMissingNotice({ className }: { className?: string }) {
  return (
    <FxBanner tone="warn" role="status" className={className}>
      <p className="m-0 font-semibold">IMEI no detectado automáticamente</p>
      <p className="m-0 mt-0.5 text-xs font-normal text-fx-text-2">
        Completá el campo IMEI. Marcá <kbd className="font-mono font-bold text-fx-text">*#06#</kbd> en el celular para verlo.
      </p>
    </FxBanner>
  );
}
