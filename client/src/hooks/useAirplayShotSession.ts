"use client";

import { useState } from "react";
import { agent } from "@/lib/agent";

// Sesión de "espejar para capturas" (iOS + AirPlay): se conecta una vez, el fiscal navega
// libremente por el teléfono y va marcando momentos puntuales — los PNG resultantes llegan
// por el evento WS "screenshot_taken" que ya maneja useFileManager, no hace falta agregarlos
// acá. Distinto del botón normal "Capturar pantalla" (fallback automático de un solo tiro).
export function useAirplayShotSession() {
  const [active, setActive]             = useState(false); // sesión iniciada (conectando o conectada)
  const [connected, setConnected]        = useState(false); // el iPhone ya está mirrorenado
  const [receiverName, setReceiverName]  = useState<string | null>(null);
  const [marksCount, setMarksCount]      = useState(0);
  const [loading, setLoadingState]       = useState<Record<string, boolean>>({});

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
      setLoad("shotStart", false);
    }
  }

  async function handleMark(serial: string, onError: (msg: string) => void) {
    setLoad("shotMark", true);
    try {
      const { count } = await agent.markAirplayShot(serial);
      setMarksCount(count);
    } catch (e) {
      onError(e instanceof Error ? e.message : "Error marcando captura");
    } finally {
      setLoad("shotMark", false);
    }
  }

  async function handleStop(serial: string, onError: (msg: string) => void) {
    setLoad("shotStop", true);
    try {
      await agent.stopAirplayShot(serial);
    } catch (e) {
      onError(e instanceof Error ? e.message : "Error finalizando sesión de captura AirPlay");
    } finally {
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
