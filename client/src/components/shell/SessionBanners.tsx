"use client";

import { Button } from "primereact/button";
import { FxBanner } from "@/components/feedback/FxBanner";
import { formatClock } from "@/lib/session-timing";
import { cn } from "@/lib/utils";
import { useSessionUi } from "./SessionManager";

const BANNER_BODY = "flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between sm:gap-4";
const ACTIONS = "flex shrink-0 flex-wrap items-center gap-2";

/**
 * Avisos de sesión (sesion-larga §8.7). Van dentro del `<header sticky>` de
 * `AppNavbar`, debajo de la píldora y con su mismo ancho: empujan el contenido
 * en vez de taparlo. Cada banner se monta una vez por vencimiento/tope
 * (`key`), así el lector lo anuncia una sola vez: el texto no cambia por tick.
 * La región `sr-only` anuncia "Sesión renovada" solo tras "Seguir conectado";
 * el toast visual queda oculto al lector para no anunciarlo dos veces.
 */
export function SessionBanners({ maxWidthClass }: { maxWidthClass: string }) {
  const ui = useSessionUi();
  if (!ui) return null;
  const { expiryAt, capAt, renewing, renewFailed, renewedFlash, announce, keepAlive, dismissCap, openEarly } = ui;
  const hasBanner = renewedFlash || expiryAt !== null || capAt !== null;

  return (
    <div className={cn("pointer-events-auto mx-auto w-full", maxWidthClass)}>
      <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">{announce}</div>

      {hasBanner && (
        <div className="mt-2 space-y-2">
          {renewedFlash ? (
            <div aria-hidden="true">
              <FxBanner tone="success" role="note">Sesión renovada</FxBanner>
            </div>
          ) : expiryAt !== null && (
            <FxBanner key={`exp-${expiryAt}`} tone="info" role="status">
              <div className={BANNER_BODY}>
                <div className="min-w-0">
                  <p className="m-0">Tu sesión vence a las {formatClock(expiryAt)} por inactividad.</p>
                  {renewFailed && (
                    <p className="m-0 mt-1 font-normal">No se pudo renovar. Reintentá.</p>
                  )}
                </div>
                <div className={ACTIONS}>
                  <Button
                    type="button"
                    size="small"
                    label={renewing ? "Renovando…" : "Seguir conectado"}
                    loading={renewing}
                    onClick={keepAlive}
                    className="min-h-11 sm:min-h-8"
                  />
                </div>
              </div>
            </FxBanner>
          )}

          {capAt !== null && (
            <FxBanner key={`cap-${capAt}`} tone="warn" role="status">
              <div className={BANNER_BODY}>
                <p className="m-0 min-w-0">
                  Por seguridad, vas a tener que volver a iniciar sesión a las {formatClock(capAt)}. Lo que cargaste no
                  se pierde.
                </p>
                <div className={ACTIONS}>
                  <Button
                    type="button"
                    size="small"
                    severity="secondary"
                    outlined
                    label="Iniciar sesión ahora"
                    onClick={openEarly}
                    className="min-h-11 sm:min-h-8"
                  />
                  <Button
                    type="button"
                    size="small"
                    severity="secondary"
                    text
                    label="Entendido"
                    onClick={dismissCap}
                    className="min-h-11 sm:min-h-8"
                  />
                </div>
              </div>
            </FxBanner>
          )}
        </div>
      )}
    </div>
  );
}
