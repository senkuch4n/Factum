"use client";

import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  ChevronDown, FileText, Archive, Key, Hash,
  Smartphone, Calendar, Clock, FolderOpen, Shield, Play,
} from "lucide-react";
import type { Case } from "@/lib/api";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { formatDate, formatTime } from "@/lib/format";
import { StatusBadge } from "./StatusBadge";

export function CaseCard({ cas, index, onResume }: { cas: Case; index: number; onResume: (c: Case) => void }) {
  const [open, setOpen] = useState(false);
  const dlURL = (filename: string) => api.downloadURL(cas.id, filename);
  const isDone = cas.status === "completed";
  const isDraft = cas.status === "draft";

  return (
    <motion.div
      className="card card-hover overflow-hidden"
      layout
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: index * 0.04 }}
    >
      <button
        className="w-full text-left p-4 flex items-center gap-4 group"
        onClick={() => setOpen(o => !o)}
        aria-expanded={open}
        aria-label={`Expediente ${cas.nro_referencia} — ${open ? "ocultar" : "ver"} detalle`}
      >
        <div className={cn(
          "w-10 h-10 rounded-md flex items-center justify-center flex-shrink-0 transition",
          isDone
            ? "bg-emerald-500/10 border border-emerald-500/25"
            : "bg-white/[0.03] border border-[var(--border)]"
        )}>
          <FolderOpen className={cn("w-5 h-5", isDone ? "text-emerald-400" : "text-[var(--text-muted)]")} strokeWidth={1.5} aria-hidden="true" />
        </div>

        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <p className="font-semibold text-sm transition-colors" style={{ color: "var(--text-primary)" }}>
              {cas.nro_referencia}
            </p>
            <StatusBadge status={cas.status} />
          </div>
          <p className="text-xs truncate mt-0.5" style={{ color: "var(--text-secondary)" }}>
            {cas.nombre_denunciante} · DNI {cas.dni_denunciante}
          </p>
          <div className="flex items-center gap-3 mt-1 flex-wrap">
            <span className="text-[11px] flex items-center gap-1" style={{ color: "var(--text-muted)" }}>
              <Smartphone className="w-3 h-3" aria-hidden="true" /> {cas.device.manufacturer} {cas.device.model}
            </span>
            <span className="text-[11px] flex items-center gap-1" style={{ color: "var(--text-muted)" }}>
              <Calendar className="w-3 h-3" aria-hidden="true" /> {formatDate(cas.created_at)}
            </span>
            <span className="text-[11px] flex items-center gap-1" style={{ color: "var(--text-muted)" }}>
              <Clock className="w-3 h-3" aria-hidden="true" /> {formatTime(cas.created_at)}
            </span>
          </div>
        </div>

        <motion.div
          animate={{ rotate: open ? 180 : 0 }}
          transition={{ duration: 0.2 }}
          className="flex-shrink-0"
          style={{ color: "var(--text-muted)" }}
        >
          <ChevronDown className="w-4 h-4" aria-hidden="true" />
        </motion.div>
      </button>

      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.22 }}
            className="overflow-hidden"
          >
            <div className="border-t px-4 pt-4 pb-5 space-y-4" style={{ borderColor: "var(--border)", background: "rgba(99,130,190,0.03)" }}>
              {isDraft && (
                <motion.button
                  onClick={() => onResume(cas)}
                  className="w-full flex items-center justify-center gap-2 py-2.5 rounded-md text-sm font-semibold"
                  style={{ background: "var(--btn-primary-bg)", color: "var(--btn-primary-text)" }}
                  whileTap={{ scale: 0.98 }}
                >
                  <Play className="w-4 h-4" aria-hidden="true" /> Retomar inspección
                </motion.button>
              )}

              <div className="grid grid-cols-2 gap-3 text-xs">
                {[
                  { label: "Fiscal",         value: cas.officer.name },
                  { label: "Unidad",         value: cas.officer.sigla },
                  { label: "DNI Fiscal",     value: cas.officer.dni },
                  { label: "Sistema op.",    value: cas.device.os_version || `Android ${cas.device.android_version}` },
                  { label: "IMEI",           value: cas.device.imei || "—", full: true },
                  { label: "Observaciones",  value: cas.observaciones,       full: true },
                ].map(({ label, value, full }) => (
                  <div key={label} className={full ? "col-span-2" : ""}>
                    <p className="section-label">{label}</p>
                    <p className="mt-1 break-words" style={{ color: "var(--text-primary)" }}>{value}</p>
                  </div>
                ))}
              </div>

              {isDone && (
                <div className="rounded-md border p-3.5 space-y-3.5" style={{ border: "1px solid var(--border-accent)", background: "rgba(45,212,191,0.04)" }}>
                  <p className="section-label flex items-center gap-1.5">
                    <Shield className="w-3 h-3" aria-hidden="true" /> Paquete forense
                  </p>
                  {cas.zip_password && (
                    <div className="flex items-start gap-2.5">
                      <Key className="w-3.5 h-3.5 text-amber-400 flex-shrink-0 mt-0.5" aria-hidden="true" />
                      <div>
                        <p className="section-label">Contraseña ZIP</p>
                        <p className="font-mono text-sm font-bold mt-1 select-all" style={{ color: "var(--text-primary)" }}>{cas.zip_password}</p>
                      </div>
                    </div>
                  )}
                  {cas.zip_hash && (
                    <div className="flex items-start gap-2.5">
                      <Hash className="w-3.5 h-3.5 text-teal-400 flex-shrink-0 mt-0.5" aria-hidden="true" />
                      <div>
                        <p className="section-label">SHA-256 del ZIP</p>
                        <p className="font-mono text-[10px] break-all select-all mt-1" style={{ color: "var(--text-secondary)" }}>{cas.zip_hash}</p>
                      </div>
                    </div>
                  )}
                  <div className="grid grid-cols-2 gap-2 pt-1">
                    {cas.zip_filename && (
                      <motion.a
                        href={dlURL(cas.zip_filename)}
                        className="btn btn-primary flex-col h-16 gap-1 text-xs rounded-md"
                        whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }}
                      >
                        <Archive className="w-5 h-5" aria-hidden="true" /><span>ZIP cifrado</span>
                      </motion.a>
                    )}
                    {cas.pdf_filename && (
                      <motion.a
                        href={dlURL(cas.pdf_filename)}
                        className="btn btn-success flex-col h-16 gap-1 text-xs rounded-md"
                        whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.97 }}
                      >
                        <FileText className="w-5 h-5" aria-hidden="true" /><span>Informe Word</span>
                      </motion.a>
                    )}
                  </div>
                </div>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  );
}
