"use client";

import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

/** "mm:ss", o "h:mm:ss" desde 1 h. Negativos (reloj de la PC atrasado) → 00:00. */
export function formatElapsed(totalSeconds: number): string {
  const t = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(t / 3600);
  const m = Math.floor((t % 3600) / 60);
  const s = t % 60;
  const pad = (n: number) => String(n).padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

/** Hora local "HH:MM" de un ISO-8601, o `null` si no se puede leer. */
export function clockTime(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  return d.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });
}

/**
 * Cronómetro desde `since` (inicio real de Tatana, grabacion-android-robusta D8):
 * sobrevive a recargas porque no cuenta desde el montaje. Se actualiza cada 1 s
 * y no se anuncia (`aria-live="off"`): el lector no lo lee cada segundo.
 */
export function ElapsedTime({ since, className }: { since: string; className?: string }) {
  const start = Date.parse(since);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  if (Number.isNaN(start)) return null;
  const text = formatElapsed((now - start) / 1000);
  return (
    <span aria-live="off" className={cn("shrink-0 tabular-nums", className)}>
      <span className="sr-only">Tiempo de grabación: </span>
      {text}
    </span>
  );
}
