"use client";

import { useState, useRef, useEffect } from "react";
import { agent } from "@/lib/agent";
import type { Device, IosRecordMode } from "@/types";

export function useRecording(
  addFile: (name: string) => void,
  markPendingVariant: (filename: string) => void,
) {
  const [isRecording, setRecording]               = useState(false);
  const [disconnectedDuringRecord, setDiscoRec]   = useState(false);
  const [deviceOffline, setDeviceOffline]         = useState(false);
  const [iosModePicker, setIosModePicker]         = useState(false);
  const [iosRecordMode, setIosRecordMode]         = useState<IosRecordMode | null>(null);
  const [airplayReceiverName, setAirplayName]     = useState<string | null>(null);
  const [androidWithMic, setAndroidWithMic]       = useState(false);
  const [loadingRec, setLoadingRec]               = useState<Record<string, boolean>>({});
  const isRecordingRef                            = useRef(false);

  useEffect(() => { isRecordingRef.current = isRecording; }, [isRecording]);

  function setLoad(key: string, val: boolean) {
    setLoadingRec(l => ({ ...l, [key]: val }));
  }

  async function handleToggleRecord(device: Device, onError: (msg: string) => void) {
    const platform = device.platform ?? "android";
    if (isRecording) {
      setLoad("stopRecord", true);
      try {
        const stopped = await agent.stopRecording(device.serial, platform);
        if (stopped?.filename) addFile(stopped.filename);
      } catch (e) {
        // El agente ya limpió su estado: reintentar el stop no sirve, así que
        // igual se sale del modo grabación (abajo) y se avisa el motivo real.
        onError(`No se pudo guardar la grabación: ${e instanceof Error ? e.message : "error desconocido"}`);
      } finally {
        setLoad("stopRecord", false);
      }
      setRecording(false);
      setIosRecordMode(null);
    } else if (platform === "ios") {
      setIosModePicker(true);
    } else {
      setLoad("startRecord", true);
      try {
        await agent.startRecording(device.serial, device.android_version, platform, undefined, androidWithMic);
        setRecording(true);
      } catch (e) {
        onError(e instanceof Error ? e.message : "Error al iniciar grabación");
      } finally {
        setLoad("startRecord", false);
      }
    }
  }

  async function handleSelectIosMode(mode: IosRecordMode, device: Device, onError: (msg: string) => void) {
    setIosModePicker(false);
    setIosRecordMode(mode);
    setAirplayName(null);
    setLoad("startRecord", true);
    try {
      await agent.startRecording(device.serial, device.android_version, "ios", mode);
      setRecording(true);
    } catch (e) {
      onError(e instanceof Error ? e.message : "Error al iniciar grabación");
      setIosRecordMode(null);
    } finally {
      setLoad("startRecord", false);
    }
  }

  function resetRecording() {
    setRecording(false);
    setDiscoRec(false);
    setDeviceOffline(false);
    setIosModePicker(false);
    setIosRecordMode(null);
    setAirplayName(null);
    setAndroidWithMic(false);
  }

  return {
    isRecording, disconnectedDuringRecord, deviceOffline,
    iosModePicker, iosRecordMode, airplayReceiverName, androidWithMic,
    isRecordingRef, loading: loadingRec,
    setRecording, setDiscoRec, setDeviceOffline, setAirplayName, setIosModePicker, setAndroidWithMic,
    handleToggleRecord, handleSelectIosMode, resetRecording,
  };
}
