"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "primereact/button";
import { Loader2, Lock, RotateCcw } from "lucide-react";
import { FxBanner } from "@/components/feedback/FxBanner";
import { screenBlockMessage } from "@/lib/agent-messages";
import { cn } from "@/lib/utils";

export interface ScreenBlock {
  /** `session`: inicio de la grabación de la sesión (grabacion-sesion-completa §9.3). */
  action: "screenshot" | "record" | "session";
  state: "off" | "locked";
}

interface Props {
  block: ScreenBlock;
  /** La acción (captura o inicio de grabación) está en vuelo. */
  busy: boolean;
  onRetry: () => void;
  onForce: () => void;
  onDismiss: () => void;
}

/**
 * Aviso de pantalla del celular apagada o bloqueada antes de capturar o grabar
 * (pulido-post-qa-final §8.7). Va junto a la botonera Pantalla / Grabar.
 *
 * - `role="alert"` (de `FxBanner`) una sola vez por aparición: el padre lo
 *   monta con `key` = acción, así un reintento que vuelve a frenar no repite
 *   el anuncio (el texto no cambia).
 * - Al aparecer, el foco va a "Reintentar".
 * - En vuelo, los botones quedan `aria-disabled` (no `disabled`): un botón
 *   `disabled` pierde el foco y el teclado quedaría en `<body>`. El doble
 *   toque lo frenan además los guards del padre.
 */
export function ScreenBlockNotice({ block, busy, onRetry, onForce, onDismiss }: Props) {
  const rootRef = useRef<HTMLDivElement>(null);
  const [pressed, setPressed] = useState<"retry" | "force" | null>(null);

  useEffect(() => {
    rootRef.current?.querySelector<HTMLButtonElement>("button[data-screen-retry]")?.focus();
  }, []);

  const forceLabel = block.action === "screenshot" ? "Capturar igual" : "Grabar igual";
  const spinner = <Loader2 className="h-3.5 w-3.5 motion-safe:animate-spin" aria-hidden="true" />;
  const busyCls = busy && "cursor-not-allowed opacity-60";

  function run(which: "retry" | "force") {
    if (busy) return;
    setPressed(which);
    if (which === "retry") onRetry(); else onForce();
  }

  return (
    <div ref={rootRef}>
      <FxBanner
        tone="warn"
        role="alert"
        icon={<Lock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}
        onClose={onDismiss}
      >
        <p className="m-0">{screenBlockMessage(block.action)}</p>
        <div className="mt-2.5 flex flex-wrap gap-2">
          <Button
            type="button"
            size="small"
            data-screen-retry=""
            icon={busy && pressed === "retry" ? spinner : <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />}
            label="Reintentar"
            onClick={() => run("retry")}
            aria-disabled={busy || undefined}
            aria-busy={(busy && pressed === "retry") || undefined}
            className={cn("min-h-11 sm:min-h-0", busyCls)}
          />
          <Button
            type="button"
            size="small"
            severity="secondary"
            icon={busy && pressed === "force" ? spinner : undefined}
            label={forceLabel}
            onClick={() => run("force")}
            aria-disabled={busy || undefined}
            aria-busy={(busy && pressed === "force") || undefined}
            className={cn("min-h-11 sm:min-h-0", busyCls)}
          />
        </div>
      </FxBanner>
    </div>
  );
}
