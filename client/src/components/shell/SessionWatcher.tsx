"use client";

import { useEffect, useRef } from "react";
import { usePathname, useRouter } from "next/navigation";
import { AUTH_EVENT, type AuthEventDetail } from "@/lib/api";

const CHANGE_PASSWORD_PATH = "/cambiar-contrasena";

/**
 * Sin UI. Escucha `factum:auth` (lo dispara `lib/api.ts`):
 * - `session_ended` → `/` (el login muestra el aviso que dejó la API);
 * - `password_change_required` → `/cambiar-contrasena`, salvo que ya esté ahí.
 */
export function SessionWatcher() {
  const router = useRouter();
  const pathname = usePathname();
  // El listener se registra una vez; la ruta actual se lee por ref.
  const pathRef = useRef(pathname);
  useEffect(() => {
    pathRef.current = pathname;
  }, [pathname]);

  useEffect(() => {
    function onAuth(e: Event) {
      const detail = (e as CustomEvent<AuthEventDetail>).detail;
      if (!detail) return;
      if (detail.type === "session_ended") {
        if (pathRef.current !== "/") router.replace("/");
      } else if (detail.type === "password_change_required") {
        if (pathRef.current !== CHANGE_PASSWORD_PATH) router.replace(CHANGE_PASSWORD_PATH);
      }
    }
    window.addEventListener(AUTH_EVENT, onAuth);
    return () => window.removeEventListener(AUTH_EVENT, onAuth);
  }, [router]);

  return null;
}
