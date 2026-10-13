"use client";

/**
 * Integrantes como lista ordenada de filas (formulario-caso-catalogos, SDD §7.5).
 * Cada fila lleva su propio tratamiento ("Dr. Juan Pérez"); el servidor arma
 * la frase del informe ("A, B y C") y acá se muestra una vista previa con la
 * misma regla (`joinIntegrantes`). Las filas vacías se ven pero no viajan.
 *
 * Reordenar es con subir/bajar (sin drag & drop, D10): accesible por teclado
 * y el foco sigue al botón usado en su nueva posición.
 */

import { useLayoutEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import { InputText } from "primereact/inputtext";
import { ChevronDown, ChevronUp, Plus, Split, Users, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { MAX_LEN_LINE, cleanIntegrantes, joinIntegrantes, newIntegranteRow } from "@/lib/pericial";
import { splitIntegrantes } from "@/lib/case-summary";
import { FormField } from "@/components/FormField";
import type { IntegranteRow } from "@/types";

/** Tope de filas (D6; el servidor valida lo mismo). */
export const MAX_INTEGRANTES = 20;

export const INTEGRANTES_ADD_ID = "case-integrantes-add";
const rowInputId = (key: string) => `case-integrante-${key}`;
const rowButtonId = (key: string, part: "up" | "down" | "remove") => `case-integrante-${key}-${part}`;

// 44 px de alto siempre; en móvil, 32 px de ancho para que el input conserve lugar.
const ROW_BUTTON = cn(
  "inline-flex h-11 w-8 sm:w-11 shrink-0 items-center justify-center rounded-fx-md border-0 bg-transparent p-0 cursor-pointer",
  "text-fx-text-3 transition-colors duration-fx-fast ease-fx fx-focus-ring",
  "enabled:hover:bg-fx-surface-3 enabled:hover:text-fx-text disabled:cursor-not-allowed disabled:opacity-40",
);

interface Props {
  rows: IntegranteRow[];
  onChange: (rows: IntegranteRow[]) => void;
  /** Marca "tomado de la causa anterior" junto a la etiqueta (precarga visible). */
  prefilled?: React.ReactNode;
}

/** Oferta "Separar en N" después de pegar varios nombres en una fila vacía (formulario-causa-rediseno §6.13). */
interface SplitOffer {
  rowKey: string;
  parts: string[];
  /** Valor que quedó en la fila después del pegado (el navegador puede quitar saltos de línea); null hasta el primer cambio. */
  pasted: string | null;
}

export function IntegrantesField({ rows, onChange, prefilled }: Props) {
  const pendingFocus = useRef<string | null>(null);
  const [offer, setOffer] = useState<SplitOffer | null>(null);
  const [addedNote, setAddedNote] = useState<{ rowKey: string; added: number } | null>(null);

  // Después del commit: el control que tiene que recibir el foco ya existe.
  useLayoutEffect(() => {
    if (!pendingFocus.current) return;
    document.getElementById(pendingFocus.current)?.focus();
    pendingFocus.current = null;
  });

  const full = rows.length >= MAX_INTEGRANTES;
  const preview = joinIntegrantes(cleanIntegrantes(rows.map(r => r.value)));

  function setValue(key: string, value: string) {
    if (offer?.rowKey === key && offer.pasted === null) setOffer({ ...offer, pasted: value });
    onChange(rows.map(r => (r.key === key ? { ...r, value } : r)));
  }

  // La oferta vale mientras la fila conserve exactamente lo pegado (editarla la descarta).
  const activeOffer = offer && offer.pasted !== null && rows.find(r => r.key === offer.rowKey)?.value === offer.pasted ? offer : null;

  function onPaste(row: IntegranteRow, e: React.ClipboardEvent<HTMLInputElement>) {
    if (row.value !== "") return;
    const text = e.clipboardData.getData("text");
    const parts = splitIntegrantes(text);
    // El pegado ocurre normal (queda en una fila, como siempre); solo se ofrece separar.
    if (parts.length >= 2) setOffer({ rowKey: row.key, parts, pasted: null });
    else setOffer(null);
    setAddedNote(null);
  }

  function acceptSplit(o: SplitOffer) {
    const index = rows.findIndex(r => r.key === o.rowKey);
    if (index < 0) return;
    const room = MAX_INTEGRANTES - rows.length + 1;
    const parts = o.parts.slice(0, room);
    const created = parts.map((v, i) => (i === 0 ? { ...rows[index], value: v } : newIntegranteRow(v)));
    pendingFocus.current = rowInputId(created[created.length - 1].key);
    onChange([...rows.slice(0, index), ...created, ...rows.slice(index + 1)]);
    setOffer(null);
    setAddedNote(parts.length < o.parts.length ? { rowKey: created[created.length - 1].key, added: parts.length } : null);
  }

  function add() {
    if (full) return;
    const row = newIntegranteRow();
    pendingFocus.current = rowInputId(row.key);
    onChange([...rows, row]);
  }

  function move(index: number, delta: -1 | 1) {
    const to = index + delta;
    if (to < 0 || to >= rows.length) return;
    const next = [...rows];
    [next[index], next[to]] = [next[to], next[index]];
    const key = rows[index].key;
    // El mismo botón en la nueva posición; si quedó deshabilitado (extremo), el otro.
    const edge = delta === -1 ? to === 0 : to === rows.length - 1;
    pendingFocus.current = rowButtonId(key, edge ? (delta === -1 ? "down" : "up") : delta === -1 ? "up" : "down");
    onChange(next);
  }

  function remove(index: number) {
    const next = rows.filter((_, i) => i !== index);
    const neighbor = next[index - 1] ?? next[index];
    pendingFocus.current = neighbor ? rowInputId(neighbor.key) : INTEGRANTES_ADD_ID;
    onChange(next);
  }

  return (
    <div className="col-span-full">
      <FormField
        id={rows.length ? rowInputId(rows[0].key) : INTEGRANTES_ADD_ID}
        icon={Users}
        label="Integrantes"
        optional
        labelBadge={prefilled}
      >
        <div className="flex flex-col gap-2">
          {rows.length > 0 && (
            <ol aria-label="Integrantes" className="m-0 flex list-none flex-col gap-2 p-0">
              {rows.map((row, i) => {
                const n = i + 1;
                const last = i === rows.length - 1;
                return (
                  <li key={row.key} className="flex flex-col gap-1.5">
                    <div className="flex items-center gap-0.5 sm:gap-1">
                    <span aria-hidden="true" className="hidden w-6 shrink-0 text-right text-xs tabular-nums text-fx-text-3 sm:inline">
                      {n}.
                    </span>
                    <InputText
                      id={rowInputId(row.key)}
                      name="integrantes"
                      value={row.value}
                      onChange={e => setValue(row.key, e.target.value)}
                      onPaste={e => onPaste(row, e)}
                      onKeyDown={e => {
                        // Enter en la última fila agrega otra (si esta no está vacía).
                        if (e.key !== "Enter" || e.nativeEvent.isComposing) return;
                        e.preventDefault();
                        if (last && row.value.trim()) add();
                      }}
                      placeholder="Ej.: Dr. Juan Pérez"
                      maxLength={MAX_LEN_LINE}
                      autoComplete="off"
                      aria-label={`Integrante ${n}`}
                      pt={{ root: { className: "min-w-0 flex-1 sm:ml-1" } }}
                    />
                    <button
                      id={rowButtonId(row.key, "up")}
                      type="button"
                      onClick={() => move(i, -1)}
                      disabled={i === 0}
                      aria-label={`Subir integrante ${n}`}
                      title="Subir"
                      className={ROW_BUTTON}
                    >
                      <ChevronUp className="h-4 w-4" aria-hidden="true" />
                    </button>
                    <button
                      id={rowButtonId(row.key, "down")}
                      type="button"
                      onClick={() => move(i, 1)}
                      disabled={last}
                      aria-label={`Bajar integrante ${n}`}
                      title="Bajar"
                      className={ROW_BUTTON}
                    >
                      <ChevronDown className="h-4 w-4" aria-hidden="true" />
                    </button>
                    <button
                      id={rowButtonId(row.key, "remove")}
                      type="button"
                      onClick={() => remove(i)}
                      aria-label={`Quitar integrante ${n}`}
                      title="Quitar"
                      className={cn(ROW_BUTTON, "enabled:hover:bg-fx-danger-soft enabled:hover:text-fx-danger")}
                    >
                      <X className="h-4 w-4" aria-hidden="true" />
                    </button>
                    </div>
                    {activeOffer?.rowKey === row.key && (
                      <div className="flex flex-wrap items-center gap-1 sm:pl-7 motion-safe:animate-[fx-fade-in_var(--fx-dur-base)_var(--fx-ease-out)_both]">
                        <Button
                          type="button"
                          size="small"
                          outlined
                          severity="secondary"
                          icon={<Split className="h-3.5 w-3.5" aria-hidden="true" />}
                          label={`Separar en ${activeOffer.parts.length} integrantes`}
                          onClick={() => acceptSplit(activeOffer)}
                          className="[@media(pointer:coarse)]:min-h-11"
                        />
                        <button
                          type="button"
                          onClick={() => setOffer(null)}
                          aria-label="Descartar"
                          title="Descartar"
                          className={ROW_BUTTON}
                        >
                          <X className="h-4 w-4" aria-hidden="true" />
                        </button>
                      </div>
                    )}
                    {addedNote?.rowKey === row.key && (
                      <p className="m-0 text-xs text-fx-text-2 sm:pl-7">
                        Se agregaron {addedNote.added} (máximo {MAX_INTEGRANTES}).
                      </p>
                    )}
                  </li>
                );
              })}
            </ol>
          )}

          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Button
              id={INTEGRANTES_ADD_ID}
              type="button"
              text
              size="small"
              icon={<Plus className="h-4 w-4" aria-hidden="true" />}
              label="Agregar integrante"
              onClick={add}
              disabled={full}
              aria-describedby={full ? `${INTEGRANTES_ADD_ID}-max` : undefined}
              className="min-h-11 -ml-2"
            />
            {full && (
              <span id={`${INTEGRANTES_ADD_ID}-max`} className="text-xs text-fx-text-3">
                Llegaste al máximo de {MAX_INTEGRANTES} integrantes
              </span>
            )}
          </div>
        </div>

        {/* Región viva siempre montada (vacía no ocupa lugar): así el lector anuncia los cambios de la frase. */}
        <div aria-live="polite">
          {preview && (
            <p className="m-0 mt-1 text-xs leading-snug text-fx-text-2">
              En el informe: <span className="text-fx-text">…con la integración de {preview}</span>
            </p>
          )}
        </div>
      </FormField>
    </div>
  );
}
