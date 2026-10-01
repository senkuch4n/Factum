"use client";

import { motion } from "framer-motion";
import { FolderOpen, Clock, CheckCircle2, Activity } from "lucide-react";
import { EASE } from "@/constants/animations";
import type { Case } from "@/types";

export function DashboardStats({ cases }: { cases: Case[] }) {
  const now = new Date();
  const thisMonth = cases.filter(c => {
    const d = new Date(c.created_at);
    return d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth();
  });
  const completed = cases.filter(c => c.status === "completed");
  const pending   = cases.filter(c => c.status !== "completed");
  const completedPct = cases.length > 0 ? Math.round((completed.length / cases.length) * 100) : 0;

  const stats = [
    { label: "Total",       value: cases.length,      icon: FolderOpen,    accent: "var(--border-md)" },
    { label: "Este mes",    value: thisMonth.length,  icon: Clock,         accent: "var(--border-md)" },
    { label: "Completadas", value: completed.length,  icon: CheckCircle2, accent: "#10b981" },
    { label: "En proceso",  value: pending.length,     icon: Activity,     accent: "#f59e0b" },
  ];

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-3" aria-live="polite">
      {stats.map(({ label, value, icon: Icon, accent }, i) => (
        <motion.div
          key={label}
          className="stat-card relative overflow-hidden"
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: i * 0.05 + 0.1, duration: 0.28, ease: EASE }}
        >
          <span className="absolute top-0 left-0 right-0 h-[2px]" style={{ background: accent, opacity: 0.7 }} aria-hidden="true" />
          <div className="flex items-center justify-between mb-2">
            <p className="section-label">{label}</p>
            <Icon className="w-3.5 h-3.5" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
          </div>
          <div className="flex items-baseline gap-1.5">
            <p className="hero-title text-3xl tabular-nums" style={{ color: "var(--text-primary)" }}>{value}</p>
            {label === "Completadas" && cases.length > 0 && (
              <span className="text-[11px] font-medium tabular-nums" style={{ color: "var(--text-muted)" }}>{completedPct}%</span>
            )}
          </div>
        </motion.div>
      ))}
    </div>
  );
}
