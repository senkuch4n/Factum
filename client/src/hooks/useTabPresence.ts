"use client";

import { useEffect, useRef, useState } from "react";
import { postTabMessage, subscribeTabMessages, type TabMessage } from "@/lib/tab-channel";

type Presence = Extract<TabMessage, { type: "presence" }>;

/** Otra pestaña del wizard con el mismo celular. */
export interface TabPeer {
  originId: string;
  serial: string;
  caseId: string | null;
  caseLabel: string | null;
  step: number;
  /** Última noticia (ms, reloj de esta pestaña). */
  seenAt: number;
}

const HEARTBEAT_MS = 10_000;
const EXPIRE_MS = 30_000;
const PRUNE_MS = 5_000;

interface PresenceInput {
  /** Pasos 2–3 del wizard con un celular elegido. */
  enabled: boolean;
  serial: string | null;
  caseId: string | null;
  caseLabel: string | null;
  step: number;
}

/**
 * Presencia entre pestañas (wizard-concurrencia-pestanas DT16): avisa a las
 * otras pestañas qué celular usa esta y devuelve las que usan el mismo, la más
 * reciente primero. Emite `presence` al cambiar algún dato, cada 10 s y como
 * respuesta (una sola vez) a una pestaña desconocida; `bye` al salir de los
 * pasos 2–3 o al cerrar la pestaña. Una entrada vence a los 30 s sin noticias.
 * Sin `BroadcastChannel` devuelve siempre `[]`.
 */
export function useTabPresence({ enabled, serial, caseId, caseLabel, step }: PresenceInput): TabPeer[] {
  const [peers, setPeers] = useState<Record<string, TabPeer>>({});
  const active = enabled && !!serial;

  // Datos actuales por ref: el listener y el heartbeat no se recrean.
  const selfRef = useRef({ serial, caseId, caseLabel, step });
  useEffect(() => { selfRef.current = { serial, caseId, caseLabel, step }; });

  // Emite al activarse y cada vez que cambia algún dato propio.
  useEffect(() => {
    if (!active || !serial) return;
    postTabMessage({ type: "presence", serial, case_id: caseId, case_label: caseLabel, step, at: Date.now() });
  }, [active, serial, caseId, caseLabel, step]);

  useEffect(() => {
    if (!active) return;
    const known = new Set<string>();

    const announce = () => {
      const s = selfRef.current;
      if (!s.serial) return;
      postTabMessage({ type: "presence", serial: s.serial, case_id: s.caseId, case_label: s.caseLabel, step: s.step, at: Date.now() });
    };

    const onPresence = (m: Presence) => {
      const firstTime = !known.has(m.origin_id);
      known.add(m.origin_id);
      setPeers(prev => ({
        ...prev,
        [m.origin_id]: { originId: m.origin_id, serial: m.serial, caseId: m.case_id, caseLabel: m.case_label, step: m.step, seenAt: Date.now() },
      }));
      // La pestaña nueva todavía no sabe de esta: se le responde una sola vez (sin ping-pong).
      if (firstTime) announce();
    };

    const unsubscribe = subscribeTabMessages(msg => {
      if (msg.type === "presence") { onPresence(msg); return; }
      if (msg.type === "bye") {
        known.delete(msg.origin_id);
        setPeers(prev => {
          if (!(msg.origin_id in prev)) return prev;
          const next = { ...prev };
          delete next[msg.origin_id];
          return next;
        });
      }
    });

    const heartbeat = setInterval(announce, HEARTBEAT_MS);
    const prune = setInterval(() => {
      const limit = Date.now() - EXPIRE_MS;
      setPeers(prev => {
        const stale = Object.values(prev).filter(p => p.seenAt < limit);
        if (stale.length === 0) return prev;
        const next = { ...prev };
        stale.forEach(p => { delete next[p.originId]; });
        return next;
      });
    }, PRUNE_MS);

    const onPageHide = () => postTabMessage({ type: "bye" });
    window.addEventListener("pagehide", onPageHide);

    return () => {
      unsubscribe();
      clearInterval(heartbeat);
      clearInterval(prune);
      window.removeEventListener("pagehide", onPageHide);
      postTabMessage({ type: "bye" });
      setPeers({});
    };
  }, [active]);

  if (!active || !serial) return [];
  return Object.values(peers)
    .filter(p => p.serial === serial)
    .sort((a, b) => b.seenAt - a.seenAt);
}
