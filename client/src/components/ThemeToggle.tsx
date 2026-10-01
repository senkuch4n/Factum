"use client";

import { motion, AnimatePresence } from "framer-motion";
import { Sun, Moon } from "lucide-react";
import { useTheme } from "@/lib/theme";

export function ThemeToggle() {
  const { isDark, toggle } = useTheme();

  return (
    <motion.button
      onClick={toggle}
      className="btn-icon rounded-lg relative overflow-hidden"
      style={{
        background: "var(--btn-secondary-bg)",
        border: "1px solid var(--border-md)",
        color: "var(--text-secondary)",
      }}
      whileHover={{ scale: 1.05 }}
      whileTap={{ scale: 0.93 }}
      title={isDark ? "Cambiar a modo claro" : "Cambiar a modo oscuro"}
      aria-label={isDark ? "Cambiar a modo claro" : "Cambiar a modo oscuro"}
      aria-pressed={isDark}
    >
      <AnimatePresence mode="wait" initial={false}>
        {isDark ? (
          <motion.div
            key="sun"
            initial={{ rotate: -90, opacity: 0, scale: 0.5 }}
            animate={{ rotate: 0,  opacity: 1, scale: 1 }}
            exit={{    rotate:  90, opacity: 0, scale: 0.5 }}
            transition={{ duration: 0.22 }}
          >
            <Sun className="w-4 h-4 text-amber-400" />
          </motion.div>
        ) : (
          <motion.div
            key="moon"
            initial={{ rotate: 90, opacity: 0, scale: 0.5 }}
            animate={{ rotate: 0,  opacity: 1, scale: 1 }}
            exit={{    rotate: -90, opacity: 0, scale: 0.5 }}
            transition={{ duration: 0.22 }}
          >
            <Moon className="w-4 h-4" style={{ color: "var(--text-secondary)" }} />
          </motion.div>
        )}
      </AnimatePresence>
    </motion.button>
  );
}
