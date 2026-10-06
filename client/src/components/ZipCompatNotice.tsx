import { FxBanner } from "@/components/feedback/FxBanner";
import { cn } from "@/lib/utils";

/**
 * Aviso de compatibilidad del ZIP cifrado (AES-256). La Utilidad de Archivo de
 * macOS y el Explorador de Windows no abren ese cifrado y responden como si la
 * contraseña estuviera mal. Se monta solo si el ZIP salió cifrado, junto a la
 * contraseña y a las acciones de abrir/guardar el ZIP.
 *
 * `role="note"`: es contenido estático de la pantalla, no un evento; no se
 * anuncia como live region al montar.
 */
export function ZipCompatNotice({ id, className }: {
  /** Para enlazarlo con `aria-describedby` desde el botón que baja el ZIP. */
  id?: string;
  className?: string;
}) {
  return (
    <div id={id} className={cn("text-left", className)}>
      <FxBanner tone="warn" role="note">
        <p className="m-0 font-semibold">¿Te dice que la contraseña es incorrecta?</p>
        <p className="m-0 mt-1 font-normal leading-relaxed text-pretty">
          No es la contraseña: el programa que viene con Windows o con macOS no abre este cifrado (AES-256).
          Usá <span translate="no">7-Zip</span> o <span translate="no">WinRAR</span> (Windows)
          o <span translate="no">Keka</span> / <span translate="no">The Unarchiver</span> (macOS).
        </p>
      </FxBanner>
    </div>
  );
}
