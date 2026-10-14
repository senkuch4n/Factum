"use client";

import { Button } from "primereact/button";
import { Dialog } from "primereact/dialog";
import { RefreshCw } from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";
import type { DeviceDetailsUi } from "@/lib/device-details";
import { DeviceDetailsPanel } from "./DeviceDetailsPanel";

/**
 * Ficha técnica completa del paso 2 (D-T11: la columna lateral tiene alto fijo
 * sin scroll, así que la ficha va en un diálogo). Foco inicial y `Escape` como
 * los demás `Dialog` del repo. "Volver a leer" solo si el caso es editable (o
 * nuevo), con el mismo dispositivo conectado (D11).
 */
export function DeviceDetailsDialog({ visible, onHide, ui }: {
  visible: boolean;
  onHide: () => void;
  ui: DeviceDetailsUi;
}) {
  const loading = ui.status === "loading";
  const footer = (
    <div className="flex flex-wrap justify-end gap-2">
      {ui.canReread && (
        <Button
          type="button"
          severity="secondary"
          outlined
          label={loading ? "Leyendo…" : "Volver a leer"}
          icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
          loading={loading}
          disabled={loading}
          onClick={ui.onReread}
          className="min-h-11"
        />
      )}
      <Button type="button" label="Cerrar" onClick={onHide} className="min-h-11" />
    </div>
  );

  return (
    <Dialog
      visible={visible}
      onHide={onHide}
      header="Datos técnicos del dispositivo"
      footer={footer}
      draggable={false}
      resizable={false}
      dismissableMask
      // Ancho por pt (el className de props pierde contra el pt global; patrón de DeviceFileExplorer).
      pt={{ root: { className: "w-[min(40rem,100%)]" } }}
    >
      <div aria-busy={loading || undefined} className="space-y-4">
        <p className="m-0 text-xs text-fx-text-2">
          Leídos por Tatana con consultas de solo lectura: no se modificó nada en el dispositivo.
        </p>
        {ui.status === "error" && ui.error && <FxBanner tone="warn" role="alert">{ui.error}</FxBanner>}
        {loading && (
          <p role="status" className="m-0 text-xs text-fx-text-2">Leyendo datos del dispositivo…</p>
        )}
        {ui.view ? (
          <DeviceDetailsPanel
            view={ui.view}
            thresholdSeconds={ui.thresholdSeconds}
            onIncludeSsidChange={ui.onIncludeSsidChange}
            ssidSaving={ui.ssidSaving}
          />
        ) : (
          !loading && ui.status !== "error" && (
            <p className="m-0 text-fx-text-3">Todavía no hay datos técnicos leídos.</p>
          )
        )}
      </div>
    </Dialog>
  );
}
