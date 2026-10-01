"use client";

import { useState } from "react";
import { Button } from "primereact/button";
import { SelectButton } from "primereact/selectbutton";
import { Loader2, Play, Trash2 } from "lucide-react";
import type { VideoVariant } from "@/lib/agent";
import { agentFileURL } from "@/lib/agent";
import { MEDIA_SURFACE } from "./capture/media";

interface CapturedFile { name: string; uploaded: boolean; sourcePath?: string; }

/** Reproductor de una grabación del agente con sus versiones (original + variantes). */
export function VideoCard({
  file, variants, isPending, onRemove,
}: {
  file: CapturedFile;
  variants: VideoVariant[];
  isPending: boolean;
  onRemove: () => void;
}) {
  const [activeTab, setActiveTab] = useState("original");

  const allTabs = [
    { id: "original", label: "Original", url: agentFileURL(file.name) },
    ...variants.map(v => ({ id: v.label, url: agentFileURL(v.filename), label: v.label })),
  ];
  const current = allTabs.find(t => t.id === activeTab) ?? allTabs[0];
  const showTabs = allTabs.length > 1 || isPending;

  return (
    <div className="overflow-hidden rounded-fx-lg border border-fx-border">
      <div className={MEDIA_SURFACE}>
        {/* contenido de imagen */}
        <video
          key={current.url}
          src={current.url}
          controls
          playsInline
          className="block max-h-[400px] w-full object-contain"
          preload="metadata"
        />
      </div>
      <div className="space-y-2 bg-fx-surface-2 px-3 py-2.5">
        {showTabs && (
          <div className="flex flex-wrap items-center gap-2">
            <SelectButton
              value={activeTab}
              onChange={e => e.value && setActiveTab(e.value)}
              allowEmpty={false}
              options={allTabs}
              optionLabel="label"
              optionValue="id"
              pt={{ root: { "aria-label": "Versión del video" } }}
            />
            {isPending && (
              <span role="status" className="inline-flex items-center gap-1 text-xs text-fx-text-3">
                <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" /> procesando…
              </span>
            )}
          </div>
        )}
        <div className="flex items-center gap-2">
          <Play className="h-3.5 w-3.5 shrink-0 text-fx-text-3" aria-hidden="true" />
          <span translate="no" className="flex-1 truncate font-mono text-xs text-fx-text-2" title={file.name}>
            {file.name}
          </span>
          <Button
            type="button"
            text
            severity="danger"
            size="small"
            icon={<Trash2 className="h-3.5 w-3.5" aria-hidden="true" />}
            aria-label={`Eliminar ${file.name}`}
            onClick={onRemove}
          />
        </div>
      </div>
    </div>
  );
}
