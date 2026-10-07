"use client";

import { useEffect, type CSSProperties, type MouseEvent } from "react";
import { NodeViewWrapper, type ReactNodeViewProps } from "@tiptap/react";
import { Button } from "primereact/button";
import { ImageOff, Pencil, RotateCw, Trash2, TriangleAlert } from "lucide-react";
import { FxTip } from "@/components/overlay/FxTip";
import { useNearViewport, usePreviewEntry } from "@/lib/report-images";
import { captionPrefix, captionText, useReportImagesContext } from "./ReportImagesContext";

type ViewState = "loading" | "ready" | "unavailable" | "error";

const FIGURE_TIP = "El número se asigna al generar el informe, igual que en el anexo";
/** Alto máximo de la vista previa: una captura vertical de celular no ocupa toda la pantalla. */
const MAX_PREVIEW_H = "22rem";

/** No le saca la selección al editor al tocar un botón del bloque. */
const keepSelection = (e: MouseEvent) => e.preventDefault();

/**
 * NodeView de una captura dentro del texto (SDD §7.3): vista previa, epígrafe
 * provisorio y barra flotante. La imagen nunca es editable: es un átomo.
 */
export function ReportImageView({ node, selected, deleteNode, editor, getPos }: ReactNodeViewProps) {
  const filename = String(node.attrs.filename ?? "");
  const alt = String(node.attrs.alt ?? "");
  const ctx = useReportImagesContext();
  const info = ctx?.images?.find(i => i.filename === filename);
  const entry = usePreviewEntry(ctx?.cache, filename);
  const [observe, visible] = useNearViewport<HTMLDivElement>();

  // Disponible según el listado (o el listado falló y decide la vista previa).
  const listSaysAvailable = ctx?.imagesStatus === "ready" ? !!info?.available : ctx?.imagesStatus === "error";
  const canLoad = !!ctx && listSaysAvailable;

  useEffect(() => {
    if (ctx && canLoad && visible && entry.status === "idle") ctx.cache.load(filename);
  }, [ctx, canLoad, visible, entry.status, filename]);

  const state: ViewState =
    !ctx ? "unavailable"
    : ctx.imagesStatus === "loading" ? "loading"
    : !listSaysAvailable ? "unavailable"
    : entry.status === "ready" ? "ready"
    : entry.status === "unavailable" ? "unavailable"
    : entry.status === "error" ? "error"
    : "loading";

  const prefix = captionPrefix(info?.role);
  const label = `${alt || "Sin descripción"} (${filename})`;

  const editAlt = () => {
    const pos = getPos();
    if (typeof pos === "number") ctx?.openPicker({ editor, pos });
  };

  // Caja de la imagen con la proporción del listado: reserva el lugar mientras
  // carga (sin saltos) y la vista previa la llena con object-fit: contain.
  const w = info?.width || 9;
  const h = info?.height || 16;
  const landscape = w >= h;
  const box: CSSProperties = landscape
    ? { aspectRatio: `${w} / ${h}`, width: "100%" }
    : { aspectRatio: `${w} / ${h}`, height: MAX_PREVIEW_H, maxWidth: "100%" };
  const media = state === "ready" || state === "loading";

  return (
    <NodeViewWrapper
      as="figure"
      contentEditable={false}
      data-fx-report-image={filename}
      data-selected={selected ? "true" : undefined}
      className="fx-rimg"
    >
      <div ref={observe} className={media ? "fx-rimg-body fx-rimg-body--media" : "fx-rimg-body fx-rimg-body--note"}>
        <div className="fx-rimg-inner" style={landscape || !media ? { width: "100%" } : undefined}>
          {state === "ready" && entry.url ? (
            <div role="img" aria-label={label} className="fx-rimg-frame" style={box}>
              {/* eslint-disable-next-line @next/next/no-img-element -- blob URL de la evidencia, sin optimizar */}
              <img src={entry.url} alt="" draggable={false} />
            </div>
          ) : state === "loading" ? (
            <div className="fx-rimg-frame fx-rimg-skeleton motion-safe:animate-pulse" style={box}>
              <span className="sr-only">Cargando la vista previa de {filename}</span>
            </div>
          ) : state === "unavailable" ? (
            <div className="fx-rimg-missing">
              <ImageOff className="h-5 w-5 shrink-0 text-fx-text-3" aria-hidden="true" />
              <p className="m-0 min-w-0 text-fx-body-sm text-fx-text-2">
                Imagen no disponible: <span translate="no" className="break-all font-medium text-fx-text">{filename}</span>
              </p>
              <Button
                type="button"
                size="small"
                severity="secondary"
                icon={<Trash2 className="h-3.5 w-3.5" aria-hidden="true" />}
                label="Quitar"
                aria-label={`Quitar la imagen ${filename}`}
                onMouseDown={keepSelection}
                onClick={() => deleteNode()}
              />
            </div>
          ) : (
            <div className="fx-rimg-missing">
              <TriangleAlert className="h-5 w-5 shrink-0 text-fx-warning" aria-hidden="true" />
              <p className="m-0 min-w-0 text-fx-body-sm text-fx-text-2">No se pudo cargar la vista previa</p>
              <Button
                type="button"
                size="small"
                severity="secondary"
                icon={<RotateCw className="h-3.5 w-3.5" aria-hidden="true" />}
                label="Reintentar"
                aria-label={`Reintentar la vista previa de ${filename}`}
                onMouseDown={keepSelection}
                onClick={() => ctx?.cache.retry(filename)}
              />
            </div>
          )}

          {selected && ctx && (
            <div role="toolbar" aria-label="Acciones de la imagen" className="fx-rimg-bar">
              <FxTip label="Editar descripción" side="top">
                <Button
                  type="button"
                  text
                  size="small"
                  severity="secondary"
                  icon={<Pencil className="h-4 w-4" aria-hidden="true" />}
                  aria-label="Editar descripción"
                  aria-keyshortcuts="Enter"
                  onMouseDown={keepSelection}
                  onClick={editAlt}
                />
              </FxTip>
              <FxTip label="Quitar" side="top">
                <Button
                  type="button"
                  text
                  size="small"
                  severity="secondary"
                  icon={<Trash2 className="h-4 w-4" aria-hidden="true" />}
                  aria-label="Quitar imagen"
                  aria-keyshortcuts="Delete"
                  onMouseDown={keepSelection}
                  onClick={() => deleteNode()}
                />
              </FxTip>
            </div>
          )}
        </div>
      </div>

      <figcaption className="fx-rimg-caption">
        {prefix === "Figura" ? (
          <FxTip label={FIGURE_TIP} side="bottom">
            <span className="cursor-help underline decoration-dotted underline-offset-2">Figura</span>
          </FxTip>
        ) : (
          <span>{prefix}</span>
        )}
        {" · "}
        {captionText(alt, filename)}
        {prefix === "Figura" && <span className="sr-only">. {FIGURE_TIP}.</span>}
      </figcaption>
    </NodeViewWrapper>
  );
}
