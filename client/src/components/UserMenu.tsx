"use client";

import { useState, useRef, useEffect } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { LogOut } from "lucide-react";

interface Props {
  user: { name: string; sigla: string; dni: string } | null;
  onLogout: () => void;
}

export function UserMenu({ user, onLogout }: Props) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function onClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onClick);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onClick);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, []);

  if (!user) return null;

  const initials = user.name.split(" ").filter(Boolean).map(w => w[0]).slice(0, 2).join("").toUpperCase();

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen(o => !o)}
        className="flex items-center justify-center w-8 h-8 rounded-full text-[11px] font-semibold flex-shrink-0 transition-colors"
        style={{ background: "var(--bg-elevated)", border: "1px solid var(--border-md)", color: "var(--text-secondary)" }}
        title={user.name}
        aria-label={`Menú de usuario — ${user.name}`}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        {initials}
      </button>

      <AnimatePresence>
        {open && (
          <motion.div
            role="menu"
            aria-label="Menú de usuario"
            className="absolute right-0 top-[calc(100%+8px)] w-60 rounded-lg overflow-hidden z-30"
            style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", boxShadow: "0 8px 24px rgba(0,0,0,0.16)", transformOrigin: "top right" }}
            initial={{ opacity: 0, y: -6, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1, transition: { duration: 0.16 } }}
            exit={{ opacity: 0, y: -6, scale: 0.98, transition: { duration: 0.12 } }}
          >
            <div className="px-3.5 py-3" style={{ borderBottom: "1px solid var(--border)" }}>
              <p className="text-[13px] font-medium truncate" style={{ color: "var(--text-primary)" }}>{user.name}</p>
              <p className="text-[11px] mt-0.5" style={{ color: "var(--text-muted)" }}>DNI {user.dni} · {user.sigla}</p>
            </div>
            <div className="px-3.5 py-2.5 flex items-center gap-2" style={{ borderBottom: "1px solid var(--border)" }}>
              <img src="/mpfs.png" alt="MPF" className="h-4 w-auto opacity-40 flex-shrink-0" />
              <p className="text-[10px] font-medium uppercase tracking-wide" style={{ color: "var(--text-muted)" }}>
                Min. Público Fiscal · Salta
              </p>
            </div>
            <button
              role="menuitem"
              onClick={() => { setOpen(false); onLogout(); }}
              className="w-full flex items-center gap-2 px-3.5 py-2.5 text-[13px] font-medium transition-colors"
              style={{ color: "var(--red)" }}
              onMouseEnter={e => { (e.currentTarget as HTMLButtonElement).style.background = "var(--bg-elevated)"; }}
              onMouseLeave={e => { (e.currentTarget as HTMLButtonElement).style.background = "transparent"; }}
            >
              <LogOut className="w-3.5 h-3.5" /> Cerrar sesión
            </button>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
