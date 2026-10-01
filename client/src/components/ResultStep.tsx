"use client";

import { motion, useReducedMotion } from "framer-motion";
import { Archive, FileText, Key, Hash, RotateCcw, ShieldCheck, CheckCircle2, Sparkles } from "lucide-react";
import { api } from "@/lib/api";
import { DottedGlowBackground } from "@/components/ui/dotted-glow-background";
import { useTheme } from "@/lib/theme";

interface Props {
  caseNumber: string;
  zipFile: string;
  pdfFile: string;
  password: string;
  hash: string;
  caseId: string;
  backendURL: string;
  onNewCase: () => void;
}

export function ResultStep({ caseNumber, zipFile, pdfFile, password, hash, caseId, backendURL, onNewCase }: Props) {
  const downloadURL = (file: string) => api.downloadURL(caseId, file);
  const prefersReducedMotion = useReducedMotion();
  const { isDark } = useTheme();

  return (
    <div className="relative overflow-hidden">
      {/* Textura ambiental — solo en este cierre celebratorio, nunca detrás de
          contenido denso. Enmascarada para desvanecerse antes de tocar el texto. */}
      <DottedGlowBackground isDark={isDark} className="[mask-image:radial-gradient(ellipse_75%_60%_at_50%_32%,black_0%,transparent_78%)] [-webkit-mask-image:radial-gradient(ellipse_75%_60%_at_50%_32%,black_0%,transparent_78%)]" />

      <div className="relative z-10 space-y-6 text-center">
      {/* Success icon */}
      <div className="relative inline-block mx-auto">
        <motion.div
          className="w-24 h-24 mx-auto rounded-3xl flex items-center justify-center"
          style={{
            background: "rgba(16,185,129,0.14)",
            border: "1px solid rgba(16,185,129,0.4)",
          }}
          initial={{ scale: 0.5, opacity: 0, rotate: -90 }}
          animate={{ scale: 1, opacity: 1, rotate: 0 }}
          transition={{ type: "spring", stiffness: 180, damping: 14, delay: 0.1 }}
        >
          <ShieldCheck className="w-12 h-12 text-emerald-400" strokeWidth={1.4} aria-hidden="true" />
        </motion.div>

        {/* Sparkles */}
        {[{ x: -36, y: -16, s: 0.2 }, { x: 38, y: -22, s: 0.35 }, { x: -28, y: 36, s: 0.28 }, { x: 36, y: 32, s: 0.2 }].map((p, i) => (
          <motion.div
            key={i}
            className="absolute"
            style={{ left: `50%`, top: `50%`, marginLeft: p.x, marginTop: p.y }}
            initial={{ opacity: 0, scale: 0 }}
            animate={{ opacity: [0, 1, 0], scale: [0.5, 1.2, 0] }}
            transition={{ duration: 1.2, delay: 0.4 + i * 0.12 }}
            aria-hidden="true"
          >
            <Sparkles className="w-4 h-4 text-emerald-300" />
          </motion.div>
        ))}

        {/* Pulse ring */}
        <motion.div
          className="absolute inset-0 rounded-3xl border border-emerald-500/40"
          animate={{ scale: [1, 1.6], opacity: [0.6, 0] }}
          transition={{ duration: 2, repeat: prefersReducedMotion ? 0 : Infinity, ease: "easeOut" }}
          aria-hidden="true"
        />
      </div>

      <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.3 }}>
        <h2 className="step-title text-2xl sm:text-3xl" style={{ color: "var(--text-primary)" }}>¡Expediente completado!</h2>
        <p className="text-sm mt-2" style={{ color: "var(--text-secondary)" }}>
          El mismo <span className="font-semibold" style={{ color: "var(--blue-lg)" }}>{caseNumber}</span> está encriptado y con firma digital.
        </p>
      </motion.div>

      {/* Forensic data */}
      <motion.div
        className="rounded-lg text-left space-y-4 p-4"
        style={{ background: "rgba(45,212,191,0.04)", border: "1px solid var(--border-accent)" }}
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.4 }}
      >
        <p className="section-label flex items-center gap-1.5">
          <ShieldCheck className="w-3 h-3" aria-hidden="true" /> Datos que te van a ser util
        </p>

        <div className="flex items-start gap-3">
          <div className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0" style={{ background: "rgba(245,158,11,0.12)", border: "1px solid rgba(245,158,11,0.25)" }}>
            <Key className="w-3.5 h-3.5 text-amber-400" aria-hidden="true" />
          </div>
          <div>
            <p className="section-label">Contraseña del ZIP</p>
            <p className="font-mono text-base font-bold mt-1 select-all" style={{ color: "var(--text-primary)" }}>{password}</p>
            <p className="text-[11px] mt-1" style={{ color: "var(--text-muted)" }}>Incluida en el informe Word · Guardala por seguridad</p>
          </div>
        </div>

        <div className="flex items-start gap-3">
          <div className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0" style={{ background: "rgba(45,212,191,0.12)", border: "1px solid rgba(45,212,191,0.25)" }}>
            <Hash className="w-3.5 h-3.5 text-teal-400" aria-hidden="true" />
          </div>
          <div>
            <p className="section-label">Hash SHA-256 del ZIP</p>
            <p className="font-mono text-[10px] break-all select-all mt-1" style={{ color: "var(--text-secondary)" }}>{hash}</p>
          </div>
        </div>

        {/* Checks */}
        <div className="grid grid-cols-2 gap-2 pt-1">
          {["Expediente encriptado", "Hash verificado", "Word oficial generado"].map(t => (
            <div key={t} className="flex items-center gap-2 text-[11px]" style={{ color: "var(--text-secondary)" }}>
              <CheckCircle2 className="w-3 h-3 text-emerald-400 flex-shrink-0" aria-hidden="true" /> {t}
            </div>
          ))}
        </div>
      </motion.div>

      {/* Downloads */}
      <motion.div
        className="grid grid-cols-2 gap-3"
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.5 }}
      >
        <motion.a
          href={downloadURL(zipFile)}
          className="btn btn-primary flex-col h-20 gap-2 rounded-lg text-sm"
          whileTap={{ scale: 0.97 }}
        >
          <Archive className="w-6 h-6" aria-hidden="true" />
          <div>
            <div>Descargar ZIP</div>
            <div className="text-[10px] opacity-60 font-normal">Expediente cifrado</div>
          </div>
        </motion.a>

        <motion.a
          href={downloadURL(pdfFile)}
          className="btn btn-success flex-col h-20 gap-2 rounded-lg text-sm"
          whileTap={{ scale: 0.97 }}
        >
          <FileText className="w-6 h-6" aria-hidden="true" />
          <div>
            <div>Informe Word</div>
            <div className="text-[10px] opacity-60 font-normal">Puedes descargarlo haciendo click aqui</div>
          </div>
        </motion.a>
      </motion.div>

      <motion.button
        className="btn-ghost w-full text-sm"
        onClick={onNewCase}
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ delay: 0.65 }}
        whileTap={{ scale: 0.98 }}
      >
        <RotateCcw className="w-4 h-4" aria-hidden="true" /> Iniciar nueva inspección
      </motion.button>
      </div>
    </div>
  );
}
