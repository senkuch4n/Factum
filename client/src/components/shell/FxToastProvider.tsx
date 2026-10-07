"use client";

import { createContext, useContext, useMemo, useRef, type ReactNode } from "react";
import { Toast } from "primereact/toast";
import { AlertTriangle, CheckCircle2, Info, XCircle } from "lucide-react";

type Severity = "success" | "info" | "warn" | "error";
type Notify = (summary: string, detail?: string) => void;

export interface FxToastApi {
  success: Notify;
  info: Notify;
  warn: Notify;
  error: Notify;
}

const ICONS: Record<Severity, ReactNode> = {
  success: <CheckCircle2 className="h-5 w-5" aria-hidden="true" />,
  info: <Info className="h-5 w-5" aria-hidden="true" />,
  warn: <AlertTriangle className="h-5 w-5" aria-hidden="true" />,
  error: <XCircle className="h-5 w-5" aria-hidden="true" />,
};

const noop: Notify = () => {};
const FxToastContext = createContext<FxToastApi>({ success: noop, info: noop, warn: noop, error: noop });

/**
 * Toast global de Prime (pt `toast`). Abajo a la derecha: no tapa la navbar
 * ni la `SystemStatusLine` (abajo a la izquierda), y su z (1200) queda por
 * encima de los diálogos. Prime pone `role="alert"` + `aria-live` en cada
 * mensaje.
 */
export function FxToastProvider({ children }: { children: ReactNode }) {
  const toastRef = useRef<Toast>(null);

  const api = useMemo<FxToastApi>(() => {
    const show = (severity: Severity): Notify => (summary, detail) =>
      toastRef.current?.show({ severity, summary, detail, life: 5000, icon: ICONS[severity] });
    return { success: show("success"), info: show("info"), warn: show("warn"), error: show("error") };
  }, []);

  return (
    <FxToastContext.Provider value={api}>
      {children}
      <Toast ref={toastRef} position="bottom-right" />
    </FxToastContext.Provider>
  );
}

/** Notificaciones globales: `toast.success("Título", "Detalle opcional")`. */
export function useFxToast(): FxToastApi {
  return useContext(FxToastContext);
}
