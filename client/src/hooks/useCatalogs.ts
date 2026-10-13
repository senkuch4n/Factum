"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { CATALOG_IDS, EMPTY_CATALOGS } from "@/lib/catalogs";
import type { CatalogEntry, CatalogId } from "@/types";

/**
 * Catálogos de sugerencias del perito (`GET /api/catalogs`), cargados cada
 * vez que se monta el paso 2: así un valor guardado con el último caso ya
 * aparece la próxima vez que se abre el paso.
 *
 * `update`/`remove` actualizan el estado local después del OK (los tres
 * campos de "partes" lo ven al instante) y **nunca** tocan el formulario: el
 * caso guarda texto, no una referencia al catálogo (D11).
 */
export function useCatalogs() {
  const [catalogs, setCatalogs] = useState<Record<CatalogId, CatalogEntry[]>>(EMPTY_CATALOGS);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  // Contador de pedidos: descarta respuestas viejas (un reintento pisa al
  // anterior) y las que llegan después del desmontaje (el cleanup lo sube).
  const seq = useRef(0);

  /**
   * Carga (o recarga) los catálogos. No limpia `error` al empezar: el aviso
   * sigue visible mientras se reintenta y solo se va con una respuesta OK.
   */
  const load = useCallback(async () => {
    const my = ++seq.current;
    setLoading(true);
    try {
      const res = await api.getCatalogs();
      if (my !== seq.current) return;
      // Las cuatro claves siempre, aunque un servidor viejo o raro no las mande.
      const next = { ...EMPTY_CATALOGS };
      for (const id of CATALOG_IDS) {
        const list = res.catalogs?.[id];
        if (Array.isArray(list)) next[id] = list;
      }
      setCatalogs(next);
      setError(false);
    } catch {
      if (my === seq.current) setError(true);
    } finally {
      if (my === seq.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
    return () => { seq.current++; };
  }, [load]);

  /** Tira `ApiError` (p. ej. 409 si choca con otro valor) sin tocar el estado. */
  const update = useCallback(async (catalog: CatalogId, id: string, value: string) => {
    const saved = await api.updateCatalogEntry(catalog, id, value);
    setCatalogs(c => ({ ...c, [catalog]: c[catalog].map(e => (e.id === id ? saved : e)) }));
    return saved;
  }, []);

  const remove = useCallback(async (catalog: CatalogId, id: string) => {
    await api.deleteCatalogEntry(catalog, id);
    setCatalogs(c => ({ ...c, [catalog]: c[catalog].filter(e => e.id !== id) }));
  }, []);

  return { catalogs, loading, error, update, remove, reload: load };
}

export type UseCatalogs = ReturnType<typeof useCatalogs>;
