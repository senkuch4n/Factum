"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { agent, AGENT_URL, AgentError, type AgentEvent, type AgentExtractionSnapshot } from "@/lib/agent";

/**
 * Feed en vivo de una extracción (extraccion-logica-android §10.2). Se suscribe
 * al WebSocket de Tatana (`extraction_progress` / `extraction_finished` /
 * `extraction_cleanup`) y hace un poll de respaldo cada 5 s si el WS no está.
 * Se reengancha al montar y en cada reconexión de Tatana (Gherkin "al volver a
 * abrir la sección").
 *
 * - `target` = id → sigue esa extracción (`GET /extractions/{id}`);
 * - `target` = `null` → sigue la extracción en curso (`GET /extractions/current`).
 */
export function useExtractionLive(target: string | null) {
  const [snapshot, setSnapshot] = useState<AgentExtractionSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [agentOnline, setAgentOnline] = useState(false);
  const [wsConnected, setWsConnected] = useState(false);
  const targetRef = useRef(target);
  targetRef.current = target;

  const fetchSnapshot = useCallback(async (signal?: AbortSignal) => {
    const t = targetRef.current;
    try {
      const s = t ? await agent.getExtraction(t, signal) : await agent.getCurrentExtraction(signal);
      setAgentOnline(true);
      setSnapshot(s);
      return s;
    } catch (e) {
      // `unreachable` = Tatana caído; un 404 (otra PC no tiene la extracción) deja el snapshot como estaba.
      if (e instanceof AgentError && e.kind === "unreachable") setAgentOnline(false);
      return null;
    }
  }, []);

  // Carga inicial y cuando cambia el `target`.
  useEffect(() => {
    let alive = true;
    setLoading(true);
    fetchSnapshot().finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [target, fetchSnapshot]);

  // WebSocket con reconexión + poll de respaldo.
  useEffect(() => {
    let ws: WebSocket | null = null;
    let retry: ReturnType<typeof setTimeout> | null = null;
    let poll: ReturnType<typeof setInterval> | null = null;
    let destroyed = false;

    function matches(data: Record<string, unknown>): boolean {
      const t = targetRef.current;
      if (!t) return true; // en curso: una sola extracción a la vez (D12)
      const id = typeof data.id === "string" ? data.id
        : typeof data.extraction_id === "string" ? data.extraction_id : null;
      return id === t;
    }

    function startPoll() { if (!poll) poll = setInterval(() => { void fetchSnapshot(); }, 5000); }
    function stopPoll() { if (poll) { clearInterval(poll); poll = null; } }

    async function connect() {
      if (destroyed) return;
      let online = false;
      try {
        const res = await fetch(`${AGENT_URL}/health`, { signal: AbortSignal.timeout(5000) });
        online = res.ok;
      } catch { online = false; }
      if (destroyed) return;
      setAgentOnline(online);
      if (!online) { setWsConnected(false); startPoll(); retry = setTimeout(connect, 3000); return; }

      void fetchSnapshot(); // reenganche al (re)conectar
      ws = agent.connectWS((e: AgentEvent) => {
        if (e.type === "extraction_progress" || e.type === "extraction_finished") {
          if (matches(e.data)) setSnapshot(e.data as unknown as AgentExtractionSnapshot);
        } else if (e.type === "extraction_cleanup") {
          // Limpieza posterior (desinstalación del connector): trae la adenda y `registered=false`.
          void fetchSnapshot();
        }
      });
      ws.onopen = () => { if (!destroyed) { setWsConnected(true); stopPoll(); } };
      ws.onerror = () => { if (!destroyed) { setWsConnected(false); startPoll(); } };
      ws.onclose = () => {
        if (destroyed) return;
        setWsConnected(false);
        startPoll();
        ws = null;
        retry = setTimeout(connect, 3000);
      };
    }

    void connect();
    return () => {
      destroyed = true;
      if (retry) clearTimeout(retry);
      stopPoll();
      ws?.close();
    };
  }, [fetchSnapshot]);

  const refresh = useCallback(() => fetchSnapshot(), [fetchSnapshot]);
  return { snapshot, loading, agentOnline, wsConnected, refresh };
}
