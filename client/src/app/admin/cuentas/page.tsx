import type { Metadata } from "next";
import { AdminAccountsScreen } from "@/components/admin/AdminAccountsScreen";

export const metadata: Metadata = {
  title: "Administrar cuentas · Factum",
};

/**
 * `/admin/cuentas` (abm-clientes §9.5). Server component mínimo: la pantalla
 * es cliente (sesión en localStorage) y hace la guarda de rol y modo.
 */
export default function AdminAccountsPage() {
  return <AdminAccountsScreen />;
}
