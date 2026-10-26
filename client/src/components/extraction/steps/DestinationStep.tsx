"use client";

import { useCallback, useEffect, useState } from "react";
import { Button } from "primereact/button";
import { AlertTriangle, ArrowLeft, Check, FolderClosed, Loader2, RefreshCw, ShieldCheck } from "lucide-react";
import type { AgentDestinationCheck, AgentDestinations, AgentDrive } from "@/lib/agent";
import { StepHeader } from "@/components/wizard/StepHeader";
import { FxBanner } from "@/components/feedback/FxBanner";
import { DRIVE_KIND_LABEL, ProgressBar, driveIcon } from "@/components/extraction/common";
import { formatBytes } from "@/lib/format";
import { cn } from "@/lib/utils";
import { agent, type AgentDestinationBrowse } from "@/lib/agent";

interface Props {
  destinations: AgentDestinations | null;
  loading: boolean;
  onRefresh: () => void;
  destPath: string | null;
  onPick: (path: string) => void;
  destCheck: AgentDestinationCheck | null;
  checkLoading: boolean;
}

/** Paso 3 (§10.4.3): elegir la unidad y la carpeta de destino. */
export function DestinationStep({ destinations, loading, onRefresh, destPath, onPick, destCheck, checkLoading }: Props) {
  const [browse, setBrowse] = useState<AgentDestinationBrowse | null>(null);
  const [browseLoading, setBrowseLoading] = useState(false);
  const [browseError, setBrowseError] = useState<string | null>(null);

  const go = useCallback((path: string) => {
    onPick(path);
    setBrowseLoading(true);
    setBrowseError(null);
    agent.browseDestination(path)
      .then(setBrowse)
      .catch(() => setBrowseError("No se pudo abrir la carpeta."))
      .finally(() => setBrowseLoading(false));
  }, [onPick]);

  // Si ya había una ruta elegida (volver al paso), restaura el navegador.
  useEffect(() => {
    if (destPath && !browse && !browseLoading) go(destPath);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="space-y-6">
      <StepHeader
        title="Destino"
        description="Elegí dónde guardar la extracción. Usá un disco con espacio de sobra."
      />

      {destinations?.restricted && (
        <FxBanner tone="info" role="note">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span>
              Solo se muestran la carpeta de evidencia y los discos USB conectados. Si enchufaste un disco recién, tocá
              Actualizar.
            </span>
            <Button
              text size="small" icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
              label="Actualizar" onClick={onRefresh} className="min-h-11"
            />
          </div>
        </FxBanner>
      )}

      {loading ? (
        <div role="status" className="flex justify-center py-10">
          <Loader2 className="h-6 w-6 animate-spin text-fx-text-3" aria-hidden="true" />
          <span className="sr-only">Buscando unidades…</span>
        </div>
      ) : !destinations ? (
        <FxBanner tone="error">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span>No se pudieron leer las unidades de esta PC.</span>
            <Button text size="small" label="Reintentar" onClick={onRefresh} className="min-h-11" />
          </div>
        </FxBanner>
      ) : (
        <>
          {/* Unidades */}
          <section aria-labelledby="dest-drives" className="space-y-2">
            <div className="flex items-center justify-between gap-2">
              <h3 id="dest-drives" className="text-fx-label uppercase text-fx-text-2">Unidades</h3>
              <Button
                text size="small" icon={<RefreshCw className="h-4 w-4" aria-hidden="true" />}
                label="Actualizar" onClick={onRefresh} className="min-h-11"
              />
            </div>
            {destinations.default_path && (
              <DriveCard
                selected={destPath === destinations.default_path}
                onClick={() => go(destinations.default_path)}
                title="Carpeta de Factum (sugerida)"
                subtitle={destinations.default_path}
                icon={<FolderClosed className="h-5 w-5 text-fx-text-2" aria-hidden="true" />}
              />
            )}
            <ul className="m-0 list-none space-y-2 p-0">
              {destinations.drives.map(d => (
                <li key={d.root_path}>
                  <DriveRow drive={d} selected={destPath === d.root_path || (destPath?.startsWith(d.root_path) ?? false)} onClick={() => go(d.root_path)} />
                </li>
              ))}
            </ul>
          </section>

          {/* Navegador de carpetas */}
          {browse && (
            <section aria-labelledby="dest-browser" className="space-y-2">
              <h3 id="dest-browser" className="text-fx-label uppercase text-fx-text-2">Carpeta</h3>
              <div className="rounded-fx-md border border-fx-border bg-fx-surface-2 p-3">
                <div className="flex items-center gap-2">
                  <Button
                    type="button" text size="small"
                    icon={<ArrowLeft className="h-4 w-4" aria-hidden="true" />}
                    label="Subir" disabled={!browse.parent}
                    onClick={() => { if (browse.parent) go(browse.parent); }}
                    className="min-h-11"
                  />
                  <p className="m-0 min-w-0 truncate font-mono text-xs text-fx-text-3" translate="no">{browse.path}</p>
                </div>

                {browseLoading ? (
                  <div role="status" className="flex justify-center py-6">
                    <Loader2 className="h-5 w-5 animate-spin text-fx-text-3" aria-hidden="true" />
                    <span className="sr-only">Cargando…</span>
                  </div>
                ) : browseError ? (
                  <p className="m-0 mt-2 flex items-center gap-2 text-fx-body-sm text-fx-danger">
                    <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />{browseError}
                  </p>
                ) : browse.entries.length === 0 ? (
                  <p className="m-0 mt-2 py-4 text-center text-fx-body-sm text-fx-text-2">
                    Esta carpeta no tiene subcarpetas. La extracción se guardará acá.
                  </p>
                ) : (
                  <ul className="m-0 mt-2 max-h-60 list-none space-y-1 overflow-y-auto p-0">
                    {browse.entries.map(e => (
                      <li key={e.path}>
                        <button
                          type="button"
                          onClick={() => go(e.path)}
                          className="flex min-h-11 w-full items-center gap-2 rounded-fx-md px-2 py-2 text-left hover:bg-fx-surface-3 fx-focus-ring"
                        >
                          <FolderClosed className="h-4 w-4 shrink-0 text-fx-text-2" aria-hidden="true" />
                          <span className="min-w-0 truncate text-fx-body-sm text-fx-text">{e.name}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </section>
          )}

          {/* Resultado del chequeo */}
          {destPath && (
            <DestinationCheckResult check={destCheck} loading={checkLoading} />
          )}

          <FxBanner tone="info" role="note" icon={<ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}>
            Usá un disco cifrado si la evidencia sale de la oficina (BitLocker, LUKS o FileVault).
          </FxBanner>
        </>
      )}
    </div>
  );
}

function DriveRow({ drive, selected, onClick }: { drive: AgentDrive; selected: boolean; onClick: () => void }) {
  const Icon = driveIcon(drive.kind);
  const usedPct = drive.total_bytes > 0 ? ((drive.total_bytes - drive.free_bytes) / drive.total_bytes) * 100 : 0;
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      className={cn(
        "fx-card fx-card-interactive flex w-full items-center gap-3 p-3.5 text-left",
        selected && "border-fx-accent",
      )}
    >
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-fx-md border border-fx-border bg-fx-surface-2">
        <Icon className="h-5 w-5 text-fx-text-2" strokeWidth={1.5} aria-hidden="true" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex items-center gap-2">
          <span className="truncate text-fx-body-sm font-semibold text-fx-text">{drive.label}</span>
          {selected && <Check className="h-4 w-4 shrink-0 text-fx-accent-text" aria-hidden="true" />}
        </span>
        <span className="mt-0.5 block text-xs text-fx-text-3">
          {DRIVE_KIND_LABEL[drive.kind]} · {drive.file_system} · {formatBytes(drive.free_bytes)} libres de {formatBytes(drive.total_bytes)}
        </span>
        <span className="mt-1.5 block">
          <ProgressBar value={usedPct} label={`Espacio usado de ${drive.label}`} />
        </span>
      </span>
    </button>
  );
}

function DriveCard({
  selected, onClick, title, subtitle, icon,
}: { selected: boolean; onClick: () => void; title: string; subtitle: string; icon: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      className={cn("fx-card fx-card-interactive flex w-full items-center gap-3 p-3.5 text-left", selected && "border-fx-accent")}
    >
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-fx-md border border-fx-border bg-fx-surface-2">
        {icon}
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex items-center gap-2">
          <span className="truncate text-fx-body-sm font-semibold text-fx-text">{title}</span>
          {selected && <Check className="h-4 w-4 shrink-0 text-fx-accent-text" aria-hidden="true" />}
        </span>
        <span className="mt-0.5 block truncate font-mono text-xs text-fx-text-3" translate="no">{subtitle}</span>
      </span>
    </button>
  );
}

function DestinationCheckResult({ check, loading }: { check: AgentDestinationCheck | null; loading: boolean }) {
  if (loading) {
    return (
      <p className="m-0 flex items-center gap-2 text-fx-body-sm text-fx-text-2">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> Comprobando el destino…
      </p>
    );
  }
  if (!check) return null;

  if (!check.ok) {
    const msg = check.reason === "insufficient_space"
      ? `Faltan ${formatBytes(check.missing_bytes ?? 0)} de espacio libre.`
      : check.reason === "not_writable"
        ? "No se puede escribir en esta carpeta."
        : check.reason === "path_not_allowed"
          ? "Esta carpeta no está permitida."
          : "La carpeta no existe.";
    return <FxBanner tone="error">{msg}</FxBanner>;
  }

  return (
    <div className="space-y-2">
      <div className="rounded-fx-md border border-fx-border bg-fx-surface-2 p-3">
        <p className="m-0 text-xs text-fx-text-3">La extracción se guardará en:</p>
        <p className="m-0 mt-1 break-all font-mono text-xs text-fx-text" translate="no">{check.final_folder_preview}</p>
        <p className="m-0 mt-1.5 text-xs text-fx-text-2">
          {check.file_system} · {formatBytes(check.free_bytes)} libres
        </p>
      </div>
      {typeof check.max_file_bytes === "number" && (
        <FxBanner tone="warn" role="status">
          Este disco es FAT32: no admite archivos de más de 4 GB. Los archivos más grandes quedarán con error en el acta.
        </FxBanner>
      )}
    </div>
  );
}
