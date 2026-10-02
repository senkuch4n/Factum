"use client";

import { useEffect, useState, useCallback } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { Checkbox } from "primereact/checkbox";
import {
  ChevronRight, ArrowLeft, FolderClosed, File as FileIcon, FileImage, FileVideo,
  FileAudio, FileText, FileSpreadsheet, Package, FileArchive, Loader2, AlertTriangle,
  Smartphone, RefreshCw, Check, Lightbulb,
} from "lucide-react";
import { agent, type DeviceFileEntry } from "@/lib/agent";
import { FOCUS_RING } from "@/lib/prime/pt/shared";
import { cn } from "@/lib/utils";
import { FxTip } from "./overlay/FxTip";

export interface PulledFileRef { filename: string; sourcePath: string; }

interface Props {
  open: boolean;
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

/* ── Ícono por tipo de archivo (extensión). Sin color por tipo (DP5 A): el ícono ya diferencia. ── */
const EXT_KIND: Record<string, { icon: typeof FileIcon }> = {};
function registerExts(exts: string[], icon: typeof FileIcon) {
  exts.forEach(e => { EXT_KIND[e] = { icon }; });
}
registerExts(["jpg", "jpeg", "png", "gif", "webp", "heic", "bmp"], FileImage);
registerExts(["mp4", "mkv", "mov", "avi", "webm", "3gp"], FileVideo);
registerExts(["mp3", "m4a", "opus", "wav", "aac", "ogg"], FileAudio);
registerExts(["pdf"], FileText);
registerExts(["doc", "docx", "txt"], FileText);
registerExts(["xls", "xlsx", "csv"], FileSpreadsheet);
registerExts(["apk"], Package);
registerExts(["zip", "rar", "7z"], FileArchive);

function fileKind(name: string) {
  const ext = name.split(".").pop()?.toLowerCase() ?? "";
  return EXT_KIND[ext] ?? { icon: FileIcon };
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

/** Grilla de una fila: check · ícono · nombre (· tamaño · fecha en sm+). */
const ROW = cn(
  "grid min-h-10 items-center gap-2.5 border-b border-fx-border px-3",
  "grid-cols-[2.25rem_1.5rem_minmax(0,1fr)] sm:grid-cols-[2.25rem_1.5rem_minmax(0,1fr)_6rem_7rem]",
);
const CHECK_ICON = <Check className="h-3.5 w-3.5" strokeWidth={3} aria-hidden="true" />;

/**
 * Explorador de archivos del celular (Android): Dialog estándar. El
 * componente público es solo el diálogo; el `Body` (navegación, búsqueda,
 * selección) se monta al abrir, así el explorador arranca de cero en cada
 * apertura. Escape y la X cierran; la máscara no (se perdería la selección).
 */
export function DeviceFileExplorer({ open, serial, onClose, onFilesAdded }: Props) {
  return (
    <Dialog
      visible={open}
      onHide={onClose}
      dismissableMask={false}
      draggable={false}
      resizable={false}
      // Tamaño y pantalla completa en < sm por pt (el className de props pierde
      // contra el pt global y en unstyled maskClassName no se aplica).
      pt={{
        root: {
          className:
            "w-[min(48rem,100%)] h-[85vh] max-h-[85vh] max-sm:w-full max-sm:h-full max-sm:max-h-full max-sm:rounded-none max-sm:border-0",
        },
        header: { className: "px-4 pt-3 pb-3" },
        headerTitle: { className: "text-fx-body-sm font-bold" },
        content: { className: "flex min-h-0 flex-1 flex-col overflow-hidden p-0" },
        mask: { className: "max-sm:p-0" },
      }}
      header={
        <span className="flex min-w-0 items-center gap-2">
          <Smartphone className="h-4 w-4 shrink-0 text-fx-text-3" aria-hidden="true" />
          <span className="truncate">Explorador de archivos del celular</span>
        </span>
      }
    >
      <ExplorerBody serial={serial} onClose={onClose} onFilesAdded={onFilesAdded} />
    </Dialog>
  );
}

function ExplorerBody({ serial, onClose, onFilesAdded }: Omit<Props, "open">) {
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
  const currentHint = mode === "browse" && segments.length > 0 ? folderHint(segments[segments.length - 1]) : null;
  const crumbs = [
    { label: "Almacenamiento", target: "/sdcard" },
    ...segments.slice(1).map((seg, i) => ({ label: seg, target: "/" + segments.slice(0, i + 2).join("/") })),
  ];

  return (
    <>
      {/* Filtros por app + volver a explorar */}
      <div className="flex shrink-0 flex-wrap items-center gap-2 border-y border-fx-border px-4 py-2.5">
        {mode === "search" && (
          <Button type="button" text severity="secondary" size="small"
            icon={<ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />}
            label="Volver a explorar carpetas" onClick={() => fetchDir(path)} />
        )}
        <div role="group" aria-label="Buscar contenido por app" className="flex flex-wrap gap-2">
          {APP_CHIPS.map(chip => (
            <button
              key={chip.key}
              type="button"
              onClick={() => runSearch(chip.key)}
              aria-pressed={searchApp === chip.key}
              className={cn(
                "inline-flex min-h-8 items-center gap-1.5 rounded-fx-md border px-2.5 text-xs font-semibold",
                "transition-colors duration-fx-fast ease-fx",
                FOCUS_RING,
                searchApp === chip.key
                  ? "border-fx-accent bg-fx-accent-soft text-fx-accent-text"
                  : "border-fx-border bg-fx-surface-2 text-fx-text-2 hover:bg-fx-surface-3",
              )}
            >
              {/* contenido de imagen: logo de marca con sus colores */}
              <img src={chip.icon} alt="" width={14} height={14} className="h-3.5 w-3.5 shrink-0" />
              {chip.label}
            </button>
          ))}
        </div>
        {mode === "search" && (
          <p className="m-0 basis-full text-xs text-fx-text-3">
            {searchApp === "whatsapp"
              ? "Búsqueda por nombre de archivo en todo el dispositivo"
              : "Búsqueda por carpetas conocidas (cobertura limitada, la app guarda la mayoría del contenido en almacenamiento privado)"}
          </p>
        )}
      </div>

      {/* Atrás + migas (solo en modo carpetas) */}
      {mode === "browse" && (
        <nav aria-label="Ruta de la carpeta" className="flex shrink-0 items-center gap-2 px-4 py-2 text-xs">
          <FxTip label="Carpeta anterior">
            <Button type="button" text severity="secondary" size="small"
              icon={<ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />}
              aria-label="Carpeta anterior" disabled={navIndex === 0} onClick={goBack} />
          </FxTip>
          <ol className="m-0 flex min-w-0 list-none flex-wrap items-center gap-1 p-0">
            {crumbs.map((c, i) => {
              const last = i === crumbs.length - 1;
              return (
                <li key={c.target} className="flex items-center gap-1">
                  {i > 0 && <ChevronRight className="h-3 w-3 text-fx-text-3" aria-hidden="true" />}
                  {last ? (
                    <span aria-current="page" className="px-1 font-semibold text-fx-text">{c.label}</span>
                  ) : (
                    <button type="button" onClick={() => navigateTo(c.target)}
                      className={cn("rounded-fx-sm px-1 font-medium text-fx-text-2 hover:text-fx-text hover:underline", FOCUS_RING)}>
                      {c.label}
                    </button>
                  )}
                </li>
              );
            })}
          </ol>
        </nav>
      )}

      {/* Pista de la carpeta actual */}
      {currentHint && (
        <p className="m-0 mx-4 mb-2 flex shrink-0 items-center gap-1.5 rounded-fx-md border border-fx-info bg-fx-info-soft px-2.5 py-1.5 text-xs text-fx-text">
          <Lightbulb className="h-3.5 w-3.5 shrink-0 text-fx-info" aria-hidden="true" />
          {currentHint}
        </p>
      )}

      {/* Lista */}
      <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain">
        {loading && (
          <div role="status" className="flex h-full items-center justify-center gap-2 text-fx-body-sm text-fx-text-3">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> Cargando…
          </div>
        )}
        {!loading && error && (
          <div role="alert" className="flex h-full flex-col items-center justify-center gap-2 px-6 text-center">
            <AlertTriangle className="h-6 w-6 text-fx-danger" aria-hidden="true" />
            <p className="m-0 text-fx-body-sm font-medium text-fx-text-2">{error}</p>
            <Button type="button" text size="small" icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Reintentar" onClick={() => fetchDir(path)} />
          </div>
        )}
        {!loading && !error && entries.length === 0 && (
          <div className="flex h-full items-center justify-center px-6 text-center text-fx-body-sm text-fx-text-3">
            {mode === "search" ? "No se encontró contenido de esta app en el dispositivo" : "Carpeta vacía"}
          </div>
        )}
        {!loading && !error && entries.length > 0 && (
          <>
            {/* Encabezado de columnas + seleccionar todo */}
            <div className={cn(ROW, "sticky top-0 z-10 bg-fx-surface-2 text-fx-label uppercase text-fx-text-3")}>
              <span className="flex justify-center">
                <Checkbox inputId="explorer-select-all" checked={allSelected} onChange={toggleSelectAll}
                  disabled={selectableFiles.length === 0} icon={CHECK_ICON} />
                <label htmlFor="explorer-select-all" className="sr-only">
                  {allSelected ? "Deseleccionar todos" : "Seleccionar todos"}
                </label>
              </span>
              <span />
              <span>{mode === "search" ? "Ruta" : "Nombre"}</span>
              <span className="hidden sm:block">Tamaño</span>
              <span className="hidden sm:block">Modificado</span>
            </div>
            <ul aria-label={mode === "search" ? "Resultados de la búsqueda" : "Contenido de la carpeta"} className="m-0 list-none p-0">
              {visibleFiles.map((e, i) => {
                if (e.is_directory) {
                  const hint = folderHint(e.name);
                  return (
                    <li key={e.path}>
                      <button
                        type="button"
                        onClick={() => navigate(e)}
                        aria-label={`Abrir carpeta ${e.name}`}
                        className={cn(ROW, "w-full text-left transition-colors duration-fx-fast hover:bg-fx-surface-3", FOCUS_RING, "focus-visible:-outline-offset-2")}
                      >
                        <span />
                        <FolderClosed className="h-[18px] w-[18px] justify-self-center text-fx-text-2" fill="currentColor" fillOpacity={0.2} aria-hidden="true" />
                        <span className="min-w-0">
                          <span translate="no" className="block truncate text-xs font-medium text-fx-text">{mode === "search" ? e.path : e.name}</span>
                          {hint && <span className="block truncate text-[11px] text-fx-text-3">{hint}</span>}
                        </span>
                        <span className="hidden text-xs text-fx-text-3 sm:block">—</span>
                        <span className="hidden text-xs tabular-nums text-fx-text-3 sm:block">{humanDate(e.modified_at)}</span>
                      </button>
                    </li>
                  );
                }
                const kind = fileKind(e.name);
                const isSelected = selected.has(e.path);
                const id = `explorer-f-${i}`;
                return (
                  <li key={e.path} className={cn(ROW, "transition-colors duration-fx-fast", isSelected ? "bg-fx-accent-soft" : "hover:bg-fx-surface-3")}>
                    <span className="flex justify-center">
                      <Checkbox inputId={id} checked={isSelected} onChange={() => toggleSelect(e.path)}
                        aria-labelledby={`${id}-name`} icon={CHECK_ICON} />
                    </span>
                    <kind.icon className="h-[18px] w-[18px] justify-self-center text-fx-text-3" aria-hidden="true" />
                    <label htmlFor={id} id={`${id}-name`} translate="no" className="min-w-0 cursor-pointer truncate text-xs font-medium text-fx-text">
                      {mode === "search" ? e.path : e.name}
                    </label>
                    <span className="hidden text-xs tabular-nums text-fx-text-3 sm:block">{humanSize(e.size)}</span>
                    <span className="hidden text-xs tabular-nums text-fx-text-3 sm:block">{humanDate(e.modified_at)}</span>
                  </li>
                );
              })}
            </ul>
            {entries.length > visibleCount && (
              <div className="flex justify-center py-3">
                <Button type="button" text size="small" label={`Mostrar más (${entries.length - visibleCount} restantes)`}
                  onClick={() => setVisibleCount(v => v + PAGE_SIZE)} />
              </div>
            )}
          </>
        )}
      </div>

      {/* Botonera fija con el contador */}
      <div className="flex shrink-0 flex-wrap items-center justify-between gap-3 border-t border-fx-border bg-fx-surface-2 px-4 py-3">
        <div aria-live="polite" className="min-w-0">
          {selected.size > 0 ? (
            <p className="m-0 text-fx-body-sm font-semibold text-fx-text">
              {selected.size} seleccionado{selected.size > 1 ? "s" : ""}
            </p>
          ) : (
            <p className="m-0 text-xs text-fx-text-3">Seleccioná archivos para agregarlos a la evidencia</p>
          )}
          {addError && <p role="alert" className="m-0 mt-0.5 text-xs text-fx-danger">{addError}</p>}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Button type="button" text severity="secondary" size="small" label="Limpiar selección"
            disabled={selected.size === 0} onClick={() => setSelected(new Set())} className="min-h-11 sm:min-h-0" />
          <Button type="button" size="small" label={`Agregar ${selected.size} a evidencia`} loading={adding}
            disabled={selected.size === 0} onClick={handleAddToEvidence} className="min-h-11 sm:min-h-0" />
        </div>
      </div>
    </>
  );
}
