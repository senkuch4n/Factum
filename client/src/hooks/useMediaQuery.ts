"use client";

import { useCallback, useSyncExternalStore } from "react";

/**
 * `matchMedia(query).matches`, reactivo. En el servidor (y en la hidratación)
 * devuelve `false`: lo que dependa de esto se monta recién en el cliente, sin
 * desajustes de hidratación. Mismo patrón que `useIsSheet` de `CaseDetailModal`.
 */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback((onChange: () => void) => {
    const mql = window.matchMedia(query);
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, [query]);
  return useSyncExternalStore(subscribe, () => window.matchMedia(query).matches, () => false);
}
