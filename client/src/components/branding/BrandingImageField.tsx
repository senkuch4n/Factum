"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import { AlertCircle, ImageIcon, Loader2, RefreshCw, Trash2, Undo2, Upload } from "lucide-react";
import { api } from "@/lib/api";
import { KEEP_IMAGE, formatBytes, inspectImageFile, type BrandingImageValue } from "@/lib/branding";
import { cn } from "@/lib/utils";
import type { BrandingImage } from "@/types";

/**
 * Object URL de una imagen de marca guardada (`url` del DTO), pedida con
 * `Authorization` (blob). Se revoca al cambiar de imagen o desmontar.
 */
export function useBrandingImageSrc(image: BrandingImage | null): { src: string | null; failed: boolean } {
  const [state, setState] = useState<{ url: string | null; src: string | null; failed: boolean }>(
    { url: null, src: null, failed: false },
  );
  const imageUrl = image?.url ?? null;

  useEffect(() => {
    if (!imageUrl) return;
    const ctrl = new AbortController();
    let created: string | null = null;
    api.getBrandingImage(imageUrl, ctrl.signal)
      .then(blob => {
        created = URL.createObjectURL(blob);
        setState({ url: imageUrl, src: created, failed: false });
      })
      .catch(() => { if (!ctrl.signal.aborted) setState({ url: imageUrl, src: null, failed: true }); });
    return () => {
      ctrl.abort();
      if (created) URL.revokeObjectURL(created);
    };
  }, [imageUrl]);

  // Solo vale lo que corresponde a la imagen pedida ahora (nunca la anterior).
  if (!imageUrl || state.url !== imageUrl) return { src: null, failed: false };
  return { src: state.src, failed: state.failed };
}

interface Props {
  /** id del control principal (el foco del error va acá). */
  id: string;
  label: string;
  /** Texto para el `alt` de la miniatura ("Logo", "Isotipo"). */
  noun: string;
  /** Imagen guardada (o `null`). */
  current: BrandingImage | null;
  /** Object URL de la imagen guardada (lo trae el formulario, compartido con la vista previa). */
  currentSrc: string | null;
  currentLoading: boolean;
  value: BrandingImageValue;
  onChange: (value: BrandingImageValue) => void;
  error?: string;
  /** Error de validación local (o `null` para limpiarlo). */
  onError: (message: string | null) => void;
  hint: string;
  disabled?: boolean;
}

/**
 * Logo o isotipo: zona de arrastrar y soltar + "Elegir archivo"
 * (marca-por-cliente §9.4). Todo se opera con teclado: la zona vacía es un
 * botón (Enter/Espacio abren el selector) y con imagen hay "Reemplazar" y
 * "Quitar". El archivo se valida en el navegador (peso, firma, dimensiones)
 * antes de aceptarlo; si no es válido, se muestra el error y no cambia nada.
 */
export function BrandingImageField({
  id, label, noun, current, currentSrc, currentLoading, value, onChange, error, onError, hint, disabled,
}: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [checking, setChecking] = useState(false);
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [hintId, error ? errorId : null].filter(Boolean).join(" ");

  const shown: { src: string | null; width?: number; height?: number; size?: number } | null =
    value.action === "replace"
      ? { src: value.previewUrl, width: value.width, height: value.height, size: value.size }
      : value.action === "keep" && current
        ? { src: currentSrc, width: current.width, height: current.height, size: current.size }
        : null;
  const removing = value.action === "remove";

  function openPicker() {
    if (!disabled && !checking) inputRef.current?.click();
  }

  function setValue(next: BrandingImageValue) {
    if (value.previewUrl && value.previewUrl !== next.previewUrl) URL.revokeObjectURL(value.previewUrl);
    onChange(next);
  }

  async function accept(file: File | undefined) {
    if (!file || disabled) return;
    setChecking(true);
    const r = await inspectImageFile(file);
    setChecking(false);
    if ("message" in r) { onError(r.message); return; }
    onError(null);
    setValue({
      action: "replace", file, previewUrl: URL.createObjectURL(file), width: r.width, height: r.height, size: file.size,
    });
  }

  function remove() {
    onError(null);
    // Sin imagen guardada, quitar el archivo nuevo es volver a "sin cambios".
    setValue(current ? { action: "remove", file: null, previewUrl: null } : KEEP_IMAGE);
    requestAnimationFrame(() => document.getElementById(id)?.focus());
  }

  function restore() {
    onError(null);
    setValue(KEEP_IMAGE);
    requestAnimationFrame(() => document.getElementById(id)?.focus());
  }

  const dropProps = {
    onDragEnter: (e: React.DragEvent) => { e.preventDefault(); if (!disabled) setDragging(true); },
    onDragOver: (e: React.DragEvent) => { e.preventDefault(); e.dataTransfer.dropEffect = disabled ? "none" : "copy"; },
    onDragLeave: (e: React.DragEvent) => {
      if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDragging(false);
    },
    onDrop: (e: React.DragEvent) => {
      e.preventDefault();
      setDragging(false);
      void accept(e.dataTransfer.files?.[0]);
    },
  };

  const zoneBase = cn(
    "rounded-fx-lg border transition-colors duration-fx-fast ease-fx",
    dragging ? "border-fx-accent bg-fx-accent-soft" : error ? "border-fx-danger" : "border-fx-border-strong",
  );

  return (
    <div>
      <p className="m-0 mb-1.5 flex items-start gap-1.5">
        <ImageIcon className="mt-px h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
        <span id={`${id}-label`} className="text-fx-label uppercase text-fx-text-2">{label}</span>
        <span className="ml-1.5 text-xs text-fx-text-3">(opcional)</span>
      </p>

      <input
        ref={inputRef}
        type="file"
        accept="image/png,image/jpeg"
        tabIndex={-1}
        aria-hidden="true"
        className="sr-only"
        onChange={e => { void accept(e.target.files?.[0]); e.target.value = ""; }}
      />

      {shown ? (
        <div {...dropProps} className={cn(zoneBase, "flex flex-wrap items-center gap-3 p-3")}>
          {/* Fondo blanco fijo: el del informe, en los dos temas. */}
          <div className="flex h-16 w-28 shrink-0 items-center justify-center overflow-hidden rounded-fx-md border border-fx-border bg-white p-1.5">
            {shown.src ? (
              <img
                src={shown.src}
                alt={`${noun} de la marca`}
                width={shown.width}
                height={shown.height}
                className="h-auto max-h-full w-auto max-w-full object-contain"
              />
            ) : currentLoading ? (
              <Loader2 className="h-4 w-4 text-[#5c656e] motion-safe:animate-spin" aria-label={`Cargando ${noun.toLowerCase()}`} />
            ) : (
              <ImageIcon className="h-5 w-5 text-[#5c656e]" aria-label="No se pudo mostrar la imagen" />
            )}
          </div>
          <div className="min-w-0 flex-1">
            <p className="m-0 text-xs tabular-nums text-fx-text-2">
              {shown.width && shown.height ? `${shown.width}×${shown.height} px` : ""}
              {shown.size ? ` · ${formatBytes(shown.size)}` : ""}
            </p>
            {value.action === "replace" && (
              <p className="m-0 mt-0.5 text-xs text-fx-text-3">Nueva, se sube al guardar.</p>
            )}
            <div className="mt-2 flex flex-wrap gap-2">
              <Button
                id={id}
                type="button"
                size="small"
                severity="secondary"
                label={checking ? "Revisando…" : "Reemplazar"}
                icon={<RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />}
                aria-describedby={describedBy}
                aria-invalid={!!error || undefined}
                disabled={disabled || checking}
                onClick={openPicker}
                className="max-md:min-h-11"
              />
              <Button
                type="button"
                size="small"
                severity="secondary"
                text
                label="Quitar"
                aria-label={`Quitar ${noun.toLowerCase()}`}
                icon={<Trash2 className="h-3.5 w-3.5" aria-hidden="true" />}
                disabled={disabled || checking}
                onClick={remove}
                className="max-md:min-h-11"
              />
            </div>
          </div>
        </div>
      ) : (
        <div {...dropProps} className={cn(zoneBase, "border-dashed")}>
          {removing && (
            <div className="flex flex-wrap items-center justify-between gap-2 border-b border-dashed border-fx-border px-3 py-2">
              <p className="m-0 text-xs text-fx-text-2">Se va a quitar al guardar.</p>
              <Button
                type="button"
                size="small"
                severity="secondary"
                text
                label="Restaurar"
                aria-label={`Restaurar ${noun.toLowerCase()}`}
                icon={<Undo2 className="h-3.5 w-3.5" aria-hidden="true" />}
                disabled={disabled}
                onClick={restore}
                className="max-md:min-h-11"
              />
            </div>
          )}
          <button
            id={id}
            type="button"
            onClick={openPicker}
            disabled={disabled || checking}
            aria-describedby={describedBy}
            aria-invalid={!!error || undefined}
            aria-labelledby={`${id}-label ${id}-cta`}
            className="flex min-h-24 w-full touch-manipulation flex-col items-center justify-center gap-1.5 rounded-fx-lg px-4 py-4 text-center transition-colors duration-fx-fast ease-fx enabled:hover:bg-fx-surface-2 fx-focus-ring disabled:cursor-not-allowed disabled:opacity-60"
          >
            {checking
              ? <Loader2 className="h-5 w-5 text-fx-text-3 motion-safe:animate-spin" aria-hidden="true" />
              : <Upload className="h-5 w-5 text-fx-text-3" aria-hidden="true" />}
            <span id={`${id}-cta`} className="text-fx-body-sm font-semibold text-fx-text">
              {checking ? "Revisando la imagen…" : "Elegir archivo"}
            </span>
            <span className="text-xs text-fx-text-3" aria-hidden="true">o arrastralo hasta acá</span>
          </button>
        </div>
      )}

      <p id={hintId} className="m-0 mt-1.5 text-xs leading-snug text-fx-text-3">{hint}</p>
      {error && (
        <p id={errorId} role="alert" className="m-0 mt-1.5 flex items-center gap-1 text-xs font-medium text-fx-danger">
          <AlertCircle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {error}
        </p>
      )}
    </div>
  );
}
