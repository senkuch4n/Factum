"use client";

import { motion, AnimatePresence } from "framer-motion";
import { AlertCircle } from "lucide-react";

/** `aria-describedby` para un control dentro de `FormField` (ayuda + error). */
export function describedBy(id: string, opts: { hint?: string; error?: string }): string | undefined {
  const ids = [opts.hint ? `${id}-hint` : null, opts.error ? `${id}-error` : null].filter(Boolean);
  return ids.length ? ids.join(" ") : undefined;
}

export function FormField({
  id, label, sublabel, icon: Icon, error, required, hint, children,
}: {
  id?: string; label: string; sublabel?: string; icon: React.ElementType;
  error?: string; required?: boolean;
  /** Ayuda persistente debajo del control (id `${id}-hint`, para aria-describedby). */
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="flex items-center gap-1.5 mb-1.5">
        <Icon className="w-3 h-3" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
        <span className="text-[0.62rem] font-semibold uppercase tracking-wider" style={{ color: "var(--text-secondary)" }}>
          {label}
        </span>
        {required && <span className="text-red-500 text-[9px] leading-none ml-0.5" aria-hidden="true">*</span>}
        {sublabel && (
          <span className="text-[10px]" style={{ color: "var(--text-muted)" }}>{sublabel}</span>
        )}
      </label>
      {children}
      {hint && (
        <p id={id ? `${id}-hint` : undefined} className="mt-1 text-[11px] leading-snug" style={{ color: "var(--text-muted)" }}>
          {hint}
        </p>
      )}
      <AnimatePresence>
        {error && (
          <motion.p
            id={id ? `${id}-error` : undefined}
            role="alert"
            className="text-xs text-red-500 mt-1.5 flex items-center gap-1"
            initial={{ opacity: 0, height: 0, y: -4 }}
            animate={{ opacity: 1, height: "auto", y: 0 }}
            exit={{ opacity: 0, height: 0 }}
          >
            <AlertCircle className="w-3 h-3 flex-shrink-0" aria-hidden="true" /> {error}
          </motion.p>
        )}
      </AnimatePresence>
    </div>
  );
}
