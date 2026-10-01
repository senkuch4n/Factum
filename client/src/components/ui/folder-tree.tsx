"use client";

/**
 * Árbol de archivos — adaptado de scrollxui (docs/components/folder-tree).
 *
 * Reconstruido a mano (sin dependencias): árbol recursivo con carpetas
 * colapsables, tokens de factum. Pensado para mostrar la estructura del
 * paquete de evidencia (qué entra en el ZIP) en la pantalla de generación/cierre.
 */

import { useState } from "react";
import { ChevronRight, File, Folder, FolderOpen } from "lucide-react";
import { cn } from "@/lib/utils";

export interface TreeNode {
  name: string;
  type: "folder" | "file";
  children?: TreeNode[];
  /** texto secundario a la derecha (tamaño, hash corto, cantidad…) */
  meta?: string;
}

/** Construye un árbol a partir de rutas tipo "capturas/imagen 1.png". */
export function buildTree(paths: string[]): TreeNode[] {
  const root: TreeNode[] = [];
  for (const path of paths) {
    const parts = path.split("/").filter(Boolean);
    let level = root;
    parts.forEach((part, i) => {
      const isFile = i === parts.length - 1;
      let node = level.find((n) => n.name === part);
      if (!node) {
        node = { name: part, type: isFile ? "file" : "folder", children: isFile ? undefined : [] };
        level.push(node);
      }
      if (!isFile) level = node.children!;
    });
  }
  return root;
}

function Node({ node, depth }: { node: TreeNode; depth: number }) {
  const [open, setOpen] = useState(depth === 0);
  const isFolder = node.type === "folder";
  const pad = 8 + depth * 16;

  if (!isFolder) {
    return (
      <div className="flex items-center gap-2 py-1.5 text-[13px]" style={{ paddingLeft: pad + 20, color: "var(--text-secondary)" }}>
        <File className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--text-muted)" }} aria-hidden="true" />
        <span className="truncate">{node.name}</span>
        {node.meta && <span className="ml-auto shrink-0 text-[11px]" style={{ color: "var(--text-muted)" }}>{node.meta}</span>}
      </div>
    );
  }

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        className="flex w-full items-center gap-2 py-1.5 text-left text-[13px] font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--blue-lg)] rounded"
        style={{ paddingLeft: pad, color: "var(--text-primary)" }}
      >
        <ChevronRight
          className={cn("h-3.5 w-3.5 shrink-0 transition-transform", open && "rotate-90")}
          style={{ color: "var(--text-muted)" }}
          aria-hidden="true"
        />
        {open ? (
          <FolderOpen className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
        ) : (
          <Folder className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--blue-lg)" }} aria-hidden="true" />
        )}
        <span className="truncate">{node.name}</span>
        {node.meta && <span className="ml-auto shrink-0 text-[11px]" style={{ color: "var(--text-muted)" }}>{node.meta}</span>}
      </button>
      {open && node.children?.map((child, i) => <Node key={child.name + i} node={child} depth={depth + 1} />)}
    </div>
  );
}

export function FolderTree({ nodes, className }: { nodes: TreeNode[]; className?: string }) {
  return (
    <div
      className={cn("rounded-lg py-1", className)}
      style={{ background: "var(--bg-elevated)", border: "1px solid var(--border)" }}
      role="tree"
    >
      {nodes.map((n, i) => <Node key={n.name + i} node={n} depth={0} />)}
    </div>
  );
}

export default FolderTree;
