"use client";

import { motion } from "framer-motion";
import { Smartphone, Calendar, Clock, UserCheck, Archive, FileText, Play } from "lucide-react";
import type { Case } from "@/lib/api";
import { api } from "@/lib/api";
import { formatDate, formatTime } from "@/lib/format";
import { StatusBadge } from "./StatusBadge";

export const STATUS_STYLE: Record<string, { accent: string; bg: string; border: string }> = {
  draft:      { accent: "var(--text-muted)",  bg: "var(--bg-surface)",      border: "var(--border)" },
  generating: { accent: "#2dd4bf",            bg: "rgba(45,212,191,0.05)",  border: "rgba(45,212,191,0.2)" },
  completed:  { accent: "#10b981",            bg: "rgba(16,185,129,0.04)",  border: "rgba(16,185,129,0.18)" },
  error:      { accent: "#ef4444",            bg: "rgba(239,68,68,0.05)",   border: "rgba(239,68,68,0.2)" },
};

export function CaseGridCard({ cas, index, onResume }: { cas: Case; index: number; onResume: (c: Case) => void }) {
  const dlURL = (filename: string) => api.downloadURL(cas.id, filename);
  const isDone = cas.status === "completed";
  const isDraft = cas.status === "draft";
  const st = STATUS_STYLE[cas.status] ?? STATUS_STYLE.draft;

  return (
    <motion.div
      className="relative rounded-lg overflow-hidden flex flex-col"
      style={{ background: st.bg, border: `1px solid ${st.border}` }}
      initial={{ opacity: 0, scale: 0.95 }}
      animate={{ opacity: 1, scale: 1 }}
      transition={{ delay: index * 0.04, type: "spring", stiffness: 260, damping: 22 }}
      whileHover={{ y: -1, boxShadow: "0 4px 14px rgba(0,0,0,0.12)" }}
    >
      <div className="h-1 w-full" style={{ background: st.accent, opacity: 0.6 }} />

      <div className="p-4 flex-1 space-y-3">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="text-sm font-bold truncate" style={{ color: "var(--text-primary)" }}>
              {cas.nro_referencia}
            </p>
            <p className="text-[11px] truncate mt-0.5" style={{ color: "var(--text-secondary)" }}>
              {cas.caratula || cas.nombre_denunciante}
              {cas.dni_denunciante && ` · DNI ${cas.dni_denunciante}`}
            </p>
          </div>
          <StatusBadge status={cas.status} />
        </div>

        <div className="flex items-center gap-1.5 text-[11px]" style={{ color: "var(--text-muted)" }}>
          <Smartphone className="w-3 h-3 flex-shrink-0" aria-hidden="true" />
          <span className="truncate">{cas.device.manufacturer} {cas.device.model}</span>
        </div>

        <div className="grid grid-cols-2 gap-x-2 gap-y-1 text-[10px]" style={{ color: "var(--text-muted)" }}>
          <div className="flex items-center gap-1 truncate">
            <Calendar className="w-2.5 h-2.5 flex-shrink-0" aria-hidden="true" />
            <span>{formatDate(cas.created_at)}</span>
          </div>
          <div className="flex items-center gap-1 truncate">
            <Clock className="w-2.5 h-2.5 flex-shrink-0" aria-hidden="true" />
            <span>{formatTime(cas.created_at)}</span>
          </div>
          <div className="col-span-2 flex items-center gap-1 truncate">
            <UserCheck className="w-2.5 h-2.5 flex-shrink-0" aria-hidden="true" />
            <span className="truncate"><span className="sr-only">Perito: </span>{cas.perito?.nombre ?? cas.officer.name}</span>
          </div>
        </div>
      </div>

      {(isDone && (cas.zip_filename || cas.pdf_filename)) || isDraft ? (
        <div className="px-3 pb-3 flex gap-2 border-t pt-3" style={{ borderColor: st.border }}>
          {isDraft && (
            <motion.button
              onClick={() => onResume(cas)}
              className="flex-1 flex items-center justify-center gap-1.5 py-2 rounded-md text-[11px] font-semibold"
              style={{ background: "var(--btn-primary-bg)", color: "var(--btn-primary-text)" }}
              whileTap={{ scale: 0.97 }}
            >
              <Play className="w-3 h-3" aria-hidden="true" /> Retomar
            </motion.button>
          )}
          {isDone && cas.zip_filename && (
            <motion.a
              href={dlURL(cas.zip_filename)}
              className="flex-1 btn btn-secondary btn-sm flex items-center justify-center gap-1 text-[10px] py-1.5"
              whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.97 }}
              title="Descargar ZIP"
            >
              <Archive className="w-3 h-3" aria-hidden="true" /> ZIP
            </motion.a>
          )}
          {isDone && cas.pdf_filename && (
            <motion.a
              href={dlURL(cas.pdf_filename)}
              className="flex-1 btn btn-success btn-sm flex items-center justify-center gap-1 text-[10px] py-1.5"
              whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.97 }}
              title="Descargar Word"
            >
              <FileText className="w-3 h-3" aria-hidden="true" /> Word
            </motion.a>
          )}
        </div>
      ) : null}
    </motion.div>
  );
}
