"use client";

import { PrimeReactProvider } from "primereact/api";
import type { APIOptions } from "primereact/api";
import { ThemeProvider } from "@/components/ThemeProvider";
import { TooltipProvider } from "@/components/ui/tooltip";
import { fxPassThrough } from "@/lib/prime/pt";
import { cn } from "@/lib/utils";
import "@/lib/prime/locale-es";
import { SystemStatusLine } from "./SystemStatusLine";
import { AppToaster } from "./AppToaster";
import { FxToastProvider } from "./FxToastProvider";

/**
 * Configuración de PrimeReact, con identidad estable (fuera del componente):
 * modo unstyled + pass-through propio sobre tokens --fx-*, locale "es" y sin
 * ripple. Los zIndex por defecto de Prime (modal 1100, overlay/menu 1000,
 * tooltip 1100, toast 1200) quedan por encima de la navbar (40) y de los
 * modales legacy (50/60), que es lo que hace falta.
 */
const PRIME_VALUE: Partial<APIOptions> = {
  unstyled: true,
  pt: fxPassThrough,
  ptOptions: { mergeSections: true, mergeProps: true, classNameMergeFunction: cn },
  locale: "es",
  ripple: false,
};

/**
 * Árbol de providers del cliente. `layout.tsx` sigue siendo server component
 * y solo monta esto. Orden: tema → PrimeReact → toast global de Prime
 * (`useFxToast`) → tooltips. `AppToaster` (sonner) sigue montado para el
 * showcase hasta que lo saque la parte 4.
 */
export function AppProviders({ children }: { children: React.ReactNode }) {
  return (
    <ThemeProvider>
      <PrimeReactProvider value={PRIME_VALUE}>
        <FxToastProvider>
          <TooltipProvider>
            {children}
            <SystemStatusLine />
            <AppToaster />
          </TooltipProvider>
        </FxToastProvider>
      </PrimeReactProvider>
    </ThemeProvider>
  );
}
