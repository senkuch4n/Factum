"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "primereact/button";
import { ArrowLeft, ArrowRight, HardDriveDownload, Loader2 } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { useAgentConnection } from "@/hooks/useAgentConnection";
import { useDeviceConnection } from "@/hooks/useDeviceConnection";
import {
  agent, AgentError,
  type AgentDestinationCheck, type AgentDestinations, type AgentExtractionEstimate, type AgentExtractionPreflight,
  type AgentExtractionStartRequest, type Device,
} from "@/lib/agent";
import { api } from "@/lib/api";
import { probeServerClock } from "@/lib/device-details";
import { getTabOriginId } from "@/lib/tab-origin";
import { AppNavbar } from "@/components/shell/AppNavbar";
import { StepIndicator, StepIndicatorCompact, type Step } from "@/components/StepIndicator";
import { StepActions } from "@/components/wizard/StepActions";
import { DeviceStep } from "./steps/DeviceStep";
import { SelectionStep } from "./steps/SelectionStep";
import { DestinationStep } from "./steps/DestinationStep";
import { ConfirmStep, type StartError } from "./steps/ConfirmStep";
import {
  categoryKind, defaultSelection, deviceModificationsPreview, hasAnySelection, needsConfirmation,
  type ExtractionSelection,
} from "@/lib/extraction";
import { formatBytes } from "@/lib/format";

const STEPS: Step[] = [
  { id: 1, label: "Dispositivo", sublabel: "Elegí el celular" },
  { id: 2, label: "Qué extraer", sublabel: "Categorías y carpetas" },
  { id: 3, label: "Destino", sublabel: "Dónde guardar" },
  { id: 4, label: "Confirmar", sublabel: "Revisá y empezá" },
];

const EMPTY_SELECTION: ExtractionSelection = { keys: [], customPaths: [], verifyOnDevice: true };

export function NewExtractionWizard() {
  const router = useRouter();
  const { user, handleLogout } = useAuth({ withHistory: false });
  const { agentOnline, devices, loadingDev, refreshDevices } = useAgentConnection(noop);

  const [step, setStep] = useState(1);
  const [device, setDevice] = useState<Device | null>(null);
  const [preflight, setPreflight] = useState<AgentExtractionPreflight | null>(null);
  const [preflightLoading, setPreflightLoading] = useState(false);
  const [preflightError, setPreflightError] = useState<string | null>(null);
  const [selection, setSelection] = useState<ExtractionSelection>(EMPTY_SELECTION);
  const [estimate, setEstimate] = useState<AgentExtractionEstimate | null>(null);
  const [estimateLoading, setEstimateLoading] = useState(false);
  const [destinations, setDestinations] = useState<AgentDestinations | null>(null);
  const [destLoading, setDestLoading] = useState(false);
  const [destPath, setDestPath] = useState<string | null>(null);
  const [destCheck, setDestCheck] = useState<AgentDestinationCheck | null>(null);
  const [checkLoading, setCheckLoading] = useState(false);
  const [confirmChecked, setConfirmChecked] = useState(false);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<StartError | null>(null);

  const connection = useDeviceConnection(agentOnline, devices, loadingDev, device?.serial ?? null);

  // Foco al contenido al cambiar de paso (accesibilidad §13.2), sin robar el foco en el montaje inicial.
  const stepRef = useRef<HTMLDivElement | null>(null);
  const mounted = useRef(false);
  useEffect(() => {
    if (!mounted.current) { mounted.current = true; return; }
    stepRef.current?.focus();
  }, [step]);

  const loadPreflight = useCallback((serial: string) => {
    setPreflight(null);
    setPreflightError(null);
    setPreflightLoading(true);
    agent.getExtractionPreflight(serial)
      .then(pf => { setPreflight(pf); setSelection(defaultSelection(pf)); })
      .catch(e => setPreflightError(describePreflightError(e)))
      .finally(() => setPreflightLoading(false));
  }, []);

  useEffect(() => {
    if (!device || device.platform !== "android") return;
    loadPreflight(device.serial);
  }, [device, loadPreflight]);

  // Estimación (debounce 400 ms) al cambiar categorías de archivos o carpetas.
  const fileCats = useMemo(() => selection.keys.filter(k => categoryKind(k) === "files"), [selection.keys]);
  useEffect(() => {
    if (!device) return;
    let alive = true;
    const h = setTimeout(() => {
      setEstimateLoading(true);
      agent.estimateExtraction(device.serial, { file_categories: fileCats, custom_paths: selection.customPaths })
        .then(e => { if (alive) setEstimate(e); })
        .catch(() => { if (alive) setEstimate(null); })
        .finally(() => { if (alive) setEstimateLoading(false); });
    }, 400);
    return () => { alive = false; clearTimeout(h); };
  }, [device, fileCats, selection.customPaths]);

  const loadDestinations = useCallback(() => {
    setDestLoading(true);
    agent.getDestinations().then(setDestinations).catch(() => setDestinations(null)).finally(() => setDestLoading(false));
  }, []);
  useEffect(() => { if (step === 3 && !destinations) loadDestinations(); }, [step, destinations, loadDestinations]);

  // Chequeo del destino en cada cambio de ruta o del tamaño estimado.
  useEffect(() => {
    if (!destPath) { setDestCheck(null); return; }
    let alive = true;
    const ctrl = new AbortController();
    setCheckLoading(true);
    agent.checkDestination({ path: destPath, required_bytes: estimate?.total_bytes ?? 0 }, ctrl.signal)
      .then(c => { if (alive) setDestCheck(c); })
      .catch(() => { if (alive) setDestCheck(null); })
      .finally(() => { if (alive) setCheckLoading(false); });
    return () => { alive = false; ctrl.abort(); };
  }, [destPath, estimate?.total_bytes]);

  const modifications = useMemo(() => deviceModificationsPreview(selection.keys), [selection.keys]);

  const canProceed = useCallback((): boolean => {
    switch (step) {
      case 1: return !!device && device.platform === "android" && !!preflight && connection === "connected";
      case 2: return hasAnySelection(selection);
      case 3: return !!destPath && destCheck?.ok === true;
      case 4: return (!needsConfirmation(selection.keys) || confirmChecked) && !starting;
      default: return false;
    }
  }, [step, device, preflight, connection, selection, destPath, destCheck, confirmChecked, starting]);

  async function onStart() {
    if (!device || !destPath || !user) return;
    setStarting(true);
    setStartError(null);
    try {
      const structured = selection.keys.filter(k => categoryKind(k) === "structured");
      const files = selection.keys.filter(k => categoryKind(k) === "files");
      const probe = await probeServerClock();
      const body: AgentExtractionStartRequest = {
        serial: device.serial,
        structured_categories: structured,
        file_categories: files,
        custom_paths: selection.customPaths,
        destination_path: destPath,
        verify_on_device: selection.verifyOnDevice && (preflight?.device_sha256_available ?? false),
        confirm_device_modifications: needsConfirmation(selection.keys),
        operator: { dni: user.dni, name: user.name },
        ...(probe ? { pc_server_probe: probe } : {}),
        origin_id: getTabOriginId(),
      };
      const snap = await agent.startExtraction(body);
      // Registro inicial best-effort (§10.2 paso 5): si falla, la sección lo reintenta.
      try {
        if (snap.record) {
          await api.upsertExtraction(snap.id, { ...snap.record, ...(probe ? { pc_server_probe: probe } : {}) });
        }
      } catch { /* reintento diferido */ }
      router.replace(`/extracciones/${snap.id}`);
    } catch (e) {
      setStartError(describeStartError(e));
      setStarting(false);
    }
  }

  if (!user) {
    return (
      <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
        <AppNavbar user={null} showThemeSwitch brandHref="/dashboard" maxWidthClass="max-w-4xl"
          sections={{ activeSection: "extracciones" }} />
        <div role="status" aria-label="Cargando" className="mx-auto w-full max-w-4xl flex-1 px-4 pt-10 sm:px-6">
          <Loader2 className="mx-auto h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
          <span className="sr-only">Cargando…</span>
        </div>
      </div>
    );
  }

  return (
    <div className="flex min-h-dvh flex-col bg-fx-bg text-fx-text">
      <AppNavbar user={user} onLogout={handleLogout} showThemeSwitch brandHref="/dashboard" maxWidthClass="max-w-4xl"
        sections={{ activeSection: "extracciones" }} />

      <main className="mx-auto w-full max-w-4xl flex-1 px-4 pb-16 pt-5 sm:px-6 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
        <header className="mb-5 flex items-center gap-2">
          <HardDriveDownload className="h-5 w-5 text-fx-text-2" aria-hidden="true" />
          <h1 className="m-0 text-fx-h2 text-fx-text">Nueva extracción</h1>
        </header>

        <div className="md:grid md:grid-cols-[200px_1fr] md:gap-8">
          <aside className="hidden md:block">
            <StepIndicator steps={STEPS} current={step} minJumpable={1} onSelect={id => { if (id < step) setStep(id); }} />
          </aside>

          <div className="min-w-0 space-y-6">
            <StepIndicatorCompact steps={STEPS} current={step} className="md:hidden" />

            <div
              key={step}
              ref={stepRef}
              tabIndex={-1}
              className="outline-none motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]"
            >
              {step === 1 && (
                <DeviceStep
                  devices={devices}
                  agentOnline={agentOnline}
                  loadingDev={loadingDev}
                  onRefresh={refreshDevices}
                  device={device}
                  onSelect={setDevice}
                  onClear={() => { setDevice(null); setPreflight(null); setPreflightError(null); }}
                  connection={connection}
                  preflight={preflight}
                  preflightLoading={preflightLoading}
                  preflightError={preflightError}
                  onRetryPreflight={() => { if (device) loadPreflight(device.serial); }}
                />
              )}
              {step === 2 && device && preflight && (
                <SelectionStep
                  serial={device.serial}
                  preflight={preflight}
                  selection={selection}
                  onChange={setSelection}
                  estimate={estimate}
                  estimateLoading={estimateLoading}
                />
              )}
              {step === 3 && (
                <DestinationStep
                  destinations={destinations}
                  loading={destLoading}
                  onRefresh={loadDestinations}
                  destPath={destPath}
                  onPick={setDestPath}
                  destCheck={destCheck}
                  checkLoading={checkLoading}
                />
              )}
              {step === 4 && preflight && (
                <ConfirmStep
                  preflight={preflight}
                  selection={selection}
                  estimate={estimate}
                  finalFolderPreview={destCheck?.final_folder_preview ?? null}
                  modifications={modifications}
                  confirmChecked={confirmChecked}
                  onConfirmChange={setConfirmChecked}
                  startError={startError}
                />
              )}
            </div>

            <StepActions>
              {step === 1 ? (
                <Button
                  severity="secondary"
                  label="Cancelar"
                  icon={<ArrowLeft className="h-4 w-4" aria-hidden="true" />}
                  onClick={() => router.push("/extracciones")}
                  className="w-full min-h-11 sm:w-auto"
                />
              ) : (
                <Button
                  severity="secondary"
                  label="Atrás"
                  icon={<ArrowLeft className="h-4 w-4" aria-hidden="true" />}
                  onClick={() => setStep(s => Math.max(1, s - 1))}
                  disabled={starting}
                  className="w-full min-h-11 sm:w-auto"
                />
              )}
              {step < 4 ? (
                <Button
                  label="Siguiente"
                  icon={<ArrowRight className="h-4 w-4" aria-hidden="true" />}
                  iconPos="right"
                  disabled={!canProceed()}
                  onClick={() => setStep(s => Math.min(4, s + 1))}
                  className="w-full min-h-11 sm:flex-1"
                />
              ) : (
                <Button
                  label={starting ? "Iniciando…" : "Iniciar extracción"}
                  icon={starting
                    ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                    : <HardDriveDownload className="h-4 w-4" aria-hidden="true" />}
                  iconPos="right"
                  disabled={!canProceed()}
                  onClick={onStart}
                  className="w-full min-h-11 sm:flex-1"
                />
              )}
            </StepActions>
          </div>
        </div>
      </main>
    </div>
  );
}

function noop() { /* la lista de equipos la maneja useAgentConnection por WS */ }

function describePreflightError(e: unknown): string {
  if (e instanceof AgentError) {
    if (e.kind === "unreachable") return "Tatana no responde. Abrí la aplicación y reintentá.";
    if (e.code === "platform_not_supported") return "La extracción de iPhone todavía no está disponible.";
    if (e.code === "device_not_found") return "El celular ya no está conectado.";
  }
  return "No se pudo leer el estado del celular.";
}

function describeStartError(e: unknown): StartError {
  if (!(e instanceof AgentError)) return { message: "No se pudo iniciar la extracción." };
  if (e.kind === "unreachable") return { message: "Tatana no responde. Abrí la aplicación y reintentá." };
  switch (e.code) {
    case "extraction_in_progress":
      return { message: "Ya hay una extracción en curso en esta PC.", vigenteId: e.body?.extraction_id };
    case "device_busy":
      return { message: "El celular está ocupado grabando. Esperá a que termine la grabación." };
    case "device_unauthorized":
      return { message: "El celular revocó la autorización. Reconectalo y tocá “Permitir”." };
    case "device_not_found":
      return { message: "El celular ya no está conectado." };
    case "path_not_allowed":
      return { message: "La carpeta de destino no está permitida." };
    case "destination_not_writable":
      return { message: "No se puede escribir en la carpeta de destino." };
    case "insufficient_storage":
      return { message: `Falta espacio en el destino${e.body?.missing_bytes ? ` (faltan ${formatBytes(e.body.missing_bytes)})` : ""}.` };
    case "platform_not_supported":
      return { message: "La extracción de iPhone todavía no está disponible." };
    case "confirmation_required":
      return { message: "Tenés que confirmar las modificaciones al dispositivo." };
    default:
      return { message: e.body?.error || "No se pudo iniciar la extracción." };
  }
}
