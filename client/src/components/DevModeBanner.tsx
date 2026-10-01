"use client";

import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { FlaskConical } from "lucide-react";
import { api } from "@/lib/api";

export function DevModeBanner() {
  const [mode, setMode] = useState<"dev" | "mpf" | null>(null);

  useEffect(() => {
    api.getMode().then(setMode).catch(() => {});
  }, []);

  return (
    <AnimatePresence>
      {mode === "dev" && (
        <motion.div
          className="w-full flex items-center justify-center gap-2 py-1.5 text-xs font-semibold tracking-wide z-50 relative"
          style={{
            background: "linear-gradient(90deg, rgba(245,158,11,0.15), rgba(234,88,12,0.12), rgba(245,158,11,0.15))",
            borderBottom: "1px solid rgba(245,158,11,0.25)",
            color: "#fbbf24",
          }}
          initial={{ height: 0, opacity: 0 }}
          animate={{ height: "auto", opacity: 1 }}
          exit={{ height: 0, opacity: 0 }}
        >
          <FlaskConical className="w-3 h-3" />
          MODO DESARROLLO — Autenticación simulada · No apto para uso oficial
        </motion.div>
      )}
    </AnimatePresence>
  );
}
