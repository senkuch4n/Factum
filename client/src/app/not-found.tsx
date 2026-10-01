import Link from "next/link";
import { FX_BUTTON_PRIMARY } from "@/lib/prime/pt/shared";

export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 bg-fx-bg px-6 text-center text-fx-text">
      <img src="/logo-theme-dark.svg" alt="Factum" width={289} height={64} className="hidden h-7 w-auto dark:block" />
      <img src="/logo-theme-white.svg" alt="Factum" width={289} height={64} className="block h-7 w-auto dark:hidden" />

      <div className="space-y-3">
        <p className="text-fx-display tabular-nums text-fx-text">404</p>
        <h1 className="text-fx-h3 text-fx-text">No encontramos esta página</h1>
        <p className="text-fx-body text-fx-text-2">La página que buscás no existe o se movió.</p>
      </div>

      <Link href="/dashboard" className={FX_BUTTON_PRIMARY}>
        Volver al inicio
      </Link>
    </main>
  );
}
