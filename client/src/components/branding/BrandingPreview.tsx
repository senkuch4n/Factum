"use client";

import { INK_COLOR } from "@/lib/branding";

interface Props {
  organizationName: string;
  contactLines: string[];
  /** Colores efectivos ("#RRGGBB"): si el cargado no es válido, el de Factum (igual que el informe). */
  primary: string;
  accent: string;
  logoSrc: string | null;
  isotypeSrc: string | null;
}

const INK = `#${INK_COLOR}`;
/** Gris de las líneas de texto simulado: fijo, no depende del tema. */
const MUTED = "#d9dde1";

function TextBars({ widths }: { widths: string[] }) {
  return (
    <div aria-hidden="true" className="flex flex-col gap-[0.35em]">
      {widths.map((w, i) => (
        <span key={i} className="block h-[0.4em] rounded-full" style={{ width: w, backgroundColor: MUTED }} />
      ))}
    </div>
  );
}

/**
 * Mini-hoja A4 con el membrete (marca-por-cliente §9.4): fondo blanco y tinta
 * fijos (no siguen el tema, como el informe), plana, sin efectos. Se
 * actualiza en vivo con lo que se carga en el formulario.
 */
export function BrandingPreview({ organizationName, contactLines, primary, accent, logoSrc, isotypeSrc }: Props) {
  const name = organizationName.trim();
  const lines = contactLines.map(l => l.trim()).filter(Boolean);
  const hasHeader = !!logoSrc || !!name || lines.length > 0;

  return (
    <figure aria-label="Vista previa del informe" className="m-0">
      <div
        className="relative mx-auto aspect-[210/297] w-full max-w-[22rem] overflow-hidden rounded-fx-sm border border-fx-border px-[9%] py-[8%] text-[10px] leading-snug shadow-fx-1"
        style={{ backgroundColor: "#ffffff", color: INK }}
      >
        {/* Membrete: logo si hay; si no, el nombre. */}
        <header className="flex min-h-[3.2em] items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            {logoSrc ? (
              <img
                src={logoSrc}
                alt={name ? `Logo de ${name}` : "Logo de la organización"}
                className="block max-h-[3.2em] max-w-[70%] object-contain object-left"
              />
            ) : name ? (
              <p translate="no" className="m-0 break-words text-[1.25em] font-bold leading-tight">{name}</p>
            ) : (
              <p className="m-0 italic" style={{ color: "#5c656e" }}>Sin membrete</p>
            )}
          </div>
          {lines.length > 0 && (
            <ul translate="no" className="m-0 max-w-[48%] list-none p-0 text-right text-[0.72em] leading-snug" style={{ color: "#3d444c" }}>
              {lines.map((l, i) => <li key={i} className="break-words">{l}</li>)}
            </ul>
          )}
        </header>
        <div aria-hidden="true" className="mt-[0.8em] h-[2px] w-full" style={{ backgroundColor: hasHeader ? primary : MUTED }} />

        <p className="m-0 mt-[1.4em] text-center text-[1.05em] font-bold uppercase tracking-wide">Informe pericial</p>

        {/* Sección con número en el primario. */}
        <div className="mt-[1.4em] flex items-baseline gap-[0.5em]">
          <span className="font-bold" style={{ color: primary }}>1.</span>
          <span className="font-bold" style={{ color: primary }}>Objeto del informe</span>
        </div>
        <div className="mt-[0.6em]"><TextBars widths={["100%", "96%", "88%", "62%"]} /></div>

        <div className="mt-[1.4em] flex items-baseline gap-[0.5em]">
          <span className="font-bold" style={{ color: primary }}>2.</span>
          <span className="font-bold" style={{ color: primary }}>Aseguramiento de la evidencia</span>
        </div>
        {/* Fila de tabla con el fondo de acento (como la fila del ZIP del informe). */}
        <table className="mt-[0.6em] w-full border-collapse text-[0.8em]" style={{ border: `1px solid ${MUTED}` }}>
          <tbody>
            <tr style={{ backgroundColor: accent, color: INK }}>
              <th scope="row" className="px-[0.6em] py-[0.4em] text-left font-semibold">Contenedor</th>
              <td className="px-[0.6em] py-[0.4em]">evidencia.zip</td>
            </tr>
            <tr>
              <th scope="row" className="px-[0.6em] py-[0.4em] text-left font-semibold" style={{ borderTop: `1px solid ${MUTED}` }}>SHA-256</th>
              <td className="px-[0.6em] py-[0.4em] font-mono" style={{ borderTop: `1px solid ${MUTED}` }}>3f9a…c21e</td>
            </tr>
          </tbody>
        </table>
        <div className="mt-[0.8em]"><TextBars widths={["100%", "74%"]} /></div>

        {/* Isotipo al cierre. */}
        {isotypeSrc && (
          <div className="absolute inset-x-0 bottom-[6%] flex justify-center">
            <img src={isotypeSrc} alt="Isotipo de la organización" className="block h-[2.4em] w-auto max-w-[30%] object-contain" />
          </div>
        )}
      </div>
      <figcaption className="m-0 mt-2 text-center text-xs text-fx-text-3">
        Vista aproximada. El informe final respeta la plantilla.
      </figcaption>
    </figure>
  );
}
