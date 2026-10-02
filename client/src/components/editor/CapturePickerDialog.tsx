"use client";

/**
 * Selector de capturas del caso y edición de la descripción
 * (editor-imagenes-informe, SDD §7.5). Un solo diálogo por `ReportStep`.
 * No importa Tiptap: recibe el listado y la caché por props.
 */

import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { Dialog } from "primereact/dialog";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { Tag } from "primereact/tag";
import { ImageOff, ImagePlus, Pencil, RotateCw } from "lucide-react";
import type { ReportImage } from "@/lib/api";
import { CAPTURE_ROLE_LABELS } from "@/lib/pericial";
import { MAX_REPORT_IMAGE_ALT, normalizeReportImageAlt } from "@/lib/report-markdown";
import { useNearViewport, usePreviewEntry, type ReportImagePreviewCache, type ReportImagesStatus } from "@/lib/report-images";
import { cn } from "@/lib/utils";

export interface CapturePickerProps {
  open: boolean;
  mode: "insert" | "edit";
  /** Modo `edit`: la captura y la descripción actuales. */
  initialFilename?: string;
  initialAlt?: string;
  images: ReportImage[] | null;
  imagesStatus: ReportImagesStatus;
  onReload: () => void;
  cache: ReportImagePreviewCache;
  onConfirm: (value: { filename: string; alt: string }) => void;
  /** Se cierra sin confirmar (Cancelar, Escape, ✕). */
  onCancel: () => void;
}

const fmt = new Intl.NumberFormat("es-AR");
/** Chip sobre la miniatura: en 3 columnas (360 px) el texto se parte en vez de cortarse. */
const CHIP = "max-w-full whitespace-normal text-center leading-tight";

/** Epígrafe tal como va a salir en el DOCX (D10), con "N" literal. */
function captionPreview(img: ReportImage | undefined, filename: string, alt: string): string {
  const suf = alt ? `${alt} (${filename})` : filename;
  if (img?.role === "imei_modelo") return `Captura de identificación (IMEI y modelo) – ${suf}`;
  if (img?.role === "nombre_dispositivo") return `Captura de identificación (nombre del dispositivo) – ${suf}`;
  return `Figura N – ${suf}`;
}

export function CapturePickerDialog(props: CapturePickerProps) {
  const { open, mode, onCancel } = props;
  return (
    <Dialog
      visible={open}
      onHide={onCancel}
      modal
      dismissableMask={false}
      closeOnEscape
      // El foco inicial lo pone el cuerpo (grilla o descripción), no Prime.
      focusOnShow={false}
      draggable={false}
      resizable={false}
      header={
        <span className="flex items-center gap-2 text-fx-body font-bold">
          {mode === "edit"
            ? <Pencil className="h-4 w-4 text-fx-text-3" aria-hidden="true" />
            : <ImagePlus className="h-4 w-4 text-fx-text-3" aria-hidden="true" />}
          {mode === "edit" ? "Editar descripción" : "Insertar captura del caso"}
        </span>
      }
      pt={{ root: { className: "w-[min(44rem,100%)]" } }}
    >
      {/* El cuerpo se monta con cada apertura: el estado arranca limpio. */}
      {open && <PickerBody {...props} />}
    </Dialog>
  );
}

function PickerBody({
  mode, initialFilename, initialAlt, images, imagesStatus, onReload, cache, onConfirm, onCancel,
}: CapturePickerProps) {
  const uid = useId();
  const formId = `${uid}-form`;
  const altId = `${uid}-alt`;
  const countId = `${uid}-count`;
  const previewId = `${uid}-preview`;
  const listRef = useRef<HTMLDivElement>(null);
  const altRef = useRef<HTMLInputElement>(null);

  const [selected, setSelected] = useState<string | null>(mode === "edit" ? initialFilename ?? null : null);
  const [alt, setAlt] = useState(initialAlt ?? "");
  const list = images ?? [];
  const firstEnabled = list.find(i => i.available)?.filename ?? null;
  // Parada de tabulación de la grilla: la seleccionada, o la primera disponible.
  const [active, setActive] = useState<string | null>(null);
  const tabStop = active ?? selected ?? firstEnabled;

  // Foco inicial: la grilla (insertar) o la descripción (editar). Se reintenta
  // hasta que llegue el listado, porque la grilla aparece después.
  const focused = useRef(false);
  useEffect(() => {
    if (focused.current) return;
    if (mode === "edit") {
      altRef.current?.focus();
      focused.current = true;
      return;
    }
    if (imagesStatus === "loading") return;
    const el = tabStop ? listRef.current?.querySelector<HTMLElement>(`[data-file="${CSS.escape(tabStop)}"]`) : null;
    (el ?? altRef.current)?.focus();
    focused.current = true;
  }, [mode, imagesStatus, tabStop]);

  const selectedImage = list.find(i => i.filename === selected);
  const cleanAlt = normalizeReportImageAlt(alt);
  const canConfirm = !!selected && (mode === "edit" || !!selectedImage?.available);

  function submit(e?: FormEvent) {
    e?.preventDefault();
    if (!canConfirm || !selected) return;
    onConfirm({ filename: selected, alt: cleanAlt });
  }

  /* ── Teclado de la grilla: flechas en 2D, Inicio/Fin, Enter/Espacio ── */
  function onGridKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    const options = Array.from(listRef.current?.querySelectorAll<HTMLElement>("[role=option]") ?? []);
    const idx = options.indexOf(document.activeElement as HTMLElement);
    if (idx < 0) return;
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      const file = options[idx].dataset.file!;
      if (options[idx].getAttribute("aria-disabled") !== "true") setSelected(file);
      return;
    }
    // Columnas = opciones en la primera fila (misma posición vertical).
    const top0 = options[0]?.offsetTop ?? 0;
    const cols = Math.max(1, options.filter(o => o.offsetTop === top0).length);
    const next =
      e.key === "ArrowRight" ? Math.min(idx + 1, options.length - 1)
      : e.key === "ArrowLeft" ? Math.max(idx - 1, 0)
      : e.key === "ArrowDown" ? Math.min(idx + cols, options.length - 1)
      : e.key === "ArrowUp" ? Math.max(idx - cols, 0)
      : e.key === "Home" ? 0
      : e.key === "End" ? options.length - 1
      : -1;
    if (next < 0) return;
    e.preventDefault();
    setActive(options[next].dataset.file ?? null);
    options[next].focus();
  }

  return (
    <>
      <form id={formId} onSubmit={submit} className="space-y-4">
        {mode === "edit" ? (
          <div className="flex items-center gap-3 rounded-fx-md border border-fx-border bg-fx-surface-2 p-2">
            <div className="w-16 shrink-0">
              <Thumb cache={cache} filename={selected ?? ""} available={selectedImage?.available ?? true} />
            </div>
            <p translate="no" className="m-0 min-w-0 break-all text-fx-body-sm font-medium text-fx-text">{selected}</p>
          </div>
        ) : imagesStatus === "loading" ? (
          <div role="status" aria-label="Cargando las capturas" className="grid grid-cols-3 gap-3 sm:grid-cols-4">
            {Array.from({ length: 8 }, (_, i) => (
              <div key={i} aria-hidden="true" className="space-y-1.5">
                <div className="aspect-[3/4] rounded-fx-md bg-fx-surface-3 motion-safe:animate-pulse" />
                <div className="h-3 w-3/4 rounded-fx-sm bg-fx-surface-3 motion-safe:animate-pulse" />
              </div>
            ))}
          </div>
        ) : imagesStatus === "error" ? (
          <div role="alert" className="flex flex-col items-start gap-2 rounded-fx-md border border-fx-border bg-fx-surface-2 p-4">
            <p className="m-0 text-fx-body-sm text-fx-text">No se pudieron cargar las capturas</p>
            <Button
              type="button"
              size="small"
              severity="secondary"
              icon={<RotateCw className="h-3.5 w-3.5" aria-hidden="true" />}
              label="Reintentar"
              onClick={onReload}
            />
          </div>
        ) : list.length === 0 ? (
          <div className="flex flex-col items-center gap-2 rounded-fx-md border border-dashed border-fx-border-strong bg-fx-surface-2 px-4 py-8 text-center">
            <ImageOff className="h-6 w-6 text-fx-text-3" aria-hidden="true" />
            <p className="m-0 max-w-sm text-fx-body-sm text-fx-text-2">
              Este caso no tiene capturas. Las capturas se toman en el paso 3.
            </p>
          </div>
        ) : (
          <div
            ref={listRef}
            role="listbox"
            aria-label="Capturas del caso"
            onKeyDown={onGridKeyDown}
            className="grid max-h-[min(24rem,50vh)] grid-cols-3 gap-3 overflow-y-auto overscroll-contain p-1 sm:grid-cols-4"
          >
            {list.map(img => {
              const isSel = img.filename === selected;
              return (
                <div
                  key={img.filename}
                  role="option"
                  data-file={img.filename}
                  aria-selected={isSel}
                  aria-disabled={!img.available || undefined}
                  tabIndex={img.filename === tabStop ? 0 : -1}
                  title={img.filename}
                  onFocus={() => setActive(img.filename)}
                  onClick={() => { if (img.available) setSelected(img.filename); }}
                  className={cn(
                    "group min-w-0 rounded-fx-md p-1 outline-none transition-colors duration-fx-fast ease-fx",
                    "focus-visible:ring-2 focus-visible:ring-fx-focus",
                    img.available ? "cursor-pointer hover:bg-fx-surface-2" : "cursor-not-allowed",
                    isSel && "bg-fx-accent-soft hover:bg-fx-accent-soft",
                  )}
                >
                  <div
                    className={cn(
                      "relative rounded-fx-md",
                      isSel ? "ring-2 ring-fx-accent" : "ring-1 ring-fx-border",
                    )}
                  >
                    <Thumb cache={cache} filename={img.filename} available={img.available} />
                    {(img.role || !img.available) && (
                      <span className="absolute inset-x-1 bottom-1 flex flex-wrap gap-1">
                        {img.role && <Tag severity="info" value={CAPTURE_ROLE_LABELS[img.role]} className={CHIP} />}
                        {!img.available && <Tag severity="secondary" value="No disponible" className={CHIP} />}
                      </span>
                    )}
                  </div>
                  <p translate="no" className={cn("m-0 mt-1.5 truncate text-xs", img.available ? "text-fx-text-2" : "text-fx-text-3")}>
                    {img.filename}
                  </p>
                </div>
              );
            })}
          </div>
        )}

        <div>
          <label htmlFor={altId} className="mb-1.5 block text-fx-body-sm font-medium text-fx-text">
            Descripción (epígrafe) <span className="font-normal text-fx-text-3">· opcional</span>
          </label>
          <InputText
            id={altId}
            ref={altRef}
            value={alt}
            maxLength={MAX_REPORT_IMAGE_ALT}
            autoComplete="off"
            aria-describedby={`${countId} ${previewId}`}
            placeholder="Por ejemplo: Chat con Juan…"
            onChange={e => setAlt(e.target.value.replace(/[\r\n]+/g, " "))}
            className="w-full"
          />
          <div className="mt-1.5 flex items-start gap-3">
            <p id={previewId} aria-live="polite" className="m-0 min-w-0 flex-1 break-words text-xs italic text-fx-text-3">
              {selected
                ? <>Se verá como: “{captionPreview(selectedImage, selected, cleanAlt)}”</>
                : "Elegí una captura para ver cómo queda el epígrafe."}
            </p>
            <p id={countId} className="m-0 shrink-0 text-xs tabular-nums text-fx-text-3">
              <span className="sr-only">Caracteres: </span>
              {fmt.format(alt.length)} / {fmt.format(MAX_REPORT_IMAGE_ALT)}
            </p>
          </div>
        </div>
      </form>

      <div className="mt-5 flex justify-end gap-2">
        <Button type="button" severity="secondary" label="Cancelar" onClick={onCancel} />
        <Button
          type="submit"
          form={formId}
          label={mode === "edit" ? "Guardar" : "Insertar"}
          disabled={!canConfirm}
        />
      </div>
    </>
  );
}

/** Miniatura con carga perezosa (entra al viewport → se pide la vista previa una sola vez). */
function Thumb({ cache, filename, available }: { cache: ReportImagePreviewCache; filename: string; available: boolean }) {
  const entry = usePreviewEntry(cache, filename);
  const [observe, visible] = useNearViewport<HTMLDivElement>();
  useEffect(() => {
    if (available && visible && filename && entry.status === "idle") cache.load(filename);
  }, [available, visible, filename, entry.status, cache]);

  return (
    <div ref={observe} className="flex aspect-[3/4] items-center justify-center overflow-hidden rounded-fx-md bg-fx-surface-3">
      {entry.status === "ready" && entry.url ? (
        // eslint-disable-next-line @next/next/no-img-element -- blob URL de la evidencia
        <img src={entry.url} alt="" draggable={false} className="h-full w-full object-contain" />
      ) : !available || entry.status === "unavailable" ? (
        <ImageOff className="h-5 w-5 text-fx-text-3" aria-hidden="true" />
      ) : entry.status === "error" ? (
        <ImageOff className="h-5 w-5 text-fx-text-3" aria-hidden="true" />
      ) : (
        <div aria-hidden="true" className="h-full w-full bg-fx-surface-3 motion-safe:animate-pulse" />
      )}
    </div>
  );
}
