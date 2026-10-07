"use client";

import { useState, useEffect, useCallback } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { InputTextarea } from "primereact/inputtextarea";
import { SelectButton } from "primereact/selectbutton";
import { Tag, type TagProps } from "primereact/tag";
import { Cpu, AppWindow, CircleHelp, CheckCircle2, Loader2, Star, Clock, Inbox, ExternalLink } from "lucide-react";
import { api, type MiToken } from "@/lib/api";
import type { User } from "@/lib/api";
import { FaroIcon } from "@/components/FaroIcon";
import { FxBanner } from "@/components/feedback/FxBanner";
import { cn } from "@/lib/utils";
import { hasRealSigla } from "@/lib/format";

interface Props {
  open: boolean;
  user: User | null;
  onClose: () => void;
}

type Categoria = "hardware" | "software" | "otro";
type Tab = "nuevo" | "mios";

const TABS: { value: Tab; label: string }[] = [
  { value: "nuevo", label: "Nuevo token" },
  { value: "mios", label: "Mis tokens" },
];

const CATEGORIAS: { value: Categoria; label: string; hint: string; icon: typeof Cpu }[] = [
  { value: "hardware", label: "Hardware", hint: "El aparato: celu, compu, cable", icon: Cpu },
  { value: "software", label: "Software", hint: "Un programa o la web", icon: AppWindow },
  { value: "otro", label: "Otro", hint: "No estoy seguro", icon: CircleHelp },
];

const ESTADO_META: Record<MiToken["estado"], { label: string; severity: TagProps["severity"]; icon: React.ReactNode }> = {
  EnEspera: { label: "En espera", severity: "warning", icon: <Clock className="h-3 w-3" aria-hidden="true" /> },
  Reasignado: { label: "En curso", severity: "info", icon: <Loader2 className="h-3 w-3" aria-hidden="true" /> },
  Finalizado: { label: "Resuelto", severity: "success", icon: <CheckCircle2 className="h-3 w-3" aria-hidden="true" /> },
};

const LABEL = "block mb-1.5 text-fx-label uppercase text-fx-text-2";

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

  // Abre Faro ya logueado con la misma identidad (DNI) — el oficial no
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

  const showForm = tokenNumero === null && tab === "nuevo";

  return (
    <Dialog
      visible={open}
      onHide={handleClose}
      closable={!loading}
      dismissableMask
      draggable={false}
      resizable={false}
      // Pantalla completa en < sm. Va por pt (no className/maskClassName): en
      // modo unstyled `maskClassName` no se aplica y el pt del componente se
      // fusiona después del global, así gana el merge.
      pt={{
        root: { className: "w-[min(28rem,100%)] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0" },
        mask: { className: "max-sm:p-0" },
      }}
      header={
        <span className="flex items-start gap-3">
          <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-fx-md bg-fx-surface-3 text-fx-text-2">
            <FaroIcon className="h-4 w-4" aria-hidden="true" />
          </span>
          <span className="min-w-0">
            <span className="block">Soporte</span>
            <span className="mt-0.5 block text-fx-body-sm font-normal tracking-normal text-fx-text-2">
              Reportá un problema o seguí el estado de tus reportes.
            </span>
          </span>
        </span>
      }
      footer={
        showForm ? (
          <>
            <Button text severity="secondary" label="Cancelar" disabled={loading} onClick={handleClose} />
            <Button
              label="Enviar"
              icon={<FaroIcon className="h-4 w-4" aria-hidden="true" />}
              loading={loading}
              disabled={!isValid}
              onClick={handleSubmit}
            />
          </>
        ) : undefined
      }
    >
      {tokenNumero !== null ? (
        <div role="status" className="flex flex-col items-center gap-3 py-2 text-center">
          <span className="inline-flex h-12 w-12 items-center justify-center rounded-fx-lg bg-fx-success-soft text-fx-success">
            <CheckCircle2 className="h-6 w-6" aria-hidden="true" />
          </span>
          <div>
            <p className="m-0 text-fx-h3 text-fx-text">Token #{tokenNumero} registrado</p>
            <p className="m-0 mt-1 text-fx-body-sm text-fx-text-2">
              El equipo de soporte lo va a atender pronto. Podés seguir el estado en &quot;Mis reportes&quot;.
            </p>
          </div>
          <div className="mt-1 flex w-full flex-col gap-2">
            <Button
              severity="secondary"
              label="Ver mis tokens"
              className="w-full"
              onClick={() => { setTokenNumero(null); setDescripcion(""); setTab("mios"); }}
            />
            <Button text severity="secondary" label="Cerrar" className="w-full" onClick={handleClose} />
          </div>
        </div>
      ) : (
        <div className="space-y-4">
          <SelectButton
            value={tab}
            onChange={(e) => e.value && setTab(e.value as Tab)}
            allowEmpty={false}
            options={TABS}
            optionLabel="label"
            optionValue="value"
            pt={{
              root: { className: "w-full", "aria-label": "Sección de soporte" },
              button: { className: "flex-1" },
            }}
          />

          {tab === "nuevo" ? (
            <div className="space-y-3.5">
              <fieldset className="m-0 border-0 p-0">
                <legend className={LABEL}>Categoría</legend>
                <SelectButton
                  value={categoria}
                  onChange={(e) => e.value && setCategoria(e.value as Categoria)}
                  allowEmpty={false}
                  options={CATEGORIAS}
                  optionLabel="label"
                  optionValue="value"
                  itemTemplate={(o: (typeof CATEGORIAS)[number]) => (
                    <>
                      <o.icon className="h-4 w-4" aria-hidden="true" />
                      <span>{o.label}</span>
                      <span className="text-[11px] font-normal leading-tight opacity-90">{o.hint}</span>
                    </>
                  )}
                  pt={{
                    root: { className: "w-full", "aria-label": "Categoría" },
                    button: { className: "flex-1 flex-col justify-start h-auto py-2.5 text-center" },
                  }}
                />
              </fieldset>

              <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
                <div>
                  <label htmlFor="soporte-numero-interno" className={LABEL}>Nro. interno</label>
                  <InputText
                    id="soporte-numero-interno"
                    value={numeroInterno}
                    onChange={(e) => setNumeroInterno(e.target.value)}
                    placeholder="1042"
                    autoComplete="off"
                  />
                </div>
                <div>
                  <label htmlFor="soporte-telefono" className={LABEL}>Teléfono</label>
                  <InputText
                    id="soporte-telefono"
                    value={telefono}
                    onChange={(e) => setTelefono(e.target.value)}
                    placeholder="11 5555-1234"
                    type="tel"
                    autoComplete="tel"
                  />
                </div>
              </div>

              <div>
                <label htmlFor="soporte-descripcion" className={LABEL}>¿Qué pasó?</label>
                <InputTextarea
                  id="soporte-descripcion"
                  rows={3}
                  value={descripcion}
                  onChange={(e) => setDescripcion(e.target.value)}
                  placeholder="Contanos qué error viste y en qué pantalla…"
                  maxLength={500}
                  aria-describedby="soporte-descripcion-hint"
                />
                <p id="soporte-descripcion-hint" className="m-0 mt-1 text-xs text-fx-text-3">
                  {descripcion.trim().length < 10
                    ? `${10 - descripcion.trim().length} caracteres más`
                    : "Listo para enviar"}
                </p>
              </div>

              {user && (
                <p className="m-0 text-xs text-fx-text-3">
                  Se va a reportar como <span className="text-fx-text-2">{user.name}</span>
                  {hasRealSigla(user.sigla) ? ` (${user.sigla.trim()})` : ""}.
                </p>
              )}

              {error && <FxBanner tone="error">{error}</FxBanner>}
            </div>
          ) : (
            <div className="space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="m-0 text-fx-label uppercase text-fx-text-2">Tus tokens</p>
                <Button
                  link
                  size="small"
                  icon={<ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />}
                  label="Ver todo en el portal de soporte"
                  loading={openingFaro}
                  onClick={handleAbrirFaro}
                  className="px-0"
                />
              </div>
              {loadingTokens ? (
                <div className="flex justify-center py-8" role="status" aria-live="polite">
                  <Loader2 className="h-5 w-5 text-fx-text-3 motion-safe:animate-spin" aria-hidden="true" />
                  <span className="sr-only">Cargando tus tokens…</span>
                </div>
              ) : tokens.length === 0 ? (
                <div className="flex flex-col items-center gap-2 py-8 text-center">
                  <Inbox className="h-6 w-6 text-fx-text-3" aria-hidden="true" />
                  <p className="m-0 text-xs text-fx-text-2">Todavía no reportaste ningún problema</p>
                </div>
              ) : (
                <ul className="m-0 space-y-2 p-0">
                  {tokens.map((t) => {
                    const meta = ESTADO_META[t.estado];
                    return (
                      <li key={t.id_token} className="fx-card list-none p-3">
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-fx-body-sm font-bold text-fx-text">{t.servicio}</span>
                          {meta && <Tag severity={meta.severity} value={meta.label} icon={meta.icon} />}
                        </div>
                        <p className="m-0 mt-1 line-clamp-2 text-xs text-fx-text-2">{t.descripcion}</p>
                        {t.descripcion_resolucion && (
                          <p className="m-0 mt-1.5 rounded-fx-md bg-fx-success-soft px-2 py-1.5 text-xs text-fx-success">
                            {t.descripcion_resolucion}
                          </p>
                        )}
                        <p className="m-0 mt-2 flex items-center gap-1 text-xs text-fx-text-3">
                          <Clock className="h-3 w-3" aria-hidden="true" />
                          {new Date(t.fecha_creacion).toLocaleDateString("es-AR")}
                        </p>

                        {t.estado === "Finalizado" && (
                          <div className="mt-2 flex flex-wrap items-center gap-1 border-t border-fx-border pt-2">
                            <span className="mr-1 text-xs text-fx-text-3">
                              {t.puntuacion ? "Tu calificación:" : "¿Cómo fue la atención?"}
                            </span>
                            {[1, 2, 3, 4, 5].map((n) => {
                              const marked = (t.puntuacion ?? 0) >= n;
                              return (
                                <button
                                  key={n}
                                  type="button"
                                  disabled={!!t.puntuacion}
                                  onClick={() => handleCalificar(t.id_token, n)}
                                  aria-label={`Calificar con ${n} estrella${n > 1 ? "s" : ""}`}
                                  aria-pressed={marked}
                                  className={cn(
                                    "inline-flex h-8 w-8 items-center justify-center rounded-fx-sm fx-focus-ring",
                                    t.puntuacion ? "cursor-default" : "cursor-pointer hover:bg-fx-surface-3",
                                  )}
                                >
                                  <Star
                                    className={cn("h-4 w-4", marked ? "fill-current text-fx-warning" : "text-fx-text-3")}
                                    aria-hidden="true"
                                  />
                                </button>
                              );
                            })}
                          </div>
                        )}
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          )}
        </div>
      )}
    </Dialog>
  );
}
