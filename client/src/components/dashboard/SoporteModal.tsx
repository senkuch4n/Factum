"use client";

import { useState, useEffect, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Cpu, AppWindow, CircleHelp, CheckCircle2, Loader2, X, Star, Clock, Inbox, ExternalLink } from "lucide-react";
import { api, type MiToken } from "@/lib/api";
import type { User } from "@/lib/api";
import { FaroIcon } from "@/components/FaroIcon";
import { cn } from "@/lib/utils";

interface Props {
  open: boolean;
  user: User | null;
  onClose: () => void;
}

type Categoria = "hardware" | "software" | "otro";
type Tab = "nuevo" | "mios";

const CATEGORIAS: { value: Categoria; label: string; hint: string; icon: typeof Cpu }[] = [
  { value: "hardware", label: "Hardware", hint: "El aparato: celu, compu, cable", icon: Cpu },
  { value: "software", label: "Software", hint: "Un programa o la web", icon: AppWindow },
  { value: "otro", label: "Otro", hint: "No estoy seguro", icon: CircleHelp },
];

const ESTADO_META: Record<MiToken["estado"], { label: string; badge: string }> = {
  EnEspera: { label: "En espera", badge: "badge-amber" },
  Reasignado: { label: "En curso", badge: "badge-blue" },
  Finalizado: { label: "Resuelto", badge: "badge-green" },
};

export function SoporteModal({ open, user, onClose }: Props) {
  const [tab, setTab] = useState<Tab>("nuevo");

  // ── Nuevo reporte ──────────────────────────────────────────────
  const [categoria, setCategoria] = useState<Categoria>("hardware");
  const [descripcion, setDescripcion] = useState("");
  const [numeroInterno, setNumeroInterno] = useState("");
  const [telefono, setTelefono] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [tokenNumero, setTokenNumero] = useState<number | null>(null);

  // ── Mis reportes ───────────────────────────────────────────────
  const [tokens, setTokens] = useState<MiToken[]>([]);
  const [loadingTokens, setLoadingTokens] = useState(false);
  const [openingFaro, setOpeningFaro] = useState(false);

  const isValid =
    descripcion.trim().length >= 10 && numeroInterno.trim().length >= 3 && telefono.trim().length >= 6;

  const cargarMisReportes = useCallback(async () => {
    setLoadingTokens(true);
    try {
      const { tokens } = await api.listarMisReportes();
      setTokens(tokens);
    } catch {
      // silencioso: no bloquea el resto del panel si Faro no responde
    } finally {
      setLoadingTokens(false);
    }
  }, []);

  useEffect(() => {
    if (open && tab === "mios") cargarMisReportes();
  }, [open, tab, cargarMisReportes]);

  useEffect(() => {
    if (!open) return;
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") handleClose();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, loading]);

  function reset() {
    setTab("nuevo");
    setCategoria("hardware");
    setDescripcion("");
    setNumeroInterno("");
    setTelefono("");
    setError("");
    setTokenNumero(null);
  }

  function handleClose() {
    if (loading) return;
    onClose();
    setTimeout(reset, 250);
  }

  async function handleSubmit() {
    if (!isValid || loading) return;
    setLoading(true);
    setError("");
    try {
      const data = await api.reportarProblema({
        categoria,
        descripcion: descripcion.trim(),
        numero_interno: numeroInterno.trim(),
        telefono: telefono.trim(),
      });
      setTokenNumero(data.token_numero);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo registrar el token");
    } finally {
      setLoading(false);
    }
  }

  // Abre Faro ya logueado con la misma identidad de MPF — el oficial no
  // vuelve a poner su contraseña ahí.
  async function handleAbrirFaro() {
    if (openingFaro) return;
    setOpeningFaro(true);
    try {
      const { url } = await api.obtenerLinkFaro();
      window.open(url, "_blank", "noopener,noreferrer");
    } catch {
      // silencioso: si Faro no responde, el usuario sigue viendo sus reportes acá mismo
    } finally {
      setOpeningFaro(false);
    }
  }

  async function handleCalificar(idToken: number, puntuacion: number) {
    setTokens((prev) => prev.map((t) => (t.id_token === idToken ? { ...t, puntuacion } : t)));
    try {
      await api.calificarReporte(idToken, puntuacion);
    } catch {
      cargarMisReportes(); // revierte al estado real si falló
    }
  }

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-50 flex items-center justify-center p-4"
          style={{ background: "rgba(0,0,0,0.7)", backdropFilter: "blur(14px)", overscrollBehavior: "contain" }}
          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
          onClick={handleClose}
        >
          <motion.div
            className="w-full max-w-sm rounded-lg overflow-hidden shadow-2xl"
            style={{ background: "var(--bg-card)", border: "1px solid var(--border)" }}
            initial={{ scale: 0.88, y: 28, opacity: 0 }}
            animate={{ scale: 1, y: 0, opacity: 1 }}
            exit={{ scale: 0.94, y: 10, opacity: 0 }}
            transition={{ type: "spring", stiffness: 380, damping: 28 }}
            onClick={(e) => e.stopPropagation()}
          >
            {tokenNumero !== null ? (
              <div className="p-6 flex flex-col items-center text-center gap-3">
                <motion.div
                  initial={{ scale: 0 }} animate={{ scale: 1 }}
                  transition={{ type: "spring", stiffness: 400, damping: 18 }}
                  className="w-12 h-12 rounded-md flex items-center justify-center"
                  style={{ background: "rgba(16,185,129,0.12)", border: "1px solid rgba(16,185,129,0.25)" }}
                >
                  <CheckCircle2 className="w-6 h-6 text-emerald-500" aria-hidden="true" />
                </motion.div>
                <div>
                  <p className="text-sm font-bold" style={{ color: "var(--text-primary)" }}>
                    Token #{tokenNumero} registrado
                  </p>
                  <p className="text-xs mt-1" style={{ color: "var(--text-muted)" }}>
                    El equipo del GFD lo va a atender pronto. Podés seguir el estado en &quot;Mis reportes&quot;.
                  </p>
                </div>
                <button
                  className="btn-secondary rounded-md w-full mt-1"
                  onClick={() => { setTokenNumero(null); setDescripcion(""); setTab("mios"); }}
                >
                  Ver mis tokens
                </button>
                <button className="btn-ghost rounded-md w-full" onClick={handleClose}>
                  Cerrar
                </button>
              </div>
            ) : (
              <>
                <div className="px-5 pt-5 pb-3 flex items-start gap-3" style={{ borderBottom: "1px solid var(--border)" }}>
                  <div
                    className="w-9 h-9 rounded-md flex items-center justify-center flex-shrink-0"
                    style={{ background: "var(--btn-secondary-bg)", border: "1px solid var(--border-md)" }}
                  >
                    <FaroIcon className="w-4 h-4" style={{ color: "var(--text-secondary)" }} aria-hidden="true" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-bold" style={{ color: "var(--text-primary)" }}>Faro - Sistema de tokens</p>
                    <p className="text-xs mt-0.5" style={{ color: "var(--text-muted)" }}>
                      Reportá un problema a traves de un token o seguílo con Faro
                    </p>
                  </div>
                  <button
                    onClick={handleClose}
                    className="flex-shrink-0 w-6 h-6 rounded-lg flex items-center justify-center"
                    style={{ color: "var(--text-muted)" }}
                    aria-label="Cerrar"
                  >
                    <X className="w-4 h-4" aria-hidden="true" />
                  </button>
                </div>

                <div className="px-4 pt-3 flex gap-1">
                  {([
                    ["nuevo", "Nuevo token"],
                    ["mios", "Mis tokens"],
                  ] as const).map(([value, label]) => (
                    <button
                      key={value}
                      onClick={() => setTab(value)}
                      aria-pressed={tab === value}
                      className="flex-1 text-xs font-semibold py-2 rounded-lg transition-colors"
                      style={{
                        background: tab === value ? "var(--btn-secondary-bg)" : "transparent",
                        color: tab === value ? "var(--text-primary)" : "var(--text-muted)",
                      }}
                    >
                      {label}
                    </button>
                  ))}
                </div>

                {tab === "nuevo" ? (
                  <>
                    <div className="p-4 space-y-3.5">
                      <div>
                        <p className="section-label mb-1.5" style={{ color: "var(--section-label-c)" }}>Categoría</p>
                        <div className="grid grid-cols-3 gap-2">
                          {CATEGORIAS.map(({ value, label, hint, icon: Icon }) => {
                            const selected = categoria === value;
                            return (
                              <button
                                key={value}
                                type="button"
                                onClick={() => setCategoria(value)}
                                aria-pressed={selected}
                                className="flex flex-col items-center gap-1.5 py-2.5 px-1 rounded-md text-xs font-semibold transition"
                                style={{
                                  background: selected ? "rgba(13,148,136,0.10)" : "var(--bg-elevated)",
                                  border: selected ? "1px solid rgba(13,148,136,0.35)" : "1px solid var(--border)",
                                  color: selected ? "#0d9488" : "var(--text-secondary)",
                                }}
                              >
                                <Icon className="w-4 h-4" aria-hidden="true" />
                                {label}
                                <span
                                  className="text-[8.5px] font-normal normal-case leading-tight text-center"
                                  style={{ color: selected ? "#0d9488" : "var(--text-muted)" }}
                                >
                                  {hint}
                                </span>
                              </button>
                            );
                          })}
                        </div>
                      </div>

                      <div className="grid grid-cols-2 gap-2.5">
                        <div>
                          <label htmlFor="soporte-numero-interno" className="section-label mb-1.5 block" style={{ color: "var(--section-label-c)" }}>Nro. interno</label>
                          <input
                            id="soporte-numero-interno"
                            className="input rounded-md"
                            value={numeroInterno}
                            onChange={(e) => setNumeroInterno(e.target.value)}
                            placeholder="1042"
                            autoComplete="off"
                          />
                        </div>
                        <div>
                          <label htmlFor="soporte-telefono" className="section-label mb-1.5 block" style={{ color: "var(--section-label-c)" }}>Teléfono</label>
                          <input
                            id="soporte-telefono"
                            className="input rounded-md"
                            value={telefono}
                            onChange={(e) => setTelefono(e.target.value)}
                            placeholder="11 5555-1234"
                            type="tel"
                            autoComplete="tel"
                          />
                        </div>
                      </div>

                      <div>
                        <label htmlFor="soporte-descripcion" className="section-label mb-1.5 block" style={{ color: "var(--section-label-c)" }}>¿Qué pasó?</label>
                        <textarea
                          id="soporte-descripcion"
                          className="input rounded-md min-h-[80px] resize-none"
                          value={descripcion}
                          onChange={(e) => setDescripcion(e.target.value)}
                          placeholder="Contanos qué error viste y en qué pantalla…"
                          maxLength={500}
                        />
                        <p className="text-[10px] mt-1" style={{ color: "var(--text-muted)" }}>
                          {descripcion.trim().length < 10
                            ? `${10 - descripcion.trim().length} caracteres más`
                            : "Listo para enviar"}
                        </p>
                      </div>

                      {user && (
                        <p className="text-[10px]" style={{ color: "var(--text-muted)" }}>
                          Se va a reportar como <span style={{ color: "var(--text-secondary)" }}>{user.name}</span>
                          {user.sigla && user.sigla !== "-" ? ` (${user.sigla})` : ""}.
                        </p>
                      )}

                      <AnimatePresence>
                        {error && (
                          <motion.p
                            initial={{ opacity: 0, height: 0 }}
                            animate={{ opacity: 1, height: "auto" }}
                            exit={{ opacity: 0, height: 0 }}
                            className="text-xs rounded-lg px-3 py-2"
                            style={{ background: "rgba(220,38,38,0.08)", color: "#dc2626" }}
                          >
                            {error}
                          </motion.p>
                        )}
                      </AnimatePresence>
                    </div>

                    <div className="px-4 pb-4 flex gap-2">
                      <button className="btn-ghost rounded-md flex-1" onClick={handleClose} disabled={loading}>
                        Cancelar
                      </button>
                      <button
                        className={cn("btn-primary rounded-md flex-1 gap-1.5", !isValid && "opacity-50")}
                        onClick={handleSubmit}
                        disabled={!isValid || loading}
                      >
                        {loading ? <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> : <FaroIcon className="w-4 h-4" aria-hidden="true" />}
                        Enviar
                      </button>
                    </div>
                  </>
                ) : (
                  <div className="p-4 max-h-[360px] overflow-y-auto">
                    <div className="flex items-center justify-between mb-3">
                      <p className="section-label" style={{ color: "var(--section-label-c)" }}>Tus tokens</p>
                      <button
                        onClick={handleAbrirFaro}
                        disabled={openingFaro}
                        className="flex items-center gap-1.5 text-[11px] font-bold disabled:opacity-60"
                        style={{ color: "#0d9488" }}
                        title="Entrás ya logueado, sin poner tu contraseña de nuevo"
                      >
                        {openingFaro ? (
                          <Loader2 className="w-3 h-3 animate-spin" aria-hidden="true" />
                        ) : (
                          <ExternalLink className="w-3 h-3" aria-hidden="true" />
                        )}
                        Ver todo en Faro
                      </button>
                    </div>
                    {loadingTokens ? (
                      <div className="flex justify-center py-8" role="status" aria-live="polite">
                        <Loader2 className="w-5 h-5 animate-spin" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
                        <span className="sr-only">Cargando tus tokens…</span>
                      </div>
                    ) : tokens.length === 0 ? (
                      <div className="flex flex-col items-center gap-2 py-8 text-center">
                        <Inbox className="w-6 h-6" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
                        <p className="text-xs" style={{ color: "var(--text-muted)" }}>
                          Todavía no reportaste ningún problema
                        </p>
                      </div>
                    ) : (
                      <div className="space-y-2">
                        {tokens.map((t) => {
                          const meta = ESTADO_META[t.estado];
                          return (
                            <div
                              key={t.id_token}
                              className="rounded-md p-3"
                              style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
                            >
                              <div className="flex items-center justify-between gap-2">
                                <span className="text-xs font-bold" style={{ color: "var(--text-primary)" }}>
                                  {t.servicio}
                                </span>
                                <span className={cn("badge", meta.badge)}>{meta.label}</span>
                              </div>
                              <p className="text-[11px] mt-1 line-clamp-2" style={{ color: "var(--text-secondary)" }}>
                                {t.descripcion}
                              </p>
                              {t.descripcion_resolucion && (
                                <p className="text-[11px] mt-1.5 rounded-lg px-2 py-1.5" style={{ background: "rgba(16,185,129,0.08)", color: "#059669" }}>
                                  {t.descripcion_resolucion}
                                </p>
                              )}
                              <div className="flex items-center gap-1 mt-2 text-[10px]" style={{ color: "var(--text-muted)" }}>
                                <Clock className="w-3 h-3" aria-hidden="true" />
                                {new Date(t.fecha_creacion).toLocaleDateString("es-AR")}
                              </div>

                              {t.estado === "Finalizado" && (
                                <div className="flex items-center gap-1 mt-2 pt-2" style={{ borderTop: "1px solid var(--border)" }}>
                                  <span className="text-[10px] mr-1" style={{ color: "var(--text-muted)" }}>
                                    {t.puntuacion ? "Tu calificación:" : "¿Cómo fue la atención?"}
                                  </span>
                                  {[1, 2, 3, 4, 5].map((n) => (
                                    <button
                                      key={n}
                                      disabled={!!t.puntuacion}
                                      onClick={() => handleCalificar(t.id_token, n)}
                                      className={t.puntuacion ? "cursor-default" : "cursor-pointer"}
                                      aria-label={`Calificar con ${n} estrella${n > 1 ? "s" : ""}`}
                                    >
                                      <Star
                                        className="w-3.5 h-3.5"
                                        aria-hidden="true"
                                        style={{
                                          color: (t.puntuacion ?? 0) >= n ? "#f59e0b" : "var(--text-muted)",
                                          fill: (t.puntuacion ?? 0) >= n ? "#f59e0b" : "none",
                                        }}
                                      />
                                    </button>
                                  ))}
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    )}
                  </div>
                )}
              </>
            )}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
