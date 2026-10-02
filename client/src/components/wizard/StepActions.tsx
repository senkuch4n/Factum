import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Botonera de un paso. Convención de hijos:
 * - "Atrás": `<Button severity="secondary" … className="w-full sm:w-auto min-h-11" />`
 * - principal: `<Button iconPos="right" … className="w-full sm:flex-1 min-h-11" />`
 *
 * En `< sm` quedan apiladas a ancho completo con la principal arriba
 * (`flex-col-reverse`); el orden de tabulación sigue al DOM (Atrás → principal).
 */
export function StepActions({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn("flex flex-col-reverse gap-3 pt-2 sm:flex-row sm:items-center", className)}>
      {children}
    </div>
  );
}
