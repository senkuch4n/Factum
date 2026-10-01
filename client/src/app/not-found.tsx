import Link from "next/link";

export default function NotFound() {
  return (
    <div
      className="flex min-h-screen flex-col items-center justify-center gap-6 px-6 text-center"
      style={{ background: "var(--bg-base)", color: "var(--text-primary)" }}
    >
      <img src="/logo-theme-dark.svg" alt="Factum" className="hidden h-7 w-auto dark:block" />
      <img src="/logo-theme-white.svg" alt="Factum" className="block h-7 w-auto dark:hidden" />

      <div className="space-y-2">
        <p className="text-5xl font-bold tracking-tight tabular-nums">404</p>
        <p className="text-sm" style={{ color: "var(--text-muted)" }}>
          La página que buscás no existe o se movió.
        </p>
      </div>

      <Link
        href="/dashboard"
        className="btn-primary btn-sm rounded-lg"
        style={{ textDecoration: "none" }}
      >
        Volver al inicio
      </Link>
    </div>
  );
}
