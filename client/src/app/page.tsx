import { ThemeSwitch } from "@/components/shell/ThemeSwitch";
import { LoginHero } from "@/components/login/LoginHero";
import { LoginForm } from "@/components/login/LoginForm";

/**
 * Login (`/`). Server component que solo compone: el panel de marca y el
 * formulario son client components. Único landmark `main`, sin navbar ni
 * footer (D6). El ThemeSwitch va primero en el DOM (orden de tab) y se
 * posiciona en la esquina: en escritorio cae sobre el panel de acceso y en
 * mobile sobre la franja de marca.
 */
export default function LoginPage() {
  return (
    <main className="relative flex min-h-dvh flex-col bg-fx-bg lg:flex-row">
      <ThemeSwitch className="absolute right-3 top-3 z-10 h-11 w-11 lg:right-8 lg:top-6" />

      <LoginHero />

      <div className="flex flex-1 items-center justify-center bg-fx-surface-1 px-5 pb-16 pt-8 lg:px-16 lg:py-12">
        <div className="w-full max-w-[400px] motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]">
          <LoginForm />
        </div>
      </div>
    </main>
  );
}
