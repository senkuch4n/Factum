"use client";

import { useState, useEffect, useCallback, useRef, useMemo } from "react";
import { motion, AnimatePresence, MotionConfig } from "framer-motion";
import { Loader2, Plus, Play, ChevronRight, ArrowLeft, Smartphone, HelpCircle, LifeBuoy } from "lucide-react";
import { Button } from "primereact/button";
import { api, ApiError, type DeviceInput, type ZipLocation } from "@/lib/api";
import { agent, AgentError, type ZipProgress } from "@/lib/agent";
import { agentErrorMessage, agentStatusMessage } from "@/lib/agent-messages";
import { useAgentIdentity, ensureAgentIdentity, isSameHostFor } from "@/hooks/useAgentIdentity";
import { syncedFolderMessage } from "@/components/ResultStep";
import type { GenerateProgress } from "@/components/dashboard/GenerateStep";
import type { Device, AgentEvent, Case, CaseFormData, ProfileFormData } from "@/types";
import {
  CASE_MESSAGES, EMPTY_CASE_FORM, PROFILE_REQUIRED_KEYS, PROFILE_MESSAGES,
  caseToForm, formToCaseRequest, isImeiMissing, isPhoneLike, prefillFromLastCase, profileErrorKey,
  todayIso, validateCaseForm, validateProfile,
} from "@/lib/pericial";
import { useExpertProfile, profileToForm, sameProfile } from "@/hooks/useExpertProfile";
import { usePublicConfig } from "@/hooks/usePublicConfig";
import { ReportStep } from "@/components/ReportStep";
import { useAuth } from "@/hooks/useAuth";
import { useAgentConnection } from "@/hooks/useAgentConnection";
import { useAgentIosStatus } from "@/hooks/useAgentIosStatus";
import { useFileManager, storageOf } from "@/hooks/useFileManager";
import { useRecording } from "@/hooks/useRecording";
import { useAirplayShotSession } from "@/hooks/useAirplayShotSession";
import { AgentChip } from "@/components/dashboard/AgentChip";
import { DashboardStats } from "@/components/dashboard/DashboardStats";
import { GreetingHeadline } from "@/components/dashboard/GreetingHeadline";
import { ResumeDeviceModal } from "@/components/dashboard/ResumeDeviceModal";
import { SoporteModal } from "@/components/dashboard/SoporteModal";
import { GenerateStep } from "@/components/dashboard/GenerateStep";
import { StepIndicator, StepIndicatorCompact } from "@/components/StepIndicator";
import { GuideModal } from "@/components/GuideModal";
import { DeviceConnect } from "@/components/DeviceConnect";
import { CaptureStep } from "@/components/CaptureStep";
import { ResultStep } from "@/components/ResultStep";
import { CaseHistory } from "@/components/CaseHistory";
import { CaseFormStep } from "@/components/CaseFormStep";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { ConfirmDialog } from "@/components/overlay/ConfirmDialog";
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

/** Saludo según la hora local: mañana 6–12, tarde 12–20, noche 20–6. */
function greetingFor(hour: number): string {
  if (hour >= 6 && hour < 12) return "Buenos días";
  if (hour >= 12 && hour < 20) return "Buenas tardes";
  return "Buenas noches";
}

function formatLongDate(date: Date): string {
  return date.toLocaleDateString("es-AR", {
    weekday: "long", day: "numeric", month: "long", year: "numeric",
  });
}

/**
 * Saludo y fecha del encabezado. Se revisan cada minuto para que sigan al reloj
 * con la pestaña abierta; como son strings, solo re-renderiza cuando cambian.
 */
function useGreetingClock() {
  const [greeting, setGreeting] = useState(() => greetingFor(new Date().getHours()));
  const [formattedDate, setFormattedDate] = useState(() => formatLongDate(new Date()));

  useEffect(() => {
    const id = setInterval(() => {
      const now = new Date();
      setGreeting(greetingFor(now.getHours()));
      setFormattedDate(formatLongDate(now));
    }, 60_000);
    return () => clearInterval(id);
  }, []);

  return { greeting, formattedDate };
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
  const [result, setResult] = useState<{
    zip: string; pdf: string; password: string | null; encrypted: boolean; hash: string; reportHash: string;
    // Flujo agent (zip-local-informe-servidor §7.7)
    zipLocation?: ZipLocation | null;
    zipState?: "final" | "pending" | "unknown";
    deleteFiles?: string[];
  } | null>(null);
  // Progreso de la generación en dos tramos (flujo agent).
  const [generateProgress, setGenerateProgress] = useState<GenerateProgress | null>(null);
  const generatingCaseRef = useRef<string | null>(null);
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
    files, loading: fileLoading, videoVariants, pendingVariantFiles, pendingBlobs, localBlobs,
    addFile, addVideoVariant, markPendingVariant,
    removeFile, setCaptureRole, handlePhotoBlob, handleUploadAndContinueLegacy, clearFiles,
    uploadProgress, uploadStates, uploadNotice, dismissUploadNotice, cancelUpload,
    storageMode, saveBusy, bindCase, handleSaveAndContinue, restoreCaseEvidence, cancelSave,
  } = useFileManager();

  // ── Evidencia en esta PC (zip-local-informe-servidor) ────────────
  const identity = useAgentIdentity();
  // El guardado automático (DP1 A) queda atado al caso abierto y a su flujo.
  const currentCaseId = currentCase?.id ?? null;
  const currentStorage = storageOf(currentCase);
  useEffect(() => {
    if (!currentCaseId || !currentStorage) { bindCase(null); return; }
    bindCase({
      id: currentCaseId,
      storage: currentStorage,
      onError: msg => setGlobal(msg),
      onEvidence: res => setCase(c => (c && c.id === currentCaseId
        ? { ...c, evidence: res.evidence, evidence_host: res.evidence_host }
        : c)),
      onRolesSaved: roles => setCase(c => (c && c.id === currentCaseId ? { ...c, capture_roles: roles } : c)),
    });
  }, [currentCaseId, currentStorage, bindCase]);

  /** Motivo por el que no se puede capturar/generar desde esta PC (flujo agent), o `null`. */
  const agentLock = useMemo<{ tone: "warn" | "error"; message: string } | null>(() => {
    if (!currentCase || currentStorage !== "agent") return null;
    if (identity.status === "loading") return null;
    if (identity.status === "offline" || identity.status === "outdated") {
      return { tone: "error", message: agentStatusMessage(identity.status, identity) };
    }
    const hasManifest = (currentCase.evidence?.length ?? 0) > 0;
    if (hasManifest && !isSameHostFor(identity, currentCase)) {
      return {
        tone: "warn",
        message: `La evidencia de este caso está en la PC ${currentCase.evidence_host?.hostname ?? "donde se capturó"}. Seguilo desde esa PC para capturar o generar.`,
      };
    }
    return null;
  }, [currentCase, currentStorage, identity]);

  // Aviso de OneDrive/iCloud (DP4) en el paso 3: una vez por sesión.
  const [syncWarningDismissed, setSyncWarningDismissed] = useState(false);
  const syncWarning = currentStorage === "agent" && !syncWarningDismissed
    && identity.status === "online" && identity.info?.evidence_directory_synced
    ? syncedFolderMessage(identity.info.evidence_directory ?? "")
    : null;

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
    if (event.type === "zip_progress") {
      const d = event.data as unknown as ZipProgress;
      if (d.case_id && d.case_id === generatingCaseRef.current) {
        setGenerateProgress(p => (p && p.stage === "zip" ? { ...p, zip: d } : p));
      }
    }
    if (event.type === "airplay_shot_connected") handleShotConnected();
    if (event.type === "airplay_shot_timeout") handleShotTimeout(msg => setGlobal(msg));
  }, [addFile, addVideoVariant, markPendingVariant, setRecording, setDiscoRec, setDeviceOffline, setAirplayName, isRecordingRef, handleShotConnected, handleShotTimeout]);

  const { agentOnline, devices, loadingDev, refreshDevices } = useAgentConnection(handleWsEvent);
  // ios-herramientas-windows §9.4: servicio de Apple y AirPlay según `/health.ios`.
  const { appleService, airplayAvailable, airplayReason } = useAgentIosStatus(agentOnline);
  const airplayUnavailableReason = airplayAvailable ? null : airplayReason ?? "uxplay_not_found";

  // Tatana se cerró o se volvió a abrir: se revalida la identidad (estado de los avisos del paso 3/5).
  const refreshIdentity = identity.refresh;
  useEffect(() => { void refreshIdentity(); }, [agentOnline, refreshIdentity]);

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

  // ── Foco y scroll al cambiar de paso (DP4 A) ─────────────────────
  // Al avanzar o retroceder, el foco va a la región "Paso N de 6: …" (el lector
  // la anuncia) y la vista vuelve arriba: si no, el foco cae en <body> al
  // desmontarse el botón que se tocó.
  const wizardTopRef = useRef<HTMLDivElement>(null);
  const stepRegionRef = useRef<HTMLDivElement>(null);
  const lastFocusedStep = useRef<number | null>(null);
  useEffect(() => {
    if (mode !== "wizard") { lastFocusedStep.current = null; return; }
    if (lastFocusedStep.current === step) return;
    lastFocusedStep.current = step;
    if (focusFieldId) return; // el paso enfoca su campo (checklist de "Generar")
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    wizardTopRef.current?.scrollIntoView({ block: "start", behavior: reduce ? "auto" : "smooth" });
    stepRegionRef.current?.focus({ preventScroll: true });
    // focusFieldId se lee a propósito sin dependencia: solo importa al cambiar de paso.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, step]);

  /**
   * El paso nuevo ya está montado y visible (fin de la transición). El paso 3
   * no recibe `focusFieldId`, así que el enlace del checklist hacia el bloque
   * de marcas de captura se resuelve acá.
   */
  function handleStepShown() {
    if (step !== 3 || !focusFieldId) return;
    const el = document.getElementById(focusFieldId);
    if (el) { el.focus({ preventScroll: true }); el.scrollIntoView({ block: "center", behavior: "smooth" }); }
    setFocusFieldId(null);
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
    const imeiManual = isImeiMissing(selDevice.imei);
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
    if (storageOf(currentCase) === "agent") { await handleGenerateAgent(); return; }
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

  /**
   * Generación del flujo agent en 3 etapas (SDD §5.5-§5.6, §7.7): `prepare` en el
   * backend → ZIP verificado en Tatana → `finish` con las capturas del informe →
   * commit del ZIP. Ante un error, se descarta el ZIP provisorio.
   */
  async function handleGenerateAgent() {
    const cas = currentCase;
    if (!cas) return;
    setGlobal(""); setGenerateMissing([]);

    // 1. Sin Tatana, desactualizado u otra PC: mensaje sin llamar al backend. El caso no cambia.
    const id = await ensureAgentIdentity(true);
    if (id.status === "offline" || id.status === "outdated" || !id.info) {
      setGlobal(agentStatusMessage(id.status === "outdated" ? "outdated" : "offline", id));
      return;
    }
    if (!isSameHostFor(id, cas)) {
      setGlobal(`La evidencia está en la PC ${cas.evidence_host?.hostname ?? "donde se capturó"}; generalo desde ahí.`);
      return;
    }

    setLoad("generate", true);
    generatingCaseRef.current = cas.id;
    setGenerateProgress({ stage: "zip", zip: null });
    let prep: Awaited<ReturnType<typeof api.prepareGeneration>> | null = null;
    let zipBuilt = false;
    try {
      // 2. Etapa 1: validación + intento (no cambia el status).
      prep = await api.prepareGeneration(cas.id, { hostname: id.info.hostname });

      // 3. Tramo 1: ZIP en esta PC.
      const zip = await agent.buildZip(cas.id, {
        case_ref: prep.case_ref, zip_filename: prep.zip_filename, password: prep.password, files: prep.files,
      });
      zipBuilt = true;

      // 4. Tramo 2: capturas que embebe el informe + metadata, en una sola request.
      setGenerateProgress({ stage: "report" });
      const form = new FormData();
      const metadata = {
        generation_id: prep.generation_id,
        zip_filename: zip.zip_filename,
        zip_hash: zip.zip_hash,
        zip_size: zip.zip_size,
        encrypted: zip.encrypted,
        zip_location: { hostname: zip.hostname, directory: zip.directory, path: zip.zip_path },
        files: zip.files,
      };
      form.append("metadata", new Blob([JSON.stringify(metadata)], { type: "application/json" }));
      for (const name of prep.report_images) {
        const blob = await agent.getCaseFileBlob(cas.id, name);
        form.append("images", blob, name);
      }
      const res = await api.finishGeneration(cas.id, form);

      // 5. Commit: si falla, no es un error de la generación (se completa después).
      let zipState: "final" | "pending" = "final";
      try {
        await agent.commitZip(cas.id, {
          case_ref: prep.case_ref, zip_filename: zip.zip_filename, zip_hash: zip.zip_hash,
          delete_files: prep.files.map(f => f.filename),
        });
      } catch {
        zipState = "pending";
      }
      setResult({
        zip: res.files.zip, pdf: res.files.pdf, password: res.password,
        encrypted: res.case.zip_encrypted === true, hash: res.zip_hash, reportHash: res.report_hash,
        zipLocation: res.zip_location ?? res.case.zip_location ?? metadata.zip_location,
        zipState, deleteFiles: prep.files.map(f => f.filename),
      });
      setCase(res.case);
      go(6);
    } catch (e) {
      // 6. Descarte del ZIP provisorio (best effort) y mensaje accionable.
      // También si el armado falló a mitad: DELETE es idempotente (204 aunque no haya nada).
      if (prep) agent.discardPendingZip(cas.id, prep.case_ref, prep.zip_filename).catch(() => {});
      if (e instanceof AgentError) {
        setGlobal(agentErrorMessage(e, { action: "generate" }));
      } else if (e instanceof ApiError) {
        if (e.missing?.length) setGenerateMissing(e.missing);
        if (e.code === "manifest_mismatch" || e.code === "generation_stale") {
          setGlobal("La evidencia del caso cambió mientras se generaba; volvé a intentar.");
        } else if (e.code === "evidence_on_other_pc") {
          setGlobal(`La evidencia está en la PC ${e.body?.evidence_hostname ?? cas.evidence_host?.hostname ?? "donde se capturó"}; generalo desde ahí.`);
        } else {
          setGlobal(e.message);
        }
        // Si el backend ya marcó el caso en "error" (falla al armar el DOCX), se recarga.
        if (zipBuilt) api.getCase(cas.id).then(r => setCase(c => (c && c.id === cas.id ? r.cas : c))).catch(() => {});
      } else {
        setGlobal("No se pudo generar el caso. Reintentá.");
      }
    } finally {
      generatingCaseRef.current = null;
      setGenerateProgress(null);
      setLoad("generate", false);
    }
  }

  function resetWizard(opts: { keepMode?: boolean } = {}) {
    clearFiles(); resetRecording(); resetShotSession();
    setStep(1); setDir(1);
    setSelDevice(null); setCase(null); setResult(null);
    setCaseForm(EMPTY_CASE_FORM); setCaseErr({}); setStatus(""); setGlobal("");
    setIsResuming(false); setFocusFieldId(null); setGenerateMissing([]);
    setSyncWarningDismissed(false);
    if (!opts.keepMode) { loadHistory(); setMode("history"); }
  }

  // Salir del wizard: en pasos con evidencia en curso (2–5) pedimos confirmación,
  // porque lo que no se guardó se pierde. En el flujo agent, sin pendientes, se sale directo.
  const [exitConfirm, setExitConfirm] = useState(false);
  function attemptExitWizard() {
    const inProgress = step >= 2 && step <= 5;
    const nothingPending = currentStorage === "agent" && !saveBusy && files.every(f => f.uploaded);
    if (inProgress && !nothingPending) setExitConfirm(true);
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

    // Flujo agent (requisito 1 de D8): la bandeja sale del manifiesto fresco y de
    // la carpeta del caso en Tatana; así un borrador con videos sobrevive a una recarga.
    if (storageOf(cas) === "agent") {
      bindCase({ id: cas.id, storage: "agent" });
      api.getCase(cas.id)
        .then(({ cas: fresh }) => {
          setCase(c => (c && c.id === fresh.id ? fresh : c));
          return restoreCaseEvidence(fresh);
        })
        .catch(e => setGlobal(e instanceof ApiError && e.status > 0
          ? e.message
          : "No se pudo recuperar la evidencia del caso. Revisá la conexión con el servidor y reintentá."));
    }
  }

  function handleResume(cas: Case) {
    if (!devices.some(d => d.serial === cas.device.serial)) {
      setResumePending(cas); setResumeConnected(false); return;
    }
    proceedResume(cas);
  }

  const { greeting, formattedDate } = useGreetingClock();

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
        description="Lo que no se guardó en esta PC se va a perder."
        confirmLabel="Salir sin guardar"
        cancelLabel="Seguir acá"
      />

      <AppNavbar
        user={user}
        onLogout={handleLogout}
        showThemeSwitch
        maxWidthClass={mode === "wizard" ? "max-w-6xl" : "max-w-5xl"}
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
                <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4">
                  <div className="min-w-0">
                    <GreetingHeadline greeting={greeting} name={user.name} />
                    {/* Fecha y botón entran como secciones, después de las palabras del saludo. */}
                    <p className="mt-2 text-fx-body text-fx-text-2 first-letter:uppercase motion-safe:animate-[fx-word-in_600ms_var(--fx-ease-out)_400ms_both]">{formattedDate}</p>
                  </div>
                  <Button
                    size="large"
                    icon={<Plus className="h-5 w-5" aria-hidden="true" />}
                    label="Nueva inspección"
                    onClick={startWizard}
                    className="w-full sm:w-auto min-h-11 shrink-0 motion-safe:animate-[fx-word-in_600ms_var(--fx-ease-out)_500ms_both]"
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

        {mode === "wizard" && (
          <div ref={wizardTopRef} className="scroll-mt-0 p-4 sm:p-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
            <div className="mx-auto flex max-w-6xl items-start gap-8">
              {/* Riel vertical de pasos (md+). Los pasos completados son navegables,
                  salvo que eso implique duplicar el caso ya creado (ver minJumpable). */}
              <aside aria-label="Progreso de la inspección" className="sticky top-6 hidden w-52 shrink-0 md:block">
                <StepIndicator
                  steps={STEPS}
                  current={step}
                  minJumpable={result ? 6 : currentCase ? 2 : 1}
                  onSelect={go}
                />
              </aside>

              <div className="min-w-0 flex-1 space-y-4">
                <StepIndicatorCompact steps={STEPS} current={step} className="md:hidden" />

                {globalError && (
                  <FxBanner tone="error" onClose={() => setGlobal("")}>{globalError}</FxBanner>
                )}
                {statusMsg && (
                  <FxBanner tone="info" icon={<Loader2 className="mt-0.5 h-4 w-4 shrink-0 animate-spin" aria-hidden="true" />}>
                    {statusMsg}
                  </FxBanner>
                )}
                {isResuming && step === 3 && currentCase && (
                  <FxBanner tone="info" icon={<Play className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
                    <p className="m-0 font-semibold">Retomando inspección · {currentCase.nro_referencia}</p>
                    <p className="m-0 mt-0.5 text-xs font-normal leading-relaxed text-fx-text-2">
                      {currentStorage === "agent" ? (
                        <>
                          {currentCase.device.manufacturer} {currentCase.device.model} — la evidencia guardada en esta PC se recupera de la carpeta del caso.
                          Reconectá el dispositivo para capturar más evidencia.
                        </>
                      ) : (
                        <>
                          {currentCase.device.manufacturer} {currentCase.device.model} — los archivos de evidencia no se almacenan en el servidor.
                          Reconectá el dispositivo para capturar nueva evidencia y volver a generar el informe.
                        </>
                      )}
                    </p>
                  </FxBanner>
                )}

                <div
                  ref={stepRegionRef}
                  tabIndex={-1}
                  role="region"
                  aria-label={`Paso ${step} de ${STEPS.length}: ${STEPS[step - 1]?.label}`}
                  className="scroll-mt-6 rounded-fx-xl outline-none"
                >
                  {/* reducedMotion="user": con reduced motion framer omite el desplazamiento
                      en x y conserva solo el fundido. */}
                  <MotionConfig reducedMotion="user">
                    <AnimatePresence mode="wait" custom={dir}>
                      <motion.div
                        key={step}
                        custom={dir}
                        variants={slideDir}
                        initial="initial"
                        animate="animate"
                        exit="exit"
                        transition={{ duration: 0.28, ease: EASE }}
                        onAnimationComplete={def => { if (def === "animate") handleStepShown(); }}
                      >
                        {/* fx-card no recorta (sin overflow), así el sticky del paso 3 funciona. */}
                        <div className="fx-card rounded-fx-xl p-5 sm:p-8 lg:p-9">
                          {step === 1 && (
                            <DeviceConnect
                              devices={devices}
                              agentOnline={agentOnline}
                              loading={loadingDev}
                              onSelect={handleSelectDevice}
                              onRefresh={refreshDevices}
                              onOpenGuide={() => setGuideOpen(true)}
                              appleServiceMissing={appleService === "missing"}
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
                                // La tarjeta AirPlay está deshabilitada sin AirPlay; por las dudas, no se llama a Tatana.
                                if (m === "airplay" && airplayUnavailableReason) return;
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
                                currentStorage === "agent"
                                  ? handleSaveAndContinue(currentCase!, () => go(4), setGlobal, setStatus, handleCaptureRolesSaved)
                                  : handleUploadAndContinueLegacy(currentCase!, () => go(4), setGlobal, setStatus, handleCaptureRolesSaved)
                              }
                              caseId={currentCase?.id}
                              storageMode={storageMode}
                              saveBusy={saveBusy}
                              evidenceLock={agentLock}
                              syncWarning={syncWarning}
                              onDismissSyncWarning={() => setSyncWarningDismissed(true)}
                              onContinueWithoutSaving={() => go(4)}
                              loading={loading}
                              deviceOffline={deviceOffline}
                              disconnectedDuringRecord={disconnectedDuringRecord}
                              onRetryRecording={() => {
                                setDiscoRec(false);
                                api.reportAgentEvent("capture_start", currentCase?.id);
                                handleToggleRecord(selDevice, setGlobal);
                              }}
                              onDismissDisconnect={() => setDiscoRec(false)}
                              onRemoveFile={name => {
                                void removeFile(name, {
                                  caseId: currentCase?.id,
                                  onError: setGlobal,
                                  onEvidence: res => setCase(c => (c ? {
                                    ...c,
                                    evidence: res.evidence,
                                    evidence_host: res.evidence_host,
                                    capture_roles: c.capture_roles.filter(r => r.filename !== name),
                                  } : c)),
                                });
                              }}
                              onAttachLocalFile={(blob, filename) => handlePhotoBlob(blob, filename)}
                              localBlobs={localBlobs}
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
                              airplayUnavailableReason={airplayUnavailableReason}
                              androidWithMic={androidWithMic}
                              onToggleAndroidWithMic={setAndroidWithMic}
                              uploading={!!fileLoading.upload}
                              uploadProgress={uploadProgress}
                              uploadStates={uploadStates}
                              uploadNotice={uploadNotice}
                              onCancelUpload={currentStorage === "agent" ? cancelSave : cancelUpload}
                              onDismissUploadNotice={dismissUploadNotice}
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
                              evidenceStorage={currentStorage}
                              evidenceCase={currentCase}
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
                              agentBlocker={agentLock?.message ?? null}
                              progress={generateProgress}
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
                              evidenceStorage={currentStorage}
                              zipLocation={result.zipLocation ?? currentCase.zip_location ?? null}
                              caseRef={currentCase.nro_referencia}
                              zipState={result.zipState ?? "unknown"}
                              sameHost={isSameHostFor(identity, currentCase)}
                              pendingDeleteFiles={result.deleteFiles}
                            />
                          )}
                        </div>
                      </motion.div>
                    </AnimatePresence>
                  </MotionConfig>
                </div>
              </div>
            </div>
          </div>
        )}

      </div>
    </div>
  );
}
