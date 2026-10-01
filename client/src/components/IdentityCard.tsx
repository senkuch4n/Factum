"use client";

import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Camera, Check, RotateCcw } from "lucide-react";
import { agentFileURL } from "@/lib/agent";
import { Lightbox } from "./Lightbox";

const ENTER = [0.22, 1, 0.36, 1] as const;

/**
 * Fila de "roster" para la identificación de una persona (fiscal / denunciante):
 * avatar circular a la izquierda, nombre + rol + DNI en el medio, y acción de
 * captura a la derecha. Pensado para ir dentro de un contenedor con `divide-y`.
 */
export function IdentityCard({
  label, role, icon: Icon, name, dni, blobURL, agentFilename, onCapture, loading, done,
}: {
  label: string; role: string; icon: React.ElementType;
  name?: string; dni?: string;
  blobURL?: string; agentFilename?: string;
  onCapture: () => void; loading: boolean; done: boolean;
}) {
  const [lightbox, setLightbox] = useState(false);
  const previewSrc = blobURL || (agentFilename ? agentFileURL(agentFilename) : undefined);
  const showPhoto = done && !!previewSrc;

  const subtitle = name
    ? dni ? `${name} · DNI ${dni}` : name
    : dni ? `DNI ${dni}`
    : showPhoto ? "Foto registrada"
    : "Sin foto";

  return (
    <>
      <div className="flex items-center gap-3.5 py-3.5">
        {/* ── Avatar ── */}
        <button
          type="button"
          onClick={showPhoto ? () => setLightbox(true) : onCapture}
          disabled={loading}
          aria-label={showPhoto ? `Ver foto — ${label}` : `Tomar foto — ${label}`}
          className="relative h-14 w-14 flex-shrink-0 rounded-full outline-none transition focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--bg-elevated)]"
        >
          <motion.span
            key={showPhoto ? "photo" : "empty"}
            className="block h-full w-full overflow-hidden rounded-full"
            initial={{ opacity: 0, scale: 0.94 }}
            animate={{ opacity: 1, scale: 1 }}
            transition={{ duration: 0.18, ease: ENTER }}
          >
            {showPhoto ? (
              <img src={previewSrc} alt={`Foto — ${label}`} className="h-full w-full object-cover" />
            ) : (
              <span
                className="flex h-full w-full items-center justify-center rounded-full border-2 border-dashed"
                style={{ borderColor: "var(--border)", background: "var(--bg-surface)" }}
              >
                {loading
                  ? <span className="h-4 w-4 animate-spin rounded-full border-2 border-current/20 border-t-current" style={{ color: "var(--text-muted)" }} />
                  : <Icon className="h-5 w-5" style={{ color: "var(--text-muted)" }} aria-hidden="true" />}
              </span>
            )}
          </motion.span>

          {showPhoto && (
            <motion.span
              className="absolute -bottom-0.5 -right-0.5 flex h-5 w-5 items-center justify-center rounded-full bg-emerald-500 ring-2 ring-[var(--bg-elevated)]"
              initial={{ scale: 0.4, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              transition={{ type: "spring", stiffness: 500, damping: 28 }}
            >
              <Check className="h-3 w-3 text-white" strokeWidth={3} />
            </motion.span>
          )}
        </button>

        {/* ── Nombre + rol + DNI ── */}
        <div className="min-w-0 flex-1">
          <p className="flex items-baseline gap-1.5 text-sm leading-tight">
            <span className="font-semibold" style={{ color: "var(--text-primary)" }}>{label}</span>
            <span style={{ color: "var(--text-muted)" }}>· {role}</span>
          </p>
          <p className="mt-0.5 truncate text-xs" style={{ color: "var(--text-muted)" }}>{subtitle}</p>
        </div>

        {/* ── Acción ── */}
        <motion.button
          type="button"
          onClick={onCapture}
          disabled={loading}
          whileTap={{ scale: 0.96 }}
          aria-label={`${showPhoto ? "Retomar" : "Tomar"} foto — ${label}`}
          className="btn-secondary btn-sm flex flex-shrink-0 items-center gap-1.5"
        >
          {loading ? (
            <><span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current/20 border-t-current" /> Abriendo…</>
          ) : showPhoto ? (
            <><RotateCcw className="h-3.5 w-3.5" /> <span className="hidden sm:inline">Retomar</span></>
          ) : (
            <><Camera className="h-3.5 w-3.5" /> Tomar foto</>
          )}
        </motion.button>
      </div>

      <AnimatePresence>
        {lightbox && previewSrc && <Lightbox src={previewSrc} onClose={() => setLightbox(false)} />}
      </AnimatePresence>
    </>
  );
}
