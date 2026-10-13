"use client";

import { useRef, useState } from "react";
import { agent, type AgentAttribution } from "@/lib/agent";

// Sesión de "espejar para capturas" (iOS + AirPlay): se conecta una vez, el fiscal navega
// libremente por el teléfono y va marcando momentos puntuales. Al finalizar, Tatana extrae
// un PNG por marca y los devuelve en la respuesta de `stop`: `handleStop` los devuelve y el
// llamador los agrega a la bandeja (wizard-concurrencia-pestanas DT6). El `screenshot_taken`
// por WS de cada marca solo lo toman las otras pestañas del mismo caso (filtro por pestaña).
// Distinto del botón normal "Capturar pantalla" (fallback automático de un solo tiro).
export function useAirplayShotSession() {
  const [active, setActive]             = useState(false); // sesión iniciada (conectando o conectada)
  const [connected, setConnected]        = useState(false); // el iPhone ya está mirrorenado
  const [receiverName, setReceiverName]  = useState<string | null>(null);
  const [marksCount, setMarksCount]      = useState(0);
  const [loading, setLoadingState]       = useState<Record<string, boolean>>({});
  // Guards sincrónicos de doble clic, uno por acción (DT8).
  const startInFlight = useRef(false);
  const markInFlight = useRef(false);
  const stopInFlight = useRef(false);

  function setLoad(key: string, val: boolean) {
    setLoadingState(l => ({ ...l, [key]: val }));
  }

  function resetShotSession() {
    setActive(false);
    setConnected(false);
    setReceiverName(null);
    setMarksCount(0);
  }

  async function handleStart(serial: string, onError: (msg: string) => void) {
    if (startInFlight.current) return;
    startInFlight.current = true;
    setLoad("shotStart", true);
    try {
      const { receiver_name } = await agent.startAirplayShot(serial);
      setReceiverName(receiver_name);
      setConnected(false);
      setMarksCount(0);
      setActive(true);
    } catch (e) {
      onError(e instanceof Error ? e.message : "Error iniciando sesión de captura AirPlay");
    } finally {
      startInFlight.current = false;
      setLoad("shotStart", false);
    }
  }

  async function handleMark(serial: string, onError: (msg: string) => void) {
    if (markInFlight.current) return;
    markInFlight.current = true;
    setLoad("shotMark", true);
    try {
      const { count } = await agent.markAirplayShot(serial);
      setMarksCount(count);
    } catch (e) {
      onError(e instanceof Error ? e.message : "Error marcando captura");
    } finally {
      markInFlight.current = false;
      setLoad("shotMark", false);
    }
  }

  /** Finaliza la sesión. Devuelve los PNG extraídos (`[]` si falló o si ya había un stop en vuelo). */
  async function handleStop(
    serial: string, onError: (msg: string) => void, attribution?: AgentAttribution,
  ): Promise<{ filename: string; url: string }[]> {
    if (stopInFlight.current) return [];
    stopInFlight.current = true;
    setLoad("shotStop", true);
    try {
      const res = await agent.stopAirplayShot(serial, attribution);
      return Array.isArray(res?.files) ? res.files.filter(f => !!f?.filename) : [];
    } catch (e) {
      onError(e instanceof Error ? e.message : "Error finalizando sesión de captura AirPlay");
      return [];
    } finally {
      stopInFlight.current = false;
      setLoad("shotStop", false);
      resetShotSession();
    }
  }

  // WS: "airplay_shot_connected" — el iPhone ya está mirrorenado, habilitar "Marcar captura".
  function handleShotConnected() {
    setConnected(true);
  }

  // WS: "airplay_shot_timeout" — nadie conectó a tiempo, volver a estado idle.
  function handleShotTimeout(onError: (msg: string) => void) {
    onError("El iPhone no se conectó a AirPlay a tiempo. Verificá WiFi y activá Espejo de pantalla.");
    resetShotSession();
  }

  return {
    active, connected, receiverName, marksCount, loading,
    handleStart, handleMark, handleStop, resetShotSession,
    handleShotConnected, handleShotTimeout,
  };
}
