"use client";

import { motion } from "framer-motion";
import { WifiOff } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Device } from "@/types";

interface Props {
  online: boolean;
  device: Device | null;
  recording: boolean;
}

export function AgentChip({ online, device, recording }: Props) {
  const deviceLabel = device
    ? `${device.manufacturer} ${device.model} · ${
        device.platform === "ios"
          ? `iOS ${device.ios_version ?? device.android_version}`
          : `Android ${device.android_version}`
      }`
    : "sin dispositivo";

  return (
    <motion.div
      className={cn(
        "hidden sm:flex items-center gap-2 px-3 py-1.5 rounded-full text-xs border font-medium select-none",
        online
          ? "border-emerald-500/20 bg-emerald-500/[0.06]"
          : "border-amber-500/20 bg-amber-500/[0.06]",
      )}
      initial={{ opacity: 0, y: -6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.2, duration: 0.25 }}
      role="status"
      aria-live="polite"
    >
      <span aria-hidden="true" className={cn(
        "w-1.5 h-1.5 rounded-full flex-shrink-0",
        online ? "bg-emerald-400 animate-pulse" : "bg-amber-400",
      )} />
      {online ? (
        <span className="text-emerald-600 dark:text-emerald-400 truncate max-w-[280px]">
          {device ? deviceLabel : "Tatana activo · esperando dispositivo"}
        </span>
      ) : (
        <span className="flex items-center gap-1.5 text-amber-600 dark:text-amber-400">
          <WifiOff className="w-3 h-3" aria-hidden="true" /> Tatana no disponible
        </span>
      )}
      {recording && (
        <>
          <span aria-hidden="true" className="w-px h-3 flex-shrink-0 opacity-20" style={{ background: "currentColor" }} />
          <span className="flex items-center gap-1 font-bold text-red-500 dark:text-red-400">
            <span aria-hidden="true" className="w-1.5 h-1.5 rounded-full bg-red-500 animate-pulse" />
            REC
          </span>
        </>
      )}
    </motion.div>
  );
}
