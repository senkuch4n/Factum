"use client";

import { useState, useEffect, useCallback, useRef, useMemo } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Loader2, AlertCircle, Plus, Play, ChevronRight, ArrowLeft, Smartphone, X, HelpCircle, LifeBuoy } from "lucide-react";
import { Button } from "primereact/button";
import { api, ApiError, type DeviceInput } from "@/lib/api";
import { agent } from "@/lib/agent";
import type { Device, AgentEvent, Case, CaseFormData, ProfileFormData } from "@/types";
import {
  CASE_MESSAGES, EMPTY_CASE_FORM, PROFILE_REQUIRED_KEYS, PROFILE_MESSAGES,
  caseToForm, formToCaseRequest, isPhoneLike, prefillFromLastCase, profileErrorKey,
  todayIso, validateCaseForm, validateProfile,
} from "@/lib/pericial";
import { useExpertProfile, profileToForm, sameProfile } from "@/hooks/useExpertProfile";
import { usePublicConfig } from "@/hooks/usePublicConfig";
import { ReportStep } from "@/components/ReportStep";
import { useAuth } from "@/hooks/useAuth";
import { useAgentConnection } from "@/hooks/useAgentConnection";
import { useFileManager } from "@/hooks/useFileManager";
import { useRecording } from "@/hooks/useRecording";
import { useAirplayShotSession } from "@/hooks/useAirplayShotSession";
import { AgentChip } from "@/components/dashboard/AgentChip";
import { DashboardStats } from "@/components/dashboard/DashboardStats";
import { ResumeDeviceModal } from "@/components/dashboard/ResumeDeviceModal";
import { SoporteModal } from "@/components/dashboard/SoporteModal";
import { GenerateStep } from "@/components/dashboard/GenerateStep";
import { StepIndicator } from "@/components/StepIndicator";
import { GuideModal } from "@/components/GuideModal";
import { DeviceConnect } from "@/components/DeviceConnect";
import { CaptureStep } from "@/components/CaptureStep";
import { ResultStep } from "@/components/ResultStep";
import { CaseHistory } from "@/components/CaseHistory";
import { CaseFormStep } from "@/components/CaseFormStep";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { ConfirmDialog } from "@/components/ui/alert-dialog";
import { FxTip } from "@/components/overlay/FxTip";
import { FxBanner } from "@/components/feedback/FxBanner";
import { EASE, slideDir } from "@/constants/animations";

const BACKEND_URL = process.env.NEXT_PUBLIC_BACKEND_URL || "http://localhost:8080";

const STEPS = [
  { id: 1, label: "Dispositivo", sublabel: "Seleccionando dispositivo" },
  { id: 2, label: "Causa", sublabel: "Datos de la causa" },
  { id: 3, label: "Captura", sublabel: "Capturando" },
  { id: 4, label: "Informe", sublabel: "Redactando" },
  { id: 5, label: "Generar", sublabel: "Creando informe" },
  { id: 6, label: "Listo", sublabel: "Resumiendo" },
];

const DEFAULT_TIPO_DISPOSITIVO = "Teléfono celular";

/* Botón de ícono de la navbar: el mismo look que ThemeSwitch. */
const NAV_ICON_BUTTON =
  "inline-flex items-center justify-center w-8 h-8 shrink-0 rounded-fx-md text-fx-text-2 hover:text-fx-text hover:bg-fx-surface-3 transition-colors duration-fx-fast ease-fx fx-focus-ring";

/** Formulario de una causa nueva: precarga del último caso pericial + defaults (§8.2). */
function newCaseForm(historyCases: Case[]): CaseFormData {
  return {
    ...EMPTY_CASE_FORM,
    ...prefillFromLastCase(historyCases),
    tipo_dispositivo: DEFAULT_TIPO_DISPOSITIVO,
    fecha_intervencion: todayIso(),
  };
}

export default function Dashboard() {
  // Soporte opcional: sin la integración habilitada no hay botón en la navbar, en la guía ni modal.
  const { supportEnabled } = usePublicConfig();

  // ── Navigation ──────────────────────────────────────────────────
  const [mode, setMode] = useState<"history" | "wizard">("history");
  const [step, setStep] = useState(1);
  const [dir, setDir] = useState(1);
  const [guideOpen, setGuideOpen] = useState(false);

  // ── Device + case ────────────────────────────────────────────────
  const [selDevice, setSelDevice] = useState<Device | null>(null);
  const [caseForm, setCaseForm] = useState<CaseFormData>(EMPTY_CASE_FORM);
  const [caseErrors, setCaseErr] = useState<Record<string, string>>({});
  const [focusErrorsTick, setFocusErrorsTick] = useState(0);
  const [currentCase, setCase] = useState<Case | null>(null);
  const [result, setResult] = useState<{ zip: string; pdf: string; password: string | null; encrypted: boolean; hash: string; reportHash: string } | null>(null);
  // Campo a enfocar al llegar a un paso (enlaces del checklist de "Generar").
  const [focusFieldId, setFocusFieldId] = useState<string | null>(null);
  const [generateMissing, setGenerateMissing] = useState<string[]>([]);
  const [isResuming, setIsResuming] = useState(false);
  const [resumePending, setResumePending] = useState<Case | null>(null);
  const [resumeConnected, setResumeConnected] = useState(false);

  // ── UI ───────────────────────────────────────────────────────────
  const [statusMsg, setStatus] = useState("");
  const [globalError, setGlobal] = useState("");
  const [dashLoading, setDashLoad] = useState<Record<string, boolean>>({});
  function setLoad(key: string, val: boolean) { setDashLoad(l => ({ ...l, [key]: val })); }
  const [reportModal, setReportModal] = useState(false);
  // Soporte abierto desde la guía: al cerrarlo, el foco vuelve al botón "Guía de uso".
  const [supportFromGuide, setSupportFromGuide] = useState(false);
  const guideBtnRef = useRef<HTMLButtonElement>(null);

  // ── Refs for WS event handler ────────────────────────────────────
  const selDeviceRef = useRef<Device | null>(null);
  const stepRef = useRef(step);
  useEffect(() => { selDeviceRef.current = selDevice; }, [selDevice]);
  useEffect(() => { stepRef.current = step; }, [step]);

  // ── Domain hooks ─────────────────────────────────────────────────
  const { user, historyCases, historyLoading, loadHistory, handleLogout } = useAuth();

  // ── Perfil del perito (tarjeta del paso 2) ───────────────────────
  const { profile, loading: profileLoading, save: saveProfile, reload: reloadProfile } = useExpertProfile();
  const [profileForm, setProfileForm] = useState<ProfileFormData>(() => profileToForm(null));
  useEffect(() => { setProfileForm(profileToForm(profile)); }, [profile]);
  const isLegacyCase = !!currentCase && (currentCase.schema_version ?? 0) === 0;

  const {
    files, loading: fileLoading, videoVariants, pendingVariantFiles, pendingBlobs,
    addFile, addVideoVariant, markPendingVariant,
    removeFile, setCaptureRole, handlePhotoBlob, handleUploadAndContinue, clearFiles,
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
    // Volver al paso 2 con el caso ya creado = modo edición con los datos guardados.
    if (n === 2 && currentCase) {
      setCaseErr({});
      setCaseForm(f => ({ ...caseToForm(currentCase), imeiOverride: f.imeiOverride }));
    }
  }, [step, setDeviceOffline, currentCase]);

  /** Va al paso `n` y enfoca `fieldId` cuando el paso monta (checklist de "Generar"). */
  function goToField(n: number, fieldId: string) {
    setFocusFieldId(fieldId);
    go(n);
  }

  // ── Actions ──────────────────────────────────────────────────────
  /** Errores del `missing` del servidor, con los mensajes del paso 2. */
  function missingToErrors(missing: string[]): Record<string, string> {
    const err: Record<string, string> = {};
    missing.forEach(k => {
      if (k === "perfil" || k === "perito") {
        const pe = validateProfile(profileForm);
        if (Object.keys(pe).length) Object.assign(err, pe);
        else err[profileErrorKey("nombre")] = "Revisá tus datos de perito";
      } else if ((PROFILE_REQUIRED_KEYS as readonly string[]).includes(k)) {
        err[profileErrorKey(k as typeof PROFILE_REQUIRED_KEYS[number])] = PROFILE_MESSAGES[k as typeof PROFILE_REQUIRED_KEYS[number]];
      } else if (k in CASE_MESSAGES) {
        err[k] = CASE_MESSAGES[k as keyof typeof CASE_MESSAGES];
      }
    });
    return err;
  }

  /** Paso 2: perfil (si cambió o está incompleto) → crear (POST) o editar (PUT) el caso. */
  async function handleSaveCase() {
    if (!selDevice) return;
    const imeiManual = selDevice.imei === "INGRESAR_MANUALMENTE";
    const localErr = { ...validateProfile(profileForm), ...validateCaseForm(caseForm, { imeiManual }) };
    setCaseErr(localErr);
    if (Object.keys(localErr).length > 0) { setFocusErrorsTick(t => t + 1); return; }

    setLoad("case", true); setGlobal("");
    try {
      if (!profile?.is_complete || !sameProfile(profileForm, profileToForm(profile))) {
        await saveProfile(profileForm);
      }
      const data = formToCaseRequest(caseForm);
      const cas = currentCase
        ? await api.updateCase(currentCase.id, { ...data, imei: imeiManual ? caseForm.imeiOverride.trim() : undefined })
        : await api.createCase({
            ...data,
            device: {
              serial: selDevice.serial, manufacturer: selDevice.manufacturer,
              model: selDevice.model, android_version: selDevice.android_version,
              imei: caseForm.imeiOverride.trim() || selDevice.imei,
              platform: selDevice.platform ?? "android",
              os_version: selDevice.platform === "ios"
                ? (selDevice.ios_version ?? String(selDevice.android_version))
                : `Android ${selDevice.android_version}`,
              name: selDevice.name ?? "",
            } as DeviceInput,
          });
      setCase(cas);
      // Con el IMEI ya guardado en el caso, el equipo deja de pedirlo a mano.
      if (imeiManual && cas.device?.imei && cas.device.imei !== "INGRESAR_MANUALMENTE") {
        setSelDevice(d => (d ? { ...d, imei: cas.device.imei } : d));
      }
      go(3);
    } catch (e) {
      if (e instanceof ApiError && e.missing?.length) {
        const err = missingToErrors(e.missing);
        setCaseErr(err);
        setFocusErrorsTick(t => t + 1);
        if (Object.keys(err).length === 0) setGlobal(e.message);
      } else {
        setGlobal(e instanceof Error ? e.message : "Error al guardar el caso");
      }
    }
    finally { setLoad("case", false); }
  }

  /** Respuesta de `PUT capture-roles`: estado completo de las marcas del caso. */
  function handleCaptureRolesSaved(roles: Case["capture_roles"]) {
    setCase(c => (c ? { ...c, capture_roles: roles } : c));
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
    setGenerateMissing([]);
    try {
      const res = await api.generateCase(currentCase.id);
      setResult({ zip: res.files.zip, pdf: res.files.pdf, password: res.password, encrypted: res.case.zip_encrypted === true, hash: res.zip_hash, reportHash: res.report_hash });
      setCase(res.case);
      go(6);
    } catch (e) {
      if (e instanceof ApiError && e.missing?.length) setGenerateMissing(e.missing);
      setGlobal(e instanceof Error ? e.message : "Error al generar el caso");
    }
    finally { setLoad("generate", false); }
  }

  function resetWizard(opts: { keepMode?: boolean } = {}) {
    clearFiles(); resetRecording(); resetShotSession();
    setStep(1); setDir(1);
    setSelDevice(null); setCase(null); setResult(null);
    setCaseForm(EMPTY_CASE_FORM); setCaseErr({}); setStatus(""); setGlobal("");
    setIsResuming(false); setFocusFieldId(null); setGenerateMissing([]);
    if (!opts.keepMode) { loadHistory(); setMode("history"); }
  }

  // Salir del wizard: en pasos con evidencia en curso (2–5) pedimos confirmación,
  // porque los archivos capturados no se guardan en el servidor.
  const [exitConfirm, setExitConfirm] = useState(false);
  function attemptExitWizard() {
    if (step >= 2 && step <= 5) setExitConfirm(true);
    else resetWizard();
  }

  function startWizard() {
    resetWizard({ keepMode: true });
    setCaseForm(newCaseForm(historyCases));
    void reloadProfile();
    setMode("wizard");
  }

  /** Paso 1 → 2. En un caso nuevo, la línea sale del agente solo si parece un número (iOS). */
  function handleSelectDevice(d: Device) {
    setSelDevice(d);
    if (!currentCase && d.platform === "ios" && isPhoneLike(d.operator)) {
      setCaseForm(f => (f.linea_dispositivo ? f : { ...f, linea_dispositivo: d.operator.trim() }));
    }
    go(2);
  }

  function proceedResume(cas: Case) {
    clearFiles(); resetRecording(); resetShotSession();
    setCase(cas); setResult(null); setCaseErr({}); setFocusFieldId(null); setGenerateMissing([]);
    setSelDevice({
      serial: cas.device.serial, state: "device",
      manufacturer: cas.device.manufacturer, model: cas.device.model,
      android_version: cas.device.android_version, imei: cas.device.imei,
      name: cas.device.name || cas.device.model, operator: "",
      platform: (cas.device.platform as "android" | "ios") ?? "android",
      ios_version: undefined,
    });
    const legacy = (cas.schema_version ?? 0) === 0;
    if (legacy) {
      // Borrador previo al informe pericial: se completa la causa antes de seguir (§8.9).
      setCaseForm({
        ...newCaseForm(historyCases),
        nro_referencia: cas.nro_referencia ?? "",
        nombre_denunciante: cas.nombre_denunciante ?? "",
        dni_denunciante: cas.dni_denunciante ?? "",
      });
      void reloadProfile();
    } else {
      setCaseForm(caseToForm(cas));
    }
    setDir(1); setStep(legacy ? 2 : 3); setStatus(""); setGlobal("");
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
    <div className="flex flex-col h-screen overflow-hidden bg-fx-bg text-fx-text">

      <ResumeDeviceModal
        cas={resumePending}
        deviceConnected={resumeConnected}
        onConfirm={() => { if (resumePending) proceedResume(resumePending); }}
        onCancel={() => { setResumePending(null); setResumeConnected(false); }}
      />
      <GuideModal
        visible={guideOpen}
        onClose={() => setGuideOpen(false)}
        onSupport={supportEnabled ? () => { setGuideOpen(false); setSupportFromGuide(true); setReportModal(true); } : undefined}
      />

      {supportEnabled && (
        <SoporteModal
          open={reportModal}
          user={user}
          onClose={() => {
            setReportModal(false);
            // El disparador ("Contactar soporte" de la guía) ya no existe: el foco va a "Guía de uso".
            if (supportFromGuide) { setSupportFromGuide(false); guideBtnRef.current?.focus(); }
          }}
        />
      )}

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
        showThemeToggle
        center={
          <div className="flex items-center justify-center gap-1.5 min-w-0">
            {mode === "wizard" && (
              <FxTip label="Volver a Inspecciones" side="bottom">
                <button
                  onClick={attemptExitWizard}
                  aria-label="Volver a Inspecciones"
                  className="flex items-center justify-center w-7 h-7 -ml-1 mr-0.5 rounded-fx-md flex-shrink-0 text-fx-text-3 hover:text-fx-text hover:bg-fx-surface-3 transition-colors duration-fx-fast ease-fx fx-focus-ring"
                >
                  <ArrowLeft className="w-3.5 h-3.5" aria-hidden="true" />
                </button>
              </FxTip>
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
            {mode === "history" && (
              <FxTip label="Nueva inspección" side="bottom">
                <button
                  type="button"
                  onClick={startWizard}
                  aria-label="Nueva inspección"
                  className="hidden sm:inline-flex items-center justify-center w-8 h-8 flex-shrink-0 rounded-fx-md bg-fx-accent text-fx-on-accent hover:bg-fx-accent-hover transition-colors duration-fx-fast ease-fx fx-focus-ring motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
                >
                  <Plus className="w-4 h-4" aria-hidden="true" />
                </button>
              </FxTip>
            )}
            <FxTip label="Guía de uso" side="bottom">
              <button
                ref={guideBtnRef}
                type="button"
                onClick={() => setGuideOpen(true)}
                aria-label="Guía de uso"
                aria-haspopup="dialog"
                aria-expanded={guideOpen}
                className={NAV_ICON_BUTTON}
              >
                <HelpCircle className="w-4 h-4" aria-hidden="true" />
              </button>
            </FxTip>
            {supportEnabled && (
              <FxTip label="Soporte" side="bottom">
                <button
                  type="button"
                  onClick={() => { setSupportFromGuide(false); setReportModal(true); }}
                  aria-label="Soporte"
                  aria-haspopup="dialog"
                  aria-expanded={reportModal}
                  className={NAV_ICON_BUTTON}
                >
                  <LifeBuoy className="w-4 h-4" aria-hidden="true" />
                </button>
              </FxTip>
            )}
          </>
        }
      />

      {/* pb-14: reserva la píldora fija de SystemStatusLine para que no tape las últimas tarjetas */}
      <div className="flex-1 overflow-y-auto pb-14">

        {mode === "history" && (
          <div className="p-4 sm:p-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
            <div className="max-w-5xl mx-auto space-y-8">
              {user && (
                <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4 motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]">
                  <div className="min-w-0">
                    <h1 className="text-fx-display text-fx-text break-words text-balance">
                      {greeting}, <span className="text-fx-accent-text">{user.name}</span>
                    </h1>
                    <p className="mt-2 text-fx-body text-fx-text-2 first-letter:uppercase">{formattedDate}</p>
                  </div>
                  <Button
                    size="large"
                    icon={<Plus className="h-5 w-5" aria-hidden="true" />}
                    label="Nueva inspección"
                    onClick={startWizard}
                    className="w-full sm:w-auto min-h-11 shrink-0"
                  />
                </div>
              )}

              {globalError && (
                <FxBanner tone="error" onClose={() => setGlobal("")}>{globalError}</FxBanner>
              )}

              {historyCases.length > 0 && <DashboardStats cases={historyCases} />}

              {draftCases.length > 0 && (
                <section aria-labelledby="drafts-title">
                  <h2 id="drafts-title" className="flex items-center gap-1.5 text-fx-label uppercase text-fx-text-2">
                    <Play className="w-3.5 h-3.5" aria-hidden="true" />
                    Pendientes de retomar
                  </h2>
                  <ul className="flex gap-3 overflow-x-auto px-1 py-2 -mx-1 mt-1">
                    {draftCases.map((cas) => (
                      <li key={cas.id} className="shrink-0">
                        <button
                          type="button"
                          onClick={() => handleResume(cas)}
                          aria-label={`Retomar inspección ${cas.nro_referencia} — ${cas.device.manufacturer} ${cas.device.model}`}
                          className="fx-card fx-card-interactive flex items-center gap-3 min-h-11 w-60 px-3.5 py-3 text-left"
                        >
                          <span className="w-9 h-9 rounded-fx-md bg-fx-accent-soft text-fx-accent-text flex items-center justify-center shrink-0">
                            <Play className="w-4 h-4" aria-hidden="true" />
                          </span>
                          <span className="min-w-0">
                            <span className="block text-fx-body-sm font-semibold text-fx-text truncate">{cas.nro_referencia}</span>
                            <span className="flex items-center gap-1 text-xs text-fx-text-3 truncate">
                              <Smartphone className="w-3 h-3 shrink-0" aria-hidden="true" />
                              <span className="truncate">{cas.device.manufacturer} {cas.device.model}</span>
                            </span>
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </section>
              )}

              {statusMsg && (
                <FxBanner
                  tone="info"
                  role="status"
                  icon={<Loader2 className="h-4 w-4 shrink-0 mt-0.5 motion-safe:animate-spin" aria-hidden="true" />}
                >
                  {statusMsg}
                </FxBanner>
              )}

              <CaseHistory
                cases={historyCases}
                loading={historyLoading}
                onNewCase={startWizard}
                onRefresh={loadHistory}
                onResume={handleResume}
              />
            </div>
          </div>
        )}

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
                      minJumpable={result ? 6 : currentCase ? 2 : 1}
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
                              onSelect={handleSelectDevice}
                              onRefresh={refreshDevices}
                              onOpenGuide={() => setGuideOpen(true)}
                            />
                          )}

                          {step === 2 && selDevice && (
                            <CaseFormStep
                              device={selDevice}
                              form={caseForm}
                              errors={caseErrors}
                              loading={!!loading.case}
                              mode={currentCase ? "edit" : "create"}
                              isLegacy={isLegacyCase}
                              profile={profile}
                              profileLoading={profileLoading}
                              profileForm={profileForm}
                              focusErrorsTick={focusErrorsTick}
                              focusFieldId={focusFieldId}
                              onFocusConsumed={() => setFocusFieldId(null)}
                              onChange={f => setCaseForm(f)}
                              onProfileChange={setProfileForm}
                              onClearError={key => setCaseErr(er => ({ ...er, [key]: "" }))}
                              onBack={() => go(1)}
                              onCancel={() => go(3)}
                              onSubmit={handleSaveCase}
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
                              onPhotoPerito={(blob, fn) => { handlePhotoBlob(blob, fn); api.reportAgentEvent("webcam", currentCase?.id); }}
                              onPhotoTitular={(blob, fn) => { handlePhotoBlob(blob, fn); api.reportAgentEvent("webcam", currentCase?.id); }}
                              titularNombre={currentCase?.nombre_denunciante ?? caseForm.nombre_denunciante}
                              titularDni={currentCase?.dni_denunciante ?? caseForm.dni_denunciante}
                              captureRoles={currentCase?.capture_roles ?? []}
                              onSetCaptureRole={(name, role) => setCaptureRole(name, role, {
                                caseId: currentCase?.id,
                                onSaved: handleCaptureRolesSaved,
                                onError: setGlobal,
                              })}
                              onUploadAndContinue={() =>
                                handleUploadAndContinue(currentCase!, () => go(4), setGlobal, setStatus, handleCaptureRolesSaved)
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
                            <ReportStep
                              caseId={currentCase.id}
                              focusFieldId={focusFieldId}
                              onFocusConsumed={() => setFocusFieldId(null)}
                              onSaved={texts => setCase(c => (c ? { ...c, report_texts: texts } : c))}
                              onBack={() => go(3)}
                              onContinue={() => go(5)}
                            />
                          )}

                          {step === 5 && currentCase && (
                            <GenerateStep
                              currentCase={currentCase}
                              files={files}
                              loading={!!loading.generate}
                              serverMissing={generateMissing}
                              onBack={() => go(4)}
                              onGenerate={handleGenerate}
                              onGoToField={goToField}
                            />
                          )}

                          {step === 6 && result && currentCase && (
                            <ResultStep
                              caseNumber={currentCase.nro_referencia}
                              zipFile={result.zip}
                              pdfFile={result.pdf}
                              password={result.password}
                              encrypted={result.encrypted}
                              hash={result.hash}
                              reportHash={result.reportHash}
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
