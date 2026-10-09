"use client";

/**
 * Acceso al historial de versiones del informe desde el detalle del caso
 * (versionado-informe, HU6 D7-B). El perito elige una de las ocho secciones y
 * abre el panel `ReportVersionHistory` en solo lectura (Ver / Comparar): la
 * restauración vive en el editor del paso 4, no en el detalle. En casos
 * `completed` el historial es de por sí solo lectura (D6).
 *
 * El panel se carga con `next/dynamic`: no entra al bundle del detalle hasta
 * que se abre.
 */

import { useState } from "react";
import dynamic from "next/dynamic";
import { Button } from "primereact/button";
import { History } from "lucide-react";
import type { Case } from "@/lib/api";
import { REPORT_SECTION_KEYS, REPORT_SECTION_LABELS } from "@/lib/pericial";

const ReportVersionHistory = dynamic(() => import("@/components/report-versions/ReportVersionHistory"), {
  ssr: false,
});

type SectionKey = (typeof REPORT_SECTION_KEYS)[number];

export function CaseVersionHistoryButton({ cas }: { cas: Case }) {
  const [section, setSection] = useState<SectionKey | null>(null);
  // El caso trae los textos en `getCase`; sin textos (caso muy viejo) no hay historial que mirar.
  const texts = cas.report_texts;

  if (!texts) return null;

  return (
    <section aria-labelledby={`case-${cas.id}-report-history`} className="space-y-2.5">
      <h3 id={`case-${cas.id}-report-history`} className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
        <History className="w-3.5 h-3.5" aria-hidden="true" /> Historial del informe
      </h3>
      <p className="m-0 text-xs text-fx-text-3">
        Mirá las versiones guardadas de cada sección del informe y compará los cambios.
      </p>
      <div className="flex flex-wrap gap-1.5">
        {REPORT_SECTION_KEYS.map(key => (
          <Button
            key={key}
            type="button"
            severity="secondary"
            size="small"
            label={REPORT_SECTION_LABELS[key]}
            onClick={() => setSection(key)}
            className="min-h-8"
          />
        ))}
      </div>

      {section && (
        <ReportVersionHistory
          open={section !== null}
          onClose={() => setSection(null)}
          caseId={cas.id}
          sectionKey={section}
          sectionLabel={REPORT_SECTION_LABELS[section]}
          currentText={texts[section] ?? ""}
          readOnly
        />
      )}
    </section>
  );
}
