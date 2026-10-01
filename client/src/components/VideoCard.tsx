"use client";

import { useState } from "react";
import { Play, Loader2, Trash2 } from "lucide-react";
import type { VideoVariant } from "@/lib/agent";
import { agentFileURL } from "@/lib/agent";
import { cn } from "@/lib/utils";

interface CapturedFile { name: string; uploaded: boolean; sourcePath?: string; }

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
    <div
      className="rounded-xl overflow-hidden"
      style={{ border: "1px solid var(--border)", background: "#000" }}
    >
      <video
        key={current.url}
        src={current.url}
        controls
        playsInline
        className="w-full max-h-[400px] object-contain"
        preload="metadata"
      />
      <div className="px-3 pt-2 pb-2 space-y-1.5" style={{ background: "var(--bg-elevated)" }}>
        {showTabs && (
          <div className="flex items-center gap-1 flex-wrap">
            {allTabs.map(tab => (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id)}
                className={cn(
                  "px-2 py-0.5 rounded text-[9px] font-semibold transition-colors border",
                  activeTab === tab.id
                    ? "border-teal-500/40 text-teal-400"
                    : "border-transparent text-[var(--text-muted)] hover:text-[var(--text-secondary)]",
                )}
                style={activeTab === tab.id ? { background: "rgba(45,212,191,0.12)" } : {}}
              >
                {tab.label}
              </button>
            ))}
            {isPending && (
              <span className="flex items-center gap-1 px-1 text-[9px]" style={{ color: "var(--text-muted)" }}>
                <Loader2 className="w-2.5 h-2.5 animate-spin" />
                procesando...
              </span>
            )}
          </div>
        )}
        <div className="flex items-center gap-2">
          <Play className="w-3 h-3 flex-shrink-0" style={{ color: "var(--text-muted)" }} />
          <span className="text-[10px] font-mono truncate flex-1" style={{ color: "var(--text-muted)" }}>
            {file.name}
          </span>
          <button
            className="flex-shrink-0 w-6 h-6 rounded flex items-center justify-center hover:bg-red-500/10 transition-colors"
            onClick={onRemove}
          >
            <Trash2 className="w-3.5 h-3.5 text-red-400" />
          </button>
        </div>
      </div>
    </div>
  );
}
