"use client";

import { useState, useEffect, useCallback } from "react";
import { useRouter } from "next/navigation";
import { motion, AnimatePresence } from "framer-motion";
import { ShieldCheck, Eye, EyeOff, Loader2, Lock, User, Hash, Wifi, WifiOff, AlertTriangle } from "lucide-react";
import { api } from "@/lib/api";
import { agent } from "@/lib/agent";
import { cn } from "@/lib/utils";
import { ThemeToggle } from "@/components/ThemeToggle";
import { useTheme } from "@/lib/theme";
import WebcamPixelGrid from "@/components/ui/webcam-pixel-grid";
import { usePublicConfig } from "@/hooks/usePublicConfig";

const EASE = [0.22, 1, 0.36, 1] as const;

export default function LoginPage() {
  const router = useRouter();
  const { isDark } = useTheme();
  const { organizationName, organizationLogoSrc } = usePublicConfig();
  const [orgLogoFailed, setOrgLogoFailed] = useState(false);
  const showOrgLogo = !!organizationLogoSrc && !orgLogoFailed;

  const [form, setForm]         = useState({ dni: "", username: "", password: "" });
  const [showPass, setShowPass] = useState(false);
  const [agentOnline, setAgent] = useState<boolean | null>(null);
  const [loading, setLoading]   = useState(false);
  const [error, setError]       = useState("");
  const [focused, setFocused]   = useState<string | null>(null);
  const [camReady,  setCamReady]  = useState(false);
  const [camDenied, setCamDenied] = useState(false);

  const handleCamReady  = useCallback(() => setCamReady(true), []);
  const handleCamError  = useCallback((err: Error) => {
    console.error("Webcam error:", err);
    setCamDenied(true);
  }, []);

  useEffect(() => {
    agent.isOnline().then(setAgent);
    const iv = setInterval(() => agent.isOnline().then(setAgent), 5000);
    return () => clearInterval(iv);
  }, []);

  function set(field: string, value: string) {
    setForm(f => ({ ...f, [field]: value }));
    setError("");
  }

  async function handleLogin(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError("");
    try {
      await api.login(form.dni, form.username, form.password);
      router.push("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Credenciales incorrectas");
    } finally {
      setLoading(false);
    }
  }

  const fields = [
    { key: "dni",      label: "DNI",     icon: Hash, type: "text", ph: "12345678", max: 8  },
    { key: "username", label: "Usuario", icon: User, type: "text", ph: "jperez",   max: 50 },
  ] as const;

  /* ── Theme-derived tokens (backdrop del panel visual) ── */
  const gridBg     = isDark ? "#0a0a0a" : "#fafafa";
  const scanColor  = isDark ? "rgba(255,255,255,0.012)" : "rgba(0,0,0,0.018)";
  const panelFade  = isDark
    ? "linear-gradient(0deg, rgba(0,0,0,0.88) 0%, rgba(0,0,0,0.15) 42%, transparent 68%)"
    : "linear-gradient(0deg, rgba(250,250,250,0.92) 0%, rgba(250,250,250,0.2) 42%, transparent 68%)";
  const panelEdge  = isDark
    ? "linear-gradient(90deg, transparent 0%, transparent 82%, rgba(0,0,0,0.5) 100%)"
    : "linear-gradient(90deg, transparent 0%, transparent 82%, rgba(250,250,250,0.6) 100%)";

  const inputIconColor = (key: string) => focused === key ? "var(--blue-lg)" : "var(--text-muted)";
  const inputBorder = (key: string) => {
    if (error)           return "rgba(220,38,38,0.4)";
    if (focused === key) return "var(--border-accent)";
    return "var(--border)";
  };
  const inputShadow = (key: string) => focused === key ? "var(--glow-blue)" : "none";

  return (
    <div className="min-h-screen flex flex-col lg:flex-row" style={{ background: "var(--bg-base)" }}>

      {/* ══════════════ Panel visual ══════════════ */}
      <div className="relative lg:w-[57%] h-[34vh] lg:h-screen overflow-hidden flex-shrink-0">
        {!camDenied ? (
          <WebcamPixelGrid
            gridCols={60}
            gridRows={40}
            maxElevation={50}
            motionSensitivity={0.25}
            elevationSmoothing={0.2}
            colorMode="webcam"
            backgroundColor={gridBg}
            mirror={true}
            gapRatio={0.05}
            invertColors={false}
            darken={isDark ? 0.6 : 0.35}
            borderColor={isDark ? "#ffffff" : "#000000"}
            borderOpacity={isDark ? 0.06 : 0.04}
            onWebcamReady={handleCamReady}
            onWebcamError={handleCamError}
          />
        ) : (
          <div className="absolute inset-0 overflow-hidden" style={{ background: "var(--bg-base)" }}>
            <motion.div
              className="absolute"
              style={{
                width: "70vw", height: "70vh", borderRadius: "50%",
                background: "radial-gradient(ellipse, rgba(13,148,136,0.16) 0%, transparent 70%)",
                top: "-15%", left: "-10%",
              }}
              animate={{ x: [0, 40, -15, 0], y: [0, -20, 35, 0], scale: [1, 1.08, 0.96, 1] }}
              transition={{ duration: 9, repeat: Infinity, ease: "easeInOut" }}
            />
            <motion.div
              className="absolute"
              style={{
                width: "55vw", height: "55vh", borderRadius: "50%",
                background: "radial-gradient(ellipse, rgba(45,212,191,0.1) 0%, transparent 70%)",
                bottom: "-15%", right: "-10%",
              }}
              animate={{ x: [0, -30, 20, 0], y: [0, 20, -30, 0], scale: [1, 0.92, 1.06, 1] }}
              transition={{ duration: 11, repeat: Infinity, ease: "easeInOut", delay: 2.5 }}
            />
            <div className="absolute inset-0" style={{
              backgroundImage: `
                linear-gradient(${scanColor} 1px, transparent 1px),
                linear-gradient(90deg, ${scanColor} 1px, transparent 1px)
              `,
              backgroundSize: "42px 42px",
            }} />
          </div>
        )}

        {/* Legibilidad: degradé inferior + borde hacia el panel de formulario */}
        <div className="absolute inset-0 pointer-events-none" style={{ background: panelFade }} />
        <div className="absolute inset-0 pointer-events-none hidden lg:block" style={{ background: panelEdge }} />

        {/* Organización emisora (Branding del backend) — esquina superior.
            Solo si hay nombre o logo; fondo propio para contrastar sobre la
            imagen de la cámara en cualquier tema. */}
        {(organizationName || showOrgLogo) && (
          <motion.div
            className="absolute top-6 left-6 right-6 lg:top-8 lg:left-8 lg:right-8 flex"
            initial={{ opacity: 0, y: -8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, ease: EASE }}
          >
            <div
              className="flex min-w-0 max-w-full items-center gap-2.5 rounded-lg px-2.5 py-1.5 backdrop-blur-sm"
              style={{
                background: isDark ? "rgba(0,0,0,0.5)" : "rgba(255,255,255,0.82)",
                color: isDark ? "rgba(255,255,255,0.92)" : "var(--text-primary)",
              }}
            >
              {showOrgLogo && (
                <img
                  src={organizationLogoSrc}
                  /* Con el nombre al lado, el logo es decorativo (no se lee dos veces). */
                  alt={organizationName ? "" : "Logo de la organización"}
                  className="h-6 w-auto max-w-[160px] shrink-0 object-contain"
                  onError={() => setOrgLogoFailed(true)}
                />
              )}
              {organizationName && (
                <span translate="no" className="min-w-0 truncate text-[13px] font-semibold" title={organizationName}>
                  {organizationName}
                </span>
              )}
            </div>
          </motion.div>
        )}

        {/* Marca + estado — esquina inferior */}
        <div className="absolute bottom-7 left-6 right-6 lg:bottom-10 lg:left-10 lg:right-10">
          <motion.div
            className="flex items-center gap-2.5 mb-3"
            initial={{ opacity: 0, y: 14 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.45, delay: 0.1, ease: EASE }}
          >
            <img
              src={isDark ? "/logo-theme-dark.svg" : "/logo-theme-white.svg"}
              alt="Factum"
              width={289}
              height={64}
              className="h-7 lg:h-8 w-auto"
              style={{ filter: isDark ? "drop-shadow(0 0 20px rgba(127,211,78,0.22))" : "drop-shadow(0 2px 8px rgba(0,0,0,0.15))" }}
            />
          </motion.div>
          <motion.p
            className="text-[13px] font-medium max-w-xs leading-snug"
            style={{ color: "rgba(255,255,255,0.55)" }}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.45, delay: 0.18, ease: EASE }}
          >
            Captura y preservación forense de evidencia digital.
          </motion.p>

          <motion.div
            className="flex items-center gap-2 mt-4 text-[11px] font-medium tracking-wide"
            style={{ color: "rgba(255,255,255,0.4)" }}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.4, delay: 0.28 }}
          >
            <span
              className={cn("w-1.5 h-1.5 rounded-full flex-shrink-0", camReady && "bg-green-400 animate-pulse")}
              style={{ background: camReady ? undefined : "rgba(255,255,255,0.25)" }}
              aria-hidden="true"
            />
            {camDenied ? "Cámara no disponible" : camReady ? "Cámara activa" : "Iniciando cámara…"}
          </motion.div>
        </div>
      </div>

      {/* ══════════════ Panel de acceso ══════════════ */}
      <div className="relative flex-1 flex flex-col" style={{ background: "var(--bg-surface)" }}>

        <div className="flex justify-end px-6 py-5 lg:px-10 lg:py-6">
          <ThemeToggle />
        </div>

        <div className="flex-1 flex items-center justify-center px-6 pb-12 lg:px-16">
          <div className="w-full max-w-[380px]">

            <motion.p
              className="section-label mb-2"
              initial={{ opacity: 0, y: -6 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.3, delay: 0.05 }}
            >
              Acceso
            </motion.p>
            <motion.h1
              className="hero-title text-2xl sm:text-3xl mb-1.5"
              style={{ color: "var(--text-primary)" }}
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.35, delay: 0.1, ease: EASE }}
            >
              Iniciar sesión
            </motion.h1>
            <motion.p
              className="text-sm mb-7"
              style={{ color: "var(--text-muted)" }}
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.35, delay: 0.15, ease: EASE }}
            >
              Ingresá con tus credenciales.
            </motion.p>

            {/* Estado del agente Tatana */}
            <motion.div
              className={cn(
                "flex items-center gap-2.5 rounded-lg px-3.5 py-2.5 mb-6 text-[11px] font-medium tracking-wide border",
                agentOnline === true  && "bg-green-500/[0.07] border-green-500/[0.18] text-green-600 dark:text-green-400",
                agentOnline === false && "bg-amber-500/[0.07] border-amber-500/[0.18] text-amber-600 dark:text-amber-400",
              )}
              style={agentOnline === null ? { background: "var(--bg-elevated)", borderColor: "var(--border)", color: "var(--text-muted)" } : undefined}
              role="status"
              aria-live="polite"
              initial={{ opacity: 0, x: -8 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ duration: 0.3, delay: 0.22 }}
            >
              {agentOnline === null  && <Loader2 className="w-3 h-3 animate-spin flex-shrink-0" aria-hidden="true" />}
              {agentOnline === true  && <Wifi    className="w-3 h-3 flex-shrink-0" aria-hidden="true" />}
              {agentOnline === false && <WifiOff className="w-3 h-3 flex-shrink-0" aria-hidden="true" />}
              <span>
                {agentOnline === null  && "Verificando agente…"}
                {agentOnline === true  && "Tatana conectado"}
                {agentOnline === false && "Tatana sin conexión"}
              </span>
              {agentOnline === true && (
                <span className="ml-auto w-1.5 h-1.5 rounded-full bg-green-400 animate-pulse flex-shrink-0" aria-hidden="true" />
              )}
            </motion.div>

            {/* Formulario */}
            <form onSubmit={handleLogin} className="space-y-4">
              {fields.map(({ key, label, icon: Icon, type, ph, max }, idx) => (
                <motion.div
                  key={key}
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.3, delay: 0.28 + idx * 0.05 }}
                >
                  <label htmlFor={key} className="block text-xs font-medium mb-1.5" style={{ color: "var(--text-secondary)" }}>
                    {label}
                  </label>
                  <div className="relative">
                    <Icon
                      className="absolute left-3.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 transition-colors duration-200"
                      style={{ color: inputIconColor(key) }}
                      aria-hidden="true"
                    />
                    <input
                      id={key}
                      type={type}
                      placeholder={ph}
                      maxLength={max}
                      value={(form as Record<string, string>)[key]}
                      onChange={e => set(key, e.target.value)}
                      onFocus={() => setFocused(key)}
                      onBlur={() => setFocused(null)}
                      required
                      className="w-full pl-10 pr-4 py-3 rounded-lg text-sm outline-none transition-all duration-200"
                      style={{
                        background: "var(--bg-input)",
                        border: `1px solid ${inputBorder(key)}`,
                        boxShadow: inputShadow(key),
                        color: "var(--text-primary)",
                        caretColor: "var(--blue-lg)",
                      }}
                    />
                  </div>
                </motion.div>
              ))}

              {/* Contraseña */}
              <motion.div
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.3, delay: 0.38 }}
              >
                <label htmlFor="password" className="block text-xs font-medium mb-1.5" style={{ color: "var(--text-secondary)" }}>
                  Contraseña
                </label>
                <div className="relative">
                  <Lock
                    className="absolute left-3.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 transition-colors duration-200"
                    style={{ color: inputIconColor("password") }}
                    aria-hidden="true"
                  />
                  <input
                    id="password"
                    type={showPass ? "text" : "password"}
                    placeholder="••••••••"
                    value={form.password}
                    onChange={e => set("password", e.target.value)}
                    onFocus={() => setFocused("password")}
                    onBlur={() => setFocused(null)}
                    required
                    className="w-full pl-10 pr-11 py-3 rounded-lg text-sm outline-none transition-all duration-200"
                    style={{
                      background: "var(--bg-input)",
                      border: `1px solid ${inputBorder("password")}`,
                      boxShadow: inputShadow("password"),
                      color: "var(--text-primary)",
                      caretColor: "var(--blue-lg)",
                    }}
                  />
                  <button
                    type="button"
                    onClick={() => setShowPass(s => !s)}
                    className="absolute right-3.5 top-1/2 -translate-y-1/2 transition-colors duration-150"
                    style={{ color: "var(--text-muted)" }}
                    aria-label={showPass ? "Ocultar contraseña" : "Mostrar contraseña"}
                    aria-pressed={showPass}
                  >
                    {showPass ? <EyeOff className="w-3.5 h-3.5" aria-hidden="true" /> : <Eye className="w-3.5 h-3.5" aria-hidden="true" />}
                  </button>
                </div>
              </motion.div>

              {/* Error */}
              <AnimatePresence>
                {error && (
                  <motion.div
                    role="alert"
                    aria-live="assertive"
                    className="flex items-center gap-2 rounded-lg px-3.5 py-2.5 text-[12px] font-medium text-red-500"
                    style={{ background: "rgba(220,38,38,0.07)", border: "1px solid rgba(220,38,38,0.2)" }}
                    initial={{ opacity: 0, height: 0, marginTop: 0 }}
                    animate={{ opacity: 1, height: "auto", marginTop: 12, transition: { duration: 0.22 } }}
                    exit={{ opacity: 0, height: 0, marginTop: 0, transition: { duration: 0.16 } }}
                  >
                    <AlertTriangle className="w-3.5 h-3.5 flex-shrink-0" aria-hidden="true" />
                    {error}
                  </motion.div>
                )}
              </AnimatePresence>

              {/* Submit */}
              <motion.button
                type="submit"
                disabled={loading}
                className="relative w-full mt-2 py-3.5 rounded-lg text-sm font-semibold overflow-hidden disabled:opacity-70"
                style={{
                  background: "var(--btn-primary-bg)",
                  color: "var(--btn-primary-text)",
                  border: "1px solid var(--btn-primary-bg)",
                }}
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.3, delay: 0.46 }}
                whileTap={{ scale: 0.98 }}
              >
                <span className="relative flex items-center justify-center gap-2">
                  {loading
                    ? <><Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Verificando credenciales…</>
                    : <><ShieldCheck className="w-4 h-4" aria-hidden="true" /> Ingresar a Factum</>
                  }
                </span>
              </motion.button>
            </form>
          </div>
        </div>
      </div>
    </div>
  );
}
