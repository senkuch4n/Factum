"use client";

import { useEffect, useState, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  X, ChevronRight, ArrowLeft, FolderClosed, File as FileIcon, FileImage, FileVideo,
  FileAudio, FileText, FileSpreadsheet, Package, FileArchive, Loader2, AlertTriangle,
  CheckSquare, Square, Smartphone, RefreshCw,
} from "lucide-react";
import { agent, type DeviceFileEntry } from "@/lib/agent";
import { cn } from "@/lib/utils";

export interface PulledFileRef { filename: string; sourcePath: string; }

interface Props {
  serial: string;
  onClose: () => void;
  onFilesAdded: (files: PulledFileRef[]) => void;
}

type AppFilter = "whatsapp" | "instagram" | "facebook";

const APP_CHIPS: { key: AppFilter; label: string; icon: string }[] = [
  { key: "whatsapp",  label: "WhatsApp",  icon: "/whatsapp.svg" },
  { key: "instagram", label: "Instagram", icon: "/instagram.svg" },
  { key: "facebook",  label: "Facebook",  icon: "/facebook.svg" },
];

const PAGE_SIZE = 150;

/* ── Hints por nombre de carpeta (heurística client-side) ─────────────── */
const FOLDER_HINTS: { test: RegExp; label: string }[] = [
  { test: /whatsapp.*images?/i, label: "Probablemente: fotos de WhatsApp" },
  { test: /whatsapp.*video/i, label: "Probablemente: videos de WhatsApp" },
  { test: /whatsapp.*(voice notes|audio)/i, label: "Probablemente: audios/notas de voz de WhatsApp" },
  { test: /whatsapp.*documents?/i, label: "Probablemente: documentos de WhatsApp" },
  { test: /whatsapp/i, label: "Contenido de WhatsApp" },
  { test: /^screenshots?$/i, label: "Capturas de pantalla" },
  { test: /^camera$/i, label: "Fotos de la cámara" },
  { test: /^dcim$/i, label: "Fotos y videos de la cámara" },
  { test: /^download(s)?$/i, label: "Archivos descargados" },
  { test: /^pictures$/i, label: "Fotos" },
  { test: /^movies$/i, label: "Videos" },
  { test: /instagram/i, label: "Contenido de Instagram" },
  { test: /facebook/i, label: "Contenido de Facebook" },
  { test: /telegram/i, label: "Contenido de Telegram" },
];
function folderHint(name: string): string | null {
  return FOLDER_HINTS.find(h => h.test.test(name))?.label ?? null;
}

/* ── Ícono/color por tipo de archivo (extensión) — estilo Explorador de Windows ── */
const FOLDER_COLOR = "#f2b705";

const EXT_KIND: Record<string, { icon: typeof FileIcon; color: string }> = {};
function registerExts(exts: string[], icon: typeof FileIcon, color: string) {
  exts.forEach(e => { EXT_KIND[e] = { icon, color }; });
}
registerExts(["jpg", "jpeg", "png", "gif", "webp", "heic", "bmp"], FileImage, "#2dd4bf");
registerExts(["mp4", "mkv", "mov", "avi", "webm", "3gp"], FileVideo, "#8b5cf6");
registerExts(["mp3", "m4a", "opus", "wav", "aac", "ogg"], FileAudio, "#a855f7");
registerExts(["pdf"], FileText, "#f87171");
registerExts(["doc", "docx", "txt"], FileText, "#60a5fa");
registerExts(["xls", "xlsx", "csv"], FileSpreadsheet, "#4ade80");
registerExts(["apk"], Package, "#fb923c");
registerExts(["zip", "rar", "7z"], FileArchive, "#facc15");

function fileKind(name: string) {
  const ext = name.split(".").pop()?.toLowerCase() ?? "";
  return EXT_KIND[ext] ?? { icon: FileIcon, color: "var(--text-muted)" };
}

function humanSize(bytes: number): string {
  if (!bytes) return "—";
  const units = ["B", "KB", "MB", "GB"];
  let v = bytes, i = 0;
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
  return `${v.toFixed(v < 10 && i > 0 ? 1 : 0)} ${units[i]}`;
}

function humanDate(iso: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "—";
  return d.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "2-digit" });
}

export function DeviceFileExplorer({ serial, onClose, onFilesAdded }: Props) {
  // Historial de navegación (como el botón "atrás" de un navegador/explorador): cada carpeta
  // visitada se apila, y "atrás" retrocede un paso sin volver a pedirle al usuario que navegue
  // por las migas de pan.
  const [navStack, setNavStack] = useState<string[]>(["/sdcard"]);
  const [navIndex, setNavIndex] = useState(0);
  const path = navStack[navIndex];

  const [entries, setEntries] = useState<DeviceFileEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [mode, setMode] = useState<"browse" | "search">("browse");
  const [searchApp, setSearchApp] = useState<AppFilter | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);
  const [adding, setAdding] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  const fetchDir = useCallback(async (target: string) => {
    setLoading(true); setError(null); setMode("browse"); setSearchApp(null);
    setVisibleCount(PAGE_SIZE);
    try {
      const data = await agent.listDeviceFiles(serial, target);
      setEntries(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo listar la carpeta");
    } finally {
      setLoading(false);
    }
  }, [serial]);

  useEffect(() => { fetchDir(navStack[0]); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  function navigateTo(target: string) {
    setNavStack(prev => [...prev.slice(0, navIndex + 1), target]);
    setNavIndex(i => i + 1);
    fetchDir(target);
  }

  function goBack() {
    if (navIndex === 0) return;
    const newIndex = navIndex - 1;
    setNavIndex(newIndex);
    fetchDir(navStack[newIndex]);
  }

  async function runSearch(app: AppFilter) {
    setLoading(true); setError(null); setMode("search"); setSearchApp(app);
    setVisibleCount(PAGE_SIZE); setSelected(new Set());
    try {
      const data = await agent.searchDeviceFilesByApp(serial, app);
      setEntries(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo buscar");
    } finally {
      setLoading(false);
    }
  }

  function navigate(entry: DeviceFileEntry) {
    if (entry.is_directory) navigateTo(entry.path);
  }

  function toggleSelect(p: string) {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(p)) next.delete(p); else next.add(p);
      return next;
    });
  }

  const visibleFiles = entries.slice(0, visibleCount);
  const selectableFiles = entries.filter(e => !e.is_directory);
  const allSelected = selectableFiles.length > 0 && selectableFiles.every(e => selected.has(e.path));

  function toggleSelectAll() {
    setSelected(prev => {
      if (allSelected) return new Set();
      return new Set(selectableFiles.map(e => e.path));
    });
  }

  async function handleAddToEvidence() {
    setAdding(true); setAddError(null);
    const targets = [...selected];
    try {
      const results = await agent.pullDeviceFiles(serial, targets);
      const okFiles: PulledFileRef[] = [];
      const stillSelected = new Set<string>();
      results.forEach((r, i) => {
        if (r.ok && r.filename) okFiles.push({ filename: r.filename, sourcePath: targets[i] });
        else stillSelected.add(targets[i]);
      });
      if (okFiles.length > 0) onFilesAdded(okFiles);
      setSelected(stillSelected);
      if (stillSelected.size > 0) {
        const failed = results.filter(r => !r.ok);
        setAddError(`${failed.length} archivo(s) no se pudieron traer: ${failed.map(f => f.original_name).join(", ")}`);
      } else {
        onClose();
      }
    } catch (e) {
      setAddError(e instanceof Error ? e.message : "Error agregando a evidencia");
    } finally {
      setAdding(false);
    }
  }

  const segments = path.split("/").filter(Boolean);

  return (
    <motion.div className="explorer-overlay"
      initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
      onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <motion.div className="explorer-panel"
        initial={{ opacity: 0, scale: 0.97, y: 8 }} animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.97, y: 8 }} transition={{ duration: 0.16 }}>

        {/* Header */}
        <div className="flex items-center justify-between gap-3 px-4 py-3 flex-shrink-0"
          style={{ borderBottom: "1px solid var(--border)" }}>
          <div className="flex items-center gap-2 min-w-0">
            <Smartphone className="w-4 h-4 flex-shrink-0" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
            <h2 className="text-sm font-bold truncate" style={{ color: "var(--text-primary)" }}>
              Explorador de archivos del celular
            </h2>
          </div>
          <button onClick={onClose} className="btn-icon btn-ghost" aria-label="Cerrar">
            <X className="w-4 h-4" aria-hidden="true" />
          </button>
        </div>

        {/* Filtros por app + volver a explorar */}
        <div className="flex items-center gap-2 px-4 py-2.5 flex-wrap flex-shrink-0"
          style={{ borderBottom: "1px solid var(--border)" }}>
          {mode === "search" && (
            <button onClick={() => fetchDir(path)}
              className="text-xs font-semibold px-2.5 py-1.5 rounded-md flex items-center gap-1.5"
              style={{ background: "var(--bg-elevated)", color: "var(--text-secondary)" }}>
              <ChevronRight className="w-3 h-3 rotate-180" aria-hidden="true" /> Volver a explorar carpetas
            </button>
          )}
          {APP_CHIPS.map(chip => (
            <button key={chip.key} onClick={() => runSearch(chip.key)}
              className="text-xs font-semibold px-2.5 py-1.5 rounded-md transition-colors flex items-center gap-1.5"
              style={searchApp === chip.key
                ? { background: "rgba(45,212,191,0.15)", color: "#2dd4bf", border: "1px solid rgba(45,212,191,0.3)" }
                : { background: "var(--bg-elevated)", color: "var(--text-secondary)", border: "1px solid var(--border)" }}>
              <img src={chip.icon} alt="" className="w-3.5 h-3.5 flex-shrink-0" />
              {chip.label}
            </button>
          ))}
          {mode === "search" && (
            <span className="text-[9px]" style={{ color: "var(--text-muted)" }}>
              {searchApp === "whatsapp"
                ? "Búsqueda por nombre de archivo en todo el dispositivo"
                : "Búsqueda por carpetas conocidas (cobertura limitada, la app guarda la mayoría del contenido en almacenamiento privado)"}
            </span>
          )}
        </div>

        {/* Atrás + breadcrumbs (solo en modo carpetas) */}
        {mode === "browse" && (
          <div className="flex items-center gap-2 px-4 py-2 flex-wrap flex-shrink-0 text-xs">
            <button onClick={goBack} disabled={navIndex === 0}
              className="w-6 h-6 rounded-md flex items-center justify-center flex-shrink-0 transition-colors disabled:opacity-30 disabled:cursor-not-allowed hover:enabled:bg-[var(--bg-hover)]"
              style={{ color: "var(--text-secondary)" }} aria-label="Atrás">
              <ArrowLeft className="w-3.5 h-3.5" aria-hidden="true" />
            </button>
            <button onClick={() => navigateTo("/sdcard")} className="hover:underline font-medium"
              style={{ color: "var(--text-secondary)" }}>
              Almacenamiento
            </button>
            {segments.slice(1).map((seg, i) => {
              const target = "/" + segments.slice(0, i + 2).join("/");
              return (
                <span key={target} className="flex items-center gap-1">
                  <ChevronRight className="w-3 h-3" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
                  <button onClick={() => navigateTo(target)} className="hover:underline font-medium"
                    style={{ color: "var(--text-secondary)" }}>
                    {seg}
                  </button>
                </span>
              );
            })}
          </div>
        )}

        {/* Hint de la carpeta actual */}
        {mode === "browse" && segments.length > 0 && folderHint(segments[segments.length - 1]) && (
          <div className="mx-4 mb-2 px-2.5 py-1.5 rounded-md text-[10px] font-medium flex-shrink-0"
            style={{ background: "rgba(245,158,11,0.08)", color: "#f59e0b", border: "1px solid rgba(245,158,11,0.2)" }}>
            💡 {folderHint(segments[segments.length - 1])}
          </div>
        )}

        {/* Lista */}
        <div className="flex-1 overflow-y-auto min-h-0">
          {loading && (
            <div className="flex items-center justify-center h-full gap-2 text-sm" style={{ color: "var(--text-muted)" }} role="status" aria-live="polite">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Cargando…
            </div>
          )}
          {!loading && error && (
            <div className="flex flex-col items-center justify-center h-full gap-2 px-6 text-center" role="alert">
              <AlertTriangle className="w-6 h-6" style={{ color: "#f87171" }} aria-hidden="true" />
              <p className="text-sm font-medium" style={{ color: "var(--text-secondary)" }}>{error}</p>
              <button onClick={() => fetchDir(path)} className="btn-ghost btn-sm flex items-center gap-1.5">
                <RefreshCw className="w-3.5 h-3.5" aria-hidden="true" /> Reintentar
              </button>
            </div>
          )}
          {!loading && !error && entries.length === 0 && (
            <div className="flex items-center justify-center h-full text-sm" style={{ color: "var(--text-muted)" }}>
              {mode === "search" ? "No se encontró contenido de esta app en el dispositivo" : "Carpeta vacía"}
            </div>
          )}
          {!loading && !error && entries.length > 0 && (
            <>
              {/* Header de columnas + seleccionar todo */}
              <div className="explorer-row text-[9px] font-bold uppercase tracking-wider sticky top-0"
                style={{ color: "var(--text-muted)", background: "var(--bg-surface)" }}>
                <button onClick={toggleSelectAll} disabled={selectableFiles.length === 0}
                  className="flex items-center justify-center disabled:opacity-30"
                  aria-pressed={allSelected} aria-label={allSelected ? "Deseleccionar todos" : "Seleccionar todos"}>
                  {allSelected ? <CheckSquare className="w-3.5 h-3.5" style={{ color: "#2dd4bf" }} aria-hidden="true" /> : <Square className="w-3.5 h-3.5" aria-hidden="true" />}
                </button>
                <span />
                <span>{mode === "search" ? "Ruta" : "Nombre"}</span>
                <span>Tamaño</span>
                <span>Modificado</span>
              </div>
              {visibleFiles.map(e => {
                const kind = e.is_directory ? null : fileKind(e.name);
                const hint = e.is_directory ? folderHint(e.name) : null;
                const isSelected = selected.has(e.path);
                return (
                  <div key={e.path} className="explorer-row cursor-pointer"
                    role="button"
                    tabIndex={0}
                    aria-label={e.is_directory ? `Abrir carpeta ${e.name}` : `${isSelected ? "Deseleccionar" : "Seleccionar"} ${e.name}`}
                    onClick={() => e.is_directory ? navigate(e) : toggleSelect(e.path)}
                    onKeyDown={ev => {
                      if (ev.key === "Enter" || ev.key === " ") {
                        ev.preventDefault();
                        e.is_directory ? navigate(e) : toggleSelect(e.path);
                      }
                    }}>
                    <button onClick={ev => { ev.stopPropagation(); if (!e.is_directory) toggleSelect(e.path); }}
                      disabled={e.is_directory} tabIndex={-1} aria-hidden="true" className="flex items-center justify-center disabled:opacity-20">
                      {isSelected ? <CheckSquare className="w-3.5 h-3.5" style={{ color: "#2dd4bf" }} /> : <Square className="w-3.5 h-3.5" style={{ color: "var(--text-muted)" }} />}
                    </button>
                    <div className="flex items-center justify-center flex-shrink-0">
                      {e.is_directory
                        ? <FolderClosed className="w-[18px] h-[18px]" style={{ color: FOLDER_COLOR }} fill={FOLDER_COLOR} fillOpacity={0.25} aria-hidden="true" />
                        : <kind.icon className="w-[18px] h-[18px]" style={{ color: kind!.color }} aria-hidden="true" />}
                    </div>
                    <div className="min-w-0">
                      <p className="text-xs font-medium truncate" style={{ color: "var(--text-primary)" }}>
                        {mode === "search" ? e.path : e.name}
                      </p>
                      {hint && <p className="text-[9px] truncate" style={{ color: "#f59e0b" }}>{hint}</p>}
                    </div>
                    <span className="text-[10px]" style={{ color: "var(--text-muted)" }}>
                      {e.is_directory ? "—" : humanSize(e.size)}
                    </span>
                    <span className="text-[10px]" style={{ color: "var(--text-muted)" }}>
                      {humanDate(e.modified_at)}
                    </span>
                  </div>
                );
              })}
              {entries.length > visibleCount && (
                <div className="flex justify-center py-3">
                  <button onClick={() => setVisibleCount(v => v + PAGE_SIZE)} className="btn-ghost btn-sm">
                    Mostrar más ({entries.length - visibleCount} restantes)
                  </button>
                </div>
              )}
            </>
          )}
        </div>

        {/* Barra de selección */}
        <AnimatePresence>
          {selected.size > 0 && (
            <motion.div
              initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: 12 }}
              className="flex items-center justify-between gap-3 px-4 py-3 flex-shrink-0"
              style={{ borderTop: "1px solid var(--border)", background: "var(--bg-elevated)" }}>
              <div className="min-w-0">
                <span className="text-xs font-semibold" style={{ color: "var(--text-primary)" }}>
                  {selected.size} seleccionado{selected.size > 1 ? "s" : ""}
                </span>
                {addError && <p className="text-[9px] mt-0.5" style={{ color: "#f87171" }}>{addError}</p>}
              </div>
              <div className="flex items-center gap-2 flex-shrink-0">
                <button onClick={() => setSelected(new Set())} className="btn-ghost btn-sm">Cancelar</button>
                <button onClick={handleAddToEvidence} disabled={adding}
                  className="btn-primary btn-sm flex items-center gap-1.5">
                  {adding ? <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> : null}
                  Agregar {selected.size} a evidencia
                </button>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </motion.div>
    </motion.div>
  );
}
