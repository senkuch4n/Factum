import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Encabezado de un paso del wizard: título (`h2`), bajada opcional y un
 * slot a la derecha (medidor, indicador de guardado…).
 */
export function StepHeader({
  title, description, aside, className, align = "start",
}: {
  title: string;
  description?: ReactNode;
  aside?: ReactNode;
  className?: string;
  align?: "start" | "center";
}) {
  const center = align === "center";
  return (
    <div
      className={cn(
        "flex flex-wrap gap-3",
        center ? "flex-col items-center text-center" : "items-end justify-between",
        className,
      )}
    >
      <div className="min-w-0">
        <h2 className="m-0 text-fx-h2 text-fx-text text-balance">{title}</h2>
        {description && (
          <p className={cn("m-0 mt-2 max-w-prose text-fx-body-sm text-fx-text-2", center && "mx-auto")}>
            {description}
          </p>
        )}
      </div>
      {aside}
    </div>
  );
}
