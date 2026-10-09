"use client";

/**
 * Diff por palabra entre dos textos visibles (versionado-informe, HU6 D7). La
 * librería `diff` (jsdiff) se carga con import dinámico: no entra al bundle del
 * dashboard hasta que el perito abre "Comparar".
 *
 * Accesibilidad: lo agregado y lo quitado no se distinguen solo por color
 * (regla de accesibilidad "Color Only"). Lo agregado va subrayado con un `+`
 * para lector de pantalla; lo quitado, tachado con un `−`. Los tokens
 * `--fx-success`/`--fx-danger` dan el color.
 */

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";

interface Part {
  value: string;
  added?: boolean;
  removed?: boolean;
}

export function VersionDiff({ before, after }: { before: string; after: string }) {
  const [parts, setParts] = useState<Part[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let alive = true;
    setParts(null);
    setFailed(false);
    import("diff")
      .then(({ diffWords }) => {
        if (alive) setParts(diffWords(before, after) as Part[]);
      })
      .catch(() => { if (alive) setFailed(true); });
    return () => { alive = false; };
  }, [before, after]);

  if (failed) {
    return <p className="m-0 text-fx-body-sm text-fx-text-2">No se pudo calcular la comparación.</p>;
  }

  if (!parts) {
    return (
      <div role="status" className="flex items-center gap-2 py-4 text-fx-body-sm text-fx-text-2">
        <Loader2 className="h-4 w-4 animate-spin text-fx-text-3" aria-hidden="true" /> Calculando diferencias…
      </div>
    );
  }

  const anyChange = parts.some(p => p.added || p.removed);

  return (
    <div className="rounded-fx-md border border-fx-border bg-fx-surface-2 p-3.5">
      {!anyChange ? (
        <p className="m-0 text-fx-body-sm text-fx-text-2">No hay diferencias de texto entre las dos versiones.</p>
      ) : (
        <p className="m-0 whitespace-pre-wrap break-words text-fx-body-sm leading-relaxed text-fx-text">
          {parts.map((p, i) => {
            if (p.added) {
              return (
                <mark
                  key={i}
                  className="rounded-fx-sm bg-fx-success-soft px-0.5 text-fx-success underline decoration-fx-success/60"
                >
                  <span className="sr-only">texto agregado: </span>
                  <span aria-hidden="true">+</span>{p.value}
                </mark>
              );
            }
            if (p.removed) {
              return (
                <del
                  key={i}
                  className="rounded-fx-sm bg-fx-danger-soft px-0.5 text-fx-danger line-through decoration-fx-danger/60"
                >
                  <span className="sr-only">texto quitado: </span>
                  <span aria-hidden="true">−</span>{p.value}
                </del>
              );
            }
            return <span key={i}>{p.value}</span>;
          })}
        </p>
      )}
    </div>
  );
}
