"use client";

import { Tag } from "primereact/tag";
import { AlertCircle, CheckCircle2, CircleDashed, Loader2 } from "lucide-react";
import { caseStatusOf, type CaseStatusKey } from "@/lib/case-status";

const ICON: Record<CaseStatusKey, React.ReactNode> = {
  draft: <CircleDashed className="h-3 w-3" aria-hidden="true" />,
  generating: <Loader2 className="h-3 w-3 motion-safe:animate-spin" aria-hidden="true" />,
  completed: <CheckCircle2 className="h-3 w-3" aria-hidden="true" />,
  error: <AlertCircle className="h-3 w-3" aria-hidden="true" />,
};

/** Estado del caso: `Tag` de Prime, siempre ícono + texto. */
export function StatusBadge({ status }: { status: string }) {
  const st = caseStatusOf(status);
  return <Tag severity={st.severity} value={st.label} icon={ICON[st.key]} />;
}
