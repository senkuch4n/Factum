"use client";

import { CheckCircle2, AlertCircle, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";

const STATUS_CLASS: Record<string, string> = {
  draft:      "badge-gray",
  generating: "badge-blue",
  completed:  "badge-green",
  error:      "badge-red",
};

const STATUS_LABEL: Record<string, string> = {
  draft: "Borrador", generating: "Generando…", completed: "Completado", error: "Error",
};

export function StatusBadge({ status }: { status: string }) {
  return (
    <span className={cn("badge", STATUS_CLASS[status] ?? "badge-gray")}>
      {status === "completed"  && <CheckCircle2 className="w-3 h-3" aria-hidden="true" />}
      {status === "error"      && <AlertCircle  className="w-3 h-3" aria-hidden="true" />}
      {status === "generating" && <Loader2 className="w-3 h-3 animate-spin" aria-hidden="true" />}
      {status === "draft"      && <div className="w-1.5 h-1.5 rounded-full bg-current" aria-hidden="true" />}
      {STATUS_LABEL[status] ?? status}
    </span>
  );
}
