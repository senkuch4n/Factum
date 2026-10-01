"use client";

import { AlertTriangle, CheckCircle2, Info, XCircle } from "lucide-react";
import { Toaster } from "sonner";
import { useTheme } from "@/lib/theme";

/**
 * Toaster global de sonner, sin estilos propios (`unstyled`) y vestido con
 * los tokens --fx-*: mismo esquema que el Toast de Prime (superficie 2 +
 * borde izquierdo por tipo). Siempre ícono + texto. `mobileOffset` deja
 * libre el FloatingDock del dashboard.
 */
const FOCUS =
  "outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-fx-focus";

const TOAST_CLASSES = {
  toast:
    "group flex w-full items-start gap-3 rounded-fx-lg border border-l-4 border-fx-border bg-fx-surface-2 p-4 font-sans text-fx-text shadow-fx-2",
  title: "text-fx-body-sm font-semibold text-fx-text",
  description: "text-fx-body-sm text-fx-text-2",
  content: "flex min-w-0 flex-1 flex-col gap-0.5",
  icon: "mt-px shrink-0",
  actionButton: `ml-auto shrink-0 rounded-fx-md bg-fx-accent px-3 py-1.5 text-xs font-bold text-fx-on-accent hover:bg-fx-accent-hover ${FOCUS}`,
  cancelButton: `shrink-0 rounded-fx-md border border-fx-border-strong bg-fx-surface-2 px-3 py-1.5 text-xs font-semibold text-fx-text hover:bg-fx-surface-3 ${FOCUS}`,
  closeButton: `absolute -left-2 -top-2 flex h-5 w-5 items-center justify-center rounded-fx-pill border border-fx-border bg-fx-surface-3 text-fx-text-2 hover:text-fx-text ${FOCUS}`,
  success: "border-l-fx-success",
  error: "border-l-fx-danger",
  warning: "border-l-fx-warning",
  info: "border-l-fx-info",
};

const ICONS = {
  success: <CheckCircle2 className="h-5 w-5 text-fx-success" aria-hidden="true" />,
  info: <Info className="h-5 w-5 text-fx-info" aria-hidden="true" />,
  warning: <AlertTriangle className="h-5 w-5 text-fx-warning" aria-hidden="true" />,
  error: <XCircle className="h-5 w-5 text-fx-danger" aria-hidden="true" />,
};

export function AppToaster() {
  const { isDark } = useTheme();

  return (
    <Toaster
      theme={isDark ? "dark" : "light"}
      position="bottom-right"
      offset={24}
      mobileOffset={{ bottom: 96 }}
      containerAriaLabel="Notificaciones"
      icons={ICONS}
      toastOptions={{
        unstyled: true,
        closeButtonAriaLabel: "Cerrar notificación",
        classNames: TOAST_CLASSES,
      }}
    />
  );
}
