import { ThemeSwitch } from "@/components/shell/ThemeSwitch";
import { LoginHero } from "@/components/login/LoginHero";
import { ChangePasswordForm } from "@/components/password/ChangePasswordForm";

/**
 * Cambio de contraseña obligatorio (`/cambiar-contrasena`, usuarios-locales
 * §9.8). Server component que solo compone, con el mismo layout del login:
 * panel de marca + formulario a la derecha.
 */
export default function ChangePasswordPage() {
  return (
    <main className="relative flex min-h-dvh flex-col bg-fx-bg lg:flex-row">
      <ThemeSwitch className="absolute right-3 top-3 z-10 h-11 w-11 lg:right-8 lg:top-6" />

      <LoginHero />

      <div className="flex flex-1 items-center justify-center bg-fx-surface-1 px-5 pb-16 pt-8 lg:px-16 lg:py-12">
        <div className="w-full max-w-[400px] motion-safe:animate-[fx-rise-in_var(--fx-dur-slow)_var(--fx-ease-out)_both]">
          <ChangePasswordForm />
        </div>
      </div>
    </main>
  );
}
