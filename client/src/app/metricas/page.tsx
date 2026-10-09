import type { Metadata } from "next";
import { MetricsScreen } from "@/components/dashboard/MetricsScreen";

export const metadata: Metadata = {
  title: "Métricas · Factum",
};

/**
 * `/metricas` (separar-metricas-vista). Sección paralela al hub de
 * inspecciones. Server component mínimo: la pantalla es cliente (sesión en
 * localStorage) y hace la guarda con `useAuth`, igual que `/admin/cuentas`.
 */
export default function MetricsPage() {
  return <MetricsScreen />;
}
