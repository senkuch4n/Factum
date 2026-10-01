"use client";

import { useState, useEffect, useCallback, useRef, useMemo } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Loader2, AlertCircle, Plus, Play, ChevronRight, ArrowLeft, Smartphone, X, HelpCircle, Sun, Moon } from "lucide-react";
import { api, type DeviceInput } from "@/lib/api";
import { agent } from "@/lib/agent";
import type { Device, AgentEvent, Case, CaseFormData } from "@/types";
import { useAuth } from "@/hooks/useAuth";
import { useAgentConnection } from "@/hooks/useAgentConnection";
import { useFileManager } from "@/hooks/useFileManager";
import { useRecording } from "@/hooks/useRecording";
import { useAirplayShotSession } from "@/hooks/useAirplayShotSession";
import { AgentChip } from "@/components/dashboard/AgentChip";
import { DashboardStats } from "@/components/dashboard/DashboardStats";
import { ResumeDeviceModal } from "@/components/dashboard/ResumeDeviceModal";
import { SoporteModal } from "@/components/dashboard/SoporteModal";
import { FaroIcon } from "@/components/FaroIcon";
import { GenerateStep } from "@/components/dashboard/GenerateStep";
import { StepIndicator } from "@/components/StepIndicator";
import { GuideModal } from "@/components/GuideModal";
import { DeviceConnect } from "@/components/DeviceConnect";
import { CaptureStep } from "@/components/CaptureStep";
import { ResultStep } from "@/components/ResultStep";
import { CaseHistory } from "@/components/CaseHistory";
import { CaseFormStep } from "@/components/CaseFormStep";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { FloatingDock } from "@/components/ui/floating-dock";
import { ConfirmDialog } from "@/components/ui/alert-dialog";
import { Tip } from "@/components/ui/tooltip";
import { useTheme } from "@/lib/theme";
import { EASE, slideDir } from "@/constants/animations";

const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:8080";

const STEPS = [
  { id: 1, label: "Dispositivo", sublabel: "Seleccionando dispositivo" },
  { id: 2, label: "Expediente", sublabel: "Llenando datos" },
  { id: 3, label: "Factum", sublabel: "Capturando" },
  { id: 4, label: "Generar", sublabel: "Creando informe" },
  { id: 5, label: "Listo", sublabel: "Resumiendo" },
];

const EMPTY_FORM: CaseFormData = {
  NroReferencia: "", NombreDenunciante: "", DNIDenunciante: "", Observaciones: "", imeiOverride: "",
};

export default function Dashboard() {
  const { isDark, toggle: toggleTheme } = useTheme();

  // ── Navigation ──────────────────────────────────────────────────
  const [mode, setMode] = useState<"history" | "wizard">("history");
  const [step, setStep] = useState(1);
  const [dir, setDir] = useState(1);
  const [guideOpen, setGuideOpen] = useState(false);

  // ── Device + case ────────────────────────────────────────────────
  const [selDevice, setSelDevice] = useState<Device | null>(null);
  const [caseForm, setCaseForm] = useState<CaseFormData>(EMPTY_FORM);
  const [caseErrors, setCaseErr] = useState<Record<string, string>>({});
  const [currentCase, setCase] = useState<Case | null>(null);
  const [result, setResult] = useState<{ zip: string; pdf: string; password: string; hash: string } | null>(null);
  const [isResuming, setIsResuming] = useState(false);
  const [resumePending, setResumePending] = useState<Case | null>(null);
  const [resumeConnected, setResumeConnected] = useState(false);

  // ── UI ───────────────────────────────────────────────────────────
  const [statusMsg, setStatus] = useState("");
  const [globalError, setGlobal] = useState("");
  const [dashLoading, setDashLoad] = useState<Record<string, boolean>>({});
  function setLoad(key: string, val: boolean) { setDashLoad(l => ({ ...l, [key]: val })); }
  const [reportModal, setReportModal] = useState(false);

  // ── Refs for WS event handler ────────────────────────────────────
  const selDeviceRef = useRef<Device | null>(null);
  const stepRef = useRef(step);
  useEffect(() => { selDeviceRef.current = selDevice; }, [selDevice]);
  useEffect(() => { stepRef.current = step; }, [step]);

  // ── Domain hooks ─────────────────────────────────────────────────
  const { user, historyCases, historyLoading, loadHistory, handleLogout } = useAuth();

  const {
    files, loading: fileLoading, videoVariants, pendingVariantFiles, pendingBlobs,
    addFile, addVideoVariant, markPendingVariant,
    removeFile, handlePhotoBlob, handleUploadAndContinue, clearFiles,
  } = useFileManager();

  const {
    isRecording, disconnectedDuringRecord, deviceOffline,
    iosModePicker, iosRecordMode, airplayReceiverName, androidWithMic,
    isRecordingRef, loading: recLoading,
    setRecording, setDiscoRec, setDeviceOffline, setAirplayName, setIosModePicker, setAndroidWithMic,
    handleToggleRecord, handleSelectIosMode, resetRecording,
  } = useRecording(addFile, markPendingVariant);

  const {
    active: shotActive, connected: shotConnected, receiverName: shotReceiverName,
    marksCount: shotMarksCount, loading: shotLoading,
    handleStart: handleShotStart, handleMark: handleShotMark, handleStop: handleShotStop,
    resetShotSession, handleShotConnected, handleShotTimeout,
  } = useAirplayShotSession();

  const loading = { ...fileLoading, ...recLoading, ...dashLoading, ...shotLoading };

  // ── WS event dispatcher ──────────────────────────────────────────
  const handleWsEvent = useCallback((event: AgentEvent) => {
    if (event.type === "devices_changed") {
      const newDevices = (event.data as { devices: Device[] }).devices || [];
      const cur = selDeviceRef.current;
      if (cur && stepRef.current === 3) {
        const stillConnected = newDevices.some(d => d.serial === cur.serial);
        if (!stillConnected) {
          if (isRecordingRef.current) { setRecording(false); setDiscoRec(true); }
          else { setDeviceOffline(true); }
        } else {
          setDeviceOffline(false);
        }
      }
    }
    if (event.type === "recording_started") setRecording(true);
    if (event.type === "recording_stopped") {
      setRecording(false); setAirplayName(null);
      const d = event.data as { filename: string; platform?: string };
      if (d.filename) { addFile(d.filename); if (d.platform === "ios") markPendingVariant(d.filename); }
    }
    if (event.type === "airplay_receiver_ready") {
      setAirplayName((event.data as { receiver_name: string }).receiver_name);
    }
    if (event.type === "video_variant_ready") {
      addVideoVariant(event.data as { original_filename: string; label: string; filename: string; url: string });
    }
    if (event.type === "photo_taken") {
      const f = (event.data as { filename: string }).filename;
      if (f) addFile(f);
    }
    if (event.type === "screenshot_taken") {
      setAirplayName(null);
      const f = (event.data as { filename: string }).filename;
      if (f && stepRef.current !== 2) addFile(f);
    }
    if (event.type === "airplay_shot_connected") handleShotConnected();
    if (event.type === "airplay_shot_timeout") handleShotTimeout(msg => setGlobal(msg));
  }, [addFile, addVideoVariant, markPendingVariant, setRecording, setDiscoRec, setDeviceOffline, setAirplayName, isRecordingRef, handleShotConnected, handleShotTimeout]);

  const { agentOnline, devices, loadingDev, refreshDevices } = useAgentConnection(handleWsEvent);

  // ── Auto-detect resume device ─────────────────────────────────────
  useEffect(() => {
    if (!resumePending) return;
    if (devices.some(d => d.serial === resumePending.device.serial)) setResumeConnected(true);
  }, [devices, resumePending]);

  // ── Auditoría: heartbeat de "empezó a capturar" para este caso ─────
  const startupReportedFor = useRef<string | null>(null);
  useEffect(() => {
    if (step !== 3 || !currentCase) return;
    if (startupReportedFor.current === currentCase.id) return;
    startupReportedFor.current = currentCase.id;
    api.reportAgentEvent("startup", currentCase.id);
  }, [step, currentCase]);

  // ── Navigation ───────────────────────────────────────────────────
  const go = useCallback((n: number) => {
    setDir(n > step ? 1 : -1);
    setStep(n); setStatus(""); setGlobal("");
    if (n !== 3) setDeviceOffline(false);
  }, [step, setDeviceOffline]);

  // ── Actions ──────────────────────────────────────────────────────
  function validateCaseForm() {
    const err: Record<string, string> = {};
    if (!caseForm.NroReferencia.trim()) err.NroReferencia = "Ingresá el número de expediente";
    if (!caseForm.NombreDenunciante.trim()) err.NombreDenunciante = "Ingresá el nombre del denunciante";
    if (!caseForm.DNIDenunciante.trim()) err.DNIDenunciante = "Ingresá el DNI del denunciante";
    setCaseErr(err);
    return Object.keys(err).length === 0;
  }

  async function handleCreateCase() {
    if (!validateCaseForm() || !selDevice) return;
    setLoad("case", true); setGlobal("");
    try {
      const cas = await api.createCase({
        nro_referencia: caseForm.NroReferencia,
        nombre_denunciante: caseForm.NombreDenunciante,
        dni_denunciante: caseForm.DNIDenunciante,
        observaciones: caseForm.Observaciones,
        device: {
          serial: selDevice.serial, manufacturer: selDevice.manufacturer,
          model: selDevice.model, android_version: selDevice.android_version,
          imei: caseForm.imeiOverride.trim() || selDevice.imei,
          platform: selDevice.platform ?? "android",
          os_version: selDevice.platform === "ios"
            ? (selDevice.ios_version ?? String(selDevice.android_version))
            : `Android ${selDevice.android_version}`,
        } as DeviceInput,
      });
      setCase(cas); go(3);
    } catch (e) { setGlobal(e instanceof Error ? e.message : "Error al crear el caso"); }
    finally { setLoad("case", false); }
  }

  async function handleScreenshot() {
    if (!selDevice) return;
    setLoad("screenshot", true);
    try {
      const { filename } = await agent.takeScreenshot(selDevice.serial, selDevice.platform ?? "android");
      addFile(filename);
      api.reportAgentEvent("screenshot", currentCase?.id);
    } catch (e) { setGlobal(e instanceof Error ? e.message : "Error al capturar pantalla"); }
    finally { setLoad("screenshot", false); setAirplayName(null); }
  }

  async function handleStartAirplayShot() {
    if (!selDevice) return;
    await handleShotStart(selDevice.serial, msg => setGlobal(msg));
  }

  async function handleMarkAirplayShot() {
    if (!selDevice) return;
    await handleShotMark(selDevice.serial, msg => setGlobal(msg));
  }

  async function handleStopAirplayShot() {
    if (!selDevice) return;
    await handleShotStop(selDevice.serial, msg => setGlobal(msg));
  }

  async function handleGenerate() {
    if (!currentCase) return;
    setLoad("generate", true); setGlobal("");
    try {
      const res = await api.generateCase(currentCase.id);
      setResult({ zip: res.files.zip, pdf: res.files.pdf, password: res.password, hash: res.zip_hash });
      go(5);
    } catch (e) { setGlobal(e instanceof Error ? e.message : "Error al generar el caso"); }
    finally { setLoad("generate", false); }
  }

  function resetWizard(opts: { keepMode?: boolean } = {}) {
    clearFiles(); resetRecording(); resetShotSession();
    setStep(1); setDir(1);
    setSelDevice(null); setCase(null); setResult(null);
    setCaseForm(EMPTY_FORM); setStatus(""); setGlobal("");
    setIsResuming(false);
    if (!opts.keepMode) { loadHistory(); setMode("history"); }
  }

  // Salir del wizard: en pasos con evidencia en curso (2–4) pedimos confirmación,
  // porque los archivos capturados no se guardan en el servidor.
  const [exitConfirm, setExitConfirm] = useState(false);
  function attemptExitWizard() {
    if (step >= 2 && step <= 4) setExitConfirm(true);
    else resetWizard();
  }

  function startWizard() { resetWizard({ keepMode: true }); setMode("wizard"); }

  function proceedResume(cas: Case) {
    clearFiles(); resetRecording(); resetShotSession();
    setCase(cas); setResult(null);
    setSelDevice({
      serial: cas.device.serial, state: "device",
      manufacturer: cas.device.manufacturer, model: cas.device.model,
      android_version: cas.device.android_version, imei: cas.device.imei,
      name: cas.device.model, operator: "",
      platform: (cas.device.platform as "android" | "ios") ?? "android",
      ios_version: undefined,
    });
    setDir(1); setStep(3); setStatus(""); setGlobal("");
    setIsResuming(true); setMode("wizard");
    setResumePending(null); setResumeConnected(false);
  }

  function handleResume(cas: Case) {
    if (!devices.some(d => d.serial === cas.device.serial)) {
      setResumePending(cas); setResumeConnected(false); return;
    }
    proceedResume(cas);
  }

  const greeting = useMemo(() => {
    const h = new Date().getHours();
    if (h < 12) return "Buenos días";
    if (h < 19) return "Buenas tardes";
    return "Buenas noches";
  }, []);

  const formattedDate = useMemo(() => new Date().toLocaleDateString("es-AR", {
    weekday: "long", day: "numeric", month: "long", year: "numeric",
  }), []);

  const draftCases = useMemo(() => historyCases.filter(c => c.status === "draft"), [historyCases]);

  // ── Render ───────────────────────────────────────────────────────
  return (
    <div className="flex flex-col h-screen overflow-hidden" style={{ background: "var(--bg-base)" }}>

      <AnimatePresence>
        {resumePending && (
          <ResumeDeviceModal
            cas={resumePending}
            deviceConnected={resumeConnected}
            onConfirm={() => proceedResume(resumePending)}
            onCancel={() => { setResumePending(null); setResumeConnected(false); }}
          />
        )}
        {guideOpen && (
          <GuideModal
            onClose={() => setGuideOpen(false)}
            onSupport={() => { setGuideOpen(false); setReportModal(true); }}
          />
        )}
      </AnimatePresence>

      <SoporteModal open={reportModal} user={user} onClose={() => setReportModal(false)} />

      <FloatingDock
        items={[
          {
            title: "Guía de uso",
            icon: <HelpCircle className="h-full w-full" strokeWidth={1.75} />,
            onClick: () => setGuideOpen(true),
            active: guideOpen,
          },
          {
            title: "¿Bug o idea? Contanos",
            icon: <FaroIcon className="h-full w-full" />,
            onClick: () => setReportModal(true),
            active: reportModal,
          },
          {
            title: isDark ? "Modo claro" : "Modo oscuro",
            icon: isDark
              ? <Sun className="h-full w-full text-amber-400" strokeWidth={1.75} />
              : <Moon className="h-full w-full" strokeWidth={1.75} />,
            onClick: toggleTheme,
          },
        ]}
      />

      <ConfirmDialog
        open={exitConfirm}
        onOpenChange={setExitConfirm}
        onConfirm={() => resetWizard()}
        title="¿Salir de la inspección?"
        description="Los archivos capturados no se guardan en el servidor. Si salís ahora, se pierden."
        confirmLabel="Salir sin guardar"
        cancelLabel="Seguir acá"
      />

      <AppNavbar
        user={user}
        onLogout={handleLogout}
        center={
          <div className="flex items-center justify-center gap-1.5 min-w-0">
            {mode === "wizard" && (
              <Tip label="Volver a Inspecciones">
                <button
                  onClick={attemptExitWizard}
                  aria-label="Volver a Inspecciones"
                  className="flex items-center justify-center w-7 h-7 -ml-1 mr-0.5 rounded-fx-md flex-shrink-0 text-fx-text-3 hover:text-fx-text hover:bg-fx-surface-3 transition-colors duration-fx-fast ease-fx fx-focus-ring"
                >
                  <ArrowLeft className="w-3.5 h-3.5" />
                </button>
              </Tip>
            )}
            <nav aria-label="Ruta" className="flex items-center gap-1.5 text-sm min-w-0">
              <span
                aria-hidden="true"
                className={`inline-block w-[5px] h-[5px] flex-shrink-0 ${mode === "history" ? "bg-fx-accent" : "bg-fx-text-3"}`}
              />
              <span className={`truncate font-medium ${mode === "history" ? "text-fx-text" : "text-fx-text-3"}`}>
                Inspecciones
              </span>
              {mode === "wizard" && (
                <>
                  <ChevronRight aria-hidden="true" className="w-3.5 h-3.5 flex-shrink-0 text-fx-text-3" />
                  <span className="font-medium truncate text-fx-text">
                    {STEPS[step - 1]?.label}
                  </span>
                </>
              )}
            </nav>
          </div>
        }
        actions={
          <>
            <AgentChip online={agentOnline} device={selDevice} recording={isRecording} />
            <AnimatePresence>
              {mode === "history" && (
                <motion.button
                  onClick={startWizard}
                  className="inline-flex items-center justify-center w-8 h-8 flex-shrink-0 rounded-fx-md bg-fx-accent text-fx-on-accent hover:bg-fx-accent-hover transition-colors duration-fx-fast ease-fx fx-focus-ring"
                  title="Nueva inspección"
                  aria-label="Nueva inspección"
                  initial={{ opacity: 0, scale: 0.88 }}
                  animate={{ opacity: 1, scale: 1 }}
                  exit={{ opacity: 0, scale: 0.88 }}
                  transition={{ duration: 0.15 }}
                  whileTap={{ scale: 0.97 }}
                >
                  <Plus className="w-4 h-4" />
                </motion.button>
              )}
            </AnimatePresence>
            {/* "¿Bug o idea?", tema y guía viven en el FloatingDock inferior. */}
          </>
        }
      />

      {/* pb-28: reserva el alto del FloatingDock fijo para que no tape las últimas tarjetas */}
      <div className="flex-1 overflow-y-auto pb-28">

        <AnimatePresence mode="wait">
          {mode === "history" && (
            <motion.div
              key="history"
              className="p-6"
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
              transition={{ duration: 0.18 }}
            >
              <div className="max-w-5xl mx-auto space-y-8">
                {user && (
                  <motion.div
                    className="mb-1 flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4"
                    initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.3, ease: EASE }}
                  >
                    <div>
                      <h1 className="hero-title text-3xl sm:text-4xl" style={{ color: "var(--text-primary)" }}>
                        {greeting}, <span style={{ color: "var(--blue-lg)" }}>{user.name}</span>
                      </h1>
                      <p className="text-sm mt-2 capitalize" style={{ color: "var(--text-muted)" }}>
                        {formattedDate}
                      </p>
                    </div>
                    <motion.button
                      onClick={startWizard}
                      className="btn-primary flex-shrink-0"
                      whileHover={{ scale: 1.02 }}
                      whileTap={{ scale: 0.98 }}
                    >
                      <Plus className="w-4 h-4" aria-hidden="true" /> Nueva inspección
                    </motion.button>
                  </motion.div>
                )}

                <AnimatePresence>
                  {globalError && (
                    <motion.div
                      className="flex items-start gap-3 rounded-md px-4 py-3 text-sm border border-red-500/20 bg-red-500/[0.07] text-red-600 dark:text-red-400"
                      initial={{ opacity: 0, y: -10, height: 0 }} animate={{ opacity: 1, y: 0, height: "auto" }} exit={{ opacity: 0, height: 0 }}
                      role="alert"
                    >
                      <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" aria-hidden="true" />
                      <span className="flex-1">{globalError}</span>
                      <button onClick={() => setGlobal("")} aria-label="Cerrar"><X className="w-3.5 h-3.5" aria-hidden="true" /></button>
                    </motion.div>
                  )}
                </AnimatePresence>

                {historyCases.length > 0 && <DashboardStats cases={historyCases} />}

                {draftCases.length > 0 && (
                  <motion.div
                    initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.28, delay: 0.15, ease: EASE }}
                  >
                    <p className="section-label mb-2.5 flex items-center gap-1.5">
                      <Play className="w-3 h-3" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
                      Pendientes de retomar
                    </p>
                    <div className="flex gap-2.5 overflow-x-auto pb-1 -mx-1 px-1">
                      {draftCases.map((cas, i) => (
                        <motion.button
                          key={cas.id}
                          onClick={() => handleResume(cas)}
                          className="flex items-center gap-3 flex-shrink-0 rounded-lg px-3.5 py-2.5 text-left transition-colors"
                          style={{ background: "rgba(13,148,136,0.06)", border: "1px solid var(--border-accent)" }}
                          initial={{ opacity: 0, x: 8 }} animate={{ opacity: 1, x: 0 }}
                          transition={{ duration: 0.25, delay: 0.18 + i * 0.04 }}
                          whileHover={{ borderColor: "var(--blue-lg)" }}
                          whileTap={{ scale: 0.98 }}
                          aria-label={`Retomar inspección ${cas.nro_referencia} — ${cas.device.manufacturer} ${cas.device.model}`}
                        >
                          <div
                            className="w-8 h-8 rounded-md flex items-center justify-center flex-shrink-0"
                            style={{ background: "rgba(13,148,136,0.12)" }}
                          >
                            <Play className="w-3.5 h-3.5" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
                          </div>
                          <div className="min-w-0">
                            <p className="text-xs font-semibold truncate" style={{ color: "var(--text-primary)" }}>{cas.nro_referencia}</p>
                            <p className="text-[11px] truncate flex items-center gap-1" style={{ color: "var(--text-muted)" }}>
                              <Smartphone className="w-2.5 h-2.5 flex-shrink-0" aria-hidden="true" />
                              {cas.device.manufacturer} {cas.device.model}
                            </p>
                          </div>
                        </motion.button>
                      ))}
                    </div>
                  </motion.div>
                )}

                <AnimatePresence>
                  {statusMsg && (
                    <motion.div
                      className="flex items-center gap-2.5 rounded-md px-4 py-2.5 text-sm border border-teal-500/20 bg-teal-500/[0.07] text-teal-600 dark:text-teal-300"
                      initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
                      role="status"
                      aria-live="polite"
                    >
                      <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" />{statusMsg}
                    </motion.div>
                  )}
                </AnimatePresence>

                <CaseHistory
                  cases={historyCases}
                  loading={historyLoading}
                  onNewCase={startWizard}
                  onRefresh={loadHistory}
                  onResume={handleResume}
                />
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        <AnimatePresence mode="wait">
          {mode === "wizard" && (
            <motion.div
              key="wizard"
              className="p-6"
              initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
              transition={{ duration: 0.18 }}
            >
              <div className="max-w-6xl mx-auto">
                <div className="flex items-center justify-end mb-4">
                  <span className="text-xs" style={{ color: "var(--text-muted)" }}>
                    Paso {step} de {STEPS.length}
                  </span>
                </div>

                <div className="flex gap-8 items-start">
                  {/* Riel vertical de pasos — reemplaza el indicador horizontal y ocupa el
                      espacio que dejaba libre la sidebar. Los pasos ya completados son
                      clickeables para volver atrás, salvo que eso implique duplicar el caso
                      ya creado (ver minJumpable). */}
                  <div className="hidden md:block w-52 flex-shrink-0 sticky top-6">
                    <StepIndicator
                      steps={STEPS}
                      current={step}
                      minJumpable={currentCase ? 3 : 1}
                      onSelect={go}
                    />
                  </div>

                  <div className="flex-1 min-w-0 space-y-7">
                    <AnimatePresence>
                      {globalError && (
                        <motion.div
                          className="flex items-start gap-3 rounded-md px-4 py-3 text-sm border border-red-500/20 bg-red-500/[0.07] text-red-600 dark:text-red-400"
                          initial={{ opacity: 0, y: -10, height: 0 }} animate={{ opacity: 1, y: 0, height: "auto" }} exit={{ opacity: 0, height: 0 }}
                          role="alert"
                        >
                          <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" aria-hidden="true" />
                          <span className="flex-1">{globalError}</span>
                          <button onClick={() => setGlobal("")} aria-label="Cerrar"><X className="w-3.5 h-3.5" aria-hidden="true" /></button>
                        </motion.div>
                      )}
                    </AnimatePresence>

                    <AnimatePresence>
                      {statusMsg && (
                        <motion.div
                          className="flex items-center gap-2.5 rounded-md px-4 py-2.5 text-sm border border-teal-500/20 bg-teal-500/[0.07] text-teal-600 dark:text-teal-300"
                          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
                        >
                          <Loader2 className="w-3.5 h-3.5 animate-spin" />{statusMsg}
                        </motion.div>
                      )}
                    </AnimatePresence>

                    <AnimatePresence>
                      {isResuming && step === 3 && currentCase && (
                        <motion.div
                          className="rounded-md px-4 py-3.5 flex items-start gap-3"
                          style={{ background: "rgba(13,148,136,0.06)", border: "1px solid var(--border-accent)" }}
                          initial={{ opacity: 0, y: -10, height: 0 }} animate={{ opacity: 1, y: 0, height: "auto" }}
                          exit={{ opacity: 0, y: -6, height: 0 }} transition={{ duration: 0.24, ease: EASE }}
                          role="status"
                        >
                          <div
                            className="w-7 h-7 rounded-lg flex items-center justify-center flex-shrink-0 mt-0.5"
                            style={{ background: "rgba(13,148,136,0.12)", border: "1px solid var(--border-accent)" }}
                          >
                            <Play className="w-3.5 h-3.5" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
                          </div>
                          <div className="min-w-0">
                            <p className="text-sm font-semibold" style={{ color: "var(--blue-lg)" }}>
                              Retomando inspección · {currentCase.nro_referencia}
                            </p>
                            <p className="text-xs mt-0.5 leading-relaxed" style={{ color: "var(--text-secondary)" }}>
                              {currentCase.device.manufacturer} {currentCase.device.model} — los archivos de evidencia no se almacenan en el servidor.
                              Reconectá el dispositivo para capturar nueva evidencia y volver a generar el informe.
                            </p>
                          </div>
                        </motion.div>
                      )}
                    </AnimatePresence>

                    <AnimatePresence mode="wait" custom={dir}>
                      <motion.div
                        key={step}
                        custom={dir}
                        variants={slideDir}
                        initial="initial"
                        animate="animate"
                        exit="exit"
                        transition={{ duration: 0.28, ease: EASE }}
                      >
                        {/* Paso 3: `!overflow-visible` porque `.card` trae overflow:hidden,
                            que rompe el position:sticky de las columnas (teléfono + controles).
                            Los demás pasos conservan el clip original. */}
                        <div className={`card p-6 sm:p-8 lg:p-9${step === 3 ? " !overflow-visible" : ""}`}>
                          {step === 1 && (
                            <DeviceConnect
                              devices={devices}
                              agentOnline={agentOnline}
                              loading={loadingDev}
                              onSelect={d => { setSelDevice(d); go(2); }}
                              onRefresh={refreshDevices}
                              onOpenGuide={() => setGuideOpen(true)}
                            />
                          )}

                          {step === 2 && selDevice && (
                            <CaseFormStep
                              device={selDevice}
                              form={caseForm}
                              errors={caseErrors}
                              loading={loading.case}
                              onChange={f => setCaseForm(f)}
                              onClearError={key => setCaseErr(er => ({ ...er, [key]: "" }))}
                              onBack={() => go(1)}
                              onSubmit={handleCreateCase}
                            />
                          )}

                          {step === 3 && selDevice && (
                            <CaptureStep
                              isRecording={isRecording}
                              androidVersion={selDevice.android_version}
                              platform={selDevice.platform ?? "android"}
                              deviceSerial={selDevice.serial}
                              files={files}
                              onScreenshot={handleScreenshot}
                              onToggleRecord={() => {
                                api.reportAgentEvent(isRecording ? "capture_stop" : "capture_start", currentCase?.id);
                                handleToggleRecord(selDevice, setGlobal);
                              }}
                              iosModePicker={iosModePicker}
                              iosRecordMode={iosRecordMode}
                              onSelectIosMode={m => {
                                api.reportAgentEvent("capture_start", currentCase?.id);
                                handleSelectIosMode(m, selDevice, setGlobal);
                              }}
                              onCancelIosMode={() => setIosModePicker(false)}
                              onPhotoFuncionario={(blob, fn) => { handlePhotoBlob(blob, fn); api.reportAgentEvent("webcam", currentCase?.id); }}
                              onPhotoDenunciante={(blob, fn) => { handlePhotoBlob(blob, fn); api.reportAgentEvent("webcam", currentCase?.id); }}
                              denuncianteNombre={currentCase?.nombre_denunciante ?? caseForm.NombreDenunciante}
                              denuncianteDni={currentCase?.dni_denunciante ?? caseForm.DNIDenunciante}
                              onUploadAndContinue={() =>
                                handleUploadAndContinue(currentCase!, () => go(4), setGlobal, setStatus)
                              }
                              loading={loading}
                              deviceOffline={deviceOffline}
                              disconnectedDuringRecord={disconnectedDuringRecord}
                              onRetryRecording={() => {
                                setDiscoRec(false);
                                api.reportAgentEvent("capture_start", currentCase?.id);
                                handleToggleRecord(selDevice, setGlobal);
                              }}
                              onDismissDisconnect={() => setDiscoRec(false)}
                              onRemoveFile={removeFile}
                              onAttachLocalFile={(blob, filename) => handlePhotoBlob(blob, filename)}
                              onDeviceFilesAdded={files => files.forEach(f => addFile(f.filename, f.sourcePath))}
                              videoVariants={videoVariants}
                              pendingVariantFiles={pendingVariantFiles}
                              airplayReceiverName={airplayReceiverName}
                              airplayShotActive={shotActive}
                              airplayShotConnected={shotConnected}
                              airplayShotReceiverName={shotReceiverName}
                              airplayShotMarksCount={shotMarksCount}
                              onStartAirplayShot={handleStartAirplayShot}
                              onMarkAirplayShot={handleMarkAirplayShot}
                              onStopAirplayShot={handleStopAirplayShot}
                              androidWithMic={androidWithMic}
                              onToggleAndroidWithMic={setAndroidWithMic}
                            />
                          )}

                          {step === 4 && currentCase && (
                            <GenerateStep
                              currentCase={currentCase}
                              files={files}
                              loading={!!loading.generate}
                              onBack={() => go(3)}
                              onGenerate={handleGenerate}
                            />
                          )}

                          {step === 5 && result && currentCase && (
                            <ResultStep
                              caseNumber={currentCase.nro_referencia}
                              zipFile={result.zip}
                              pdfFile={result.pdf}
                              password={result.password}
                              hash={result.hash}
                              caseId={currentCase.id}
                              backendURL={BACKEND_URL}
                              onNewCase={() => resetWizard()}
                            />
                          )}
                        </div>
                      </motion.div>
                    </AnimatePresence>
                  </div>
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>

      </div>
    </div>
  );
}
