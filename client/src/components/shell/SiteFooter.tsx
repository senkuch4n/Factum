import { APP_VERSION } from "@/lib/version";

export interface SiteFooterColumn {
  title: string;
  links: { label: string; href: string }[];
}

interface SiteFooterProps {
  /** Columnas extra (soporte, contacto, legales) que suma cada HU de página. */
  extraColumns?: SiteFooterColumn[];
}

const LINK =
  "rounded-fx-sm text-fx-body-sm text-fx-text-3 no-underline transition-colors duration-fx-fast ease-fx hover:text-fx-text fx-focus-ring";

/**
 * Footer multicolumna (landmark contentinfo). Contenido placeholder: el
 * institucional definitivo queda pendiente con el usuario. Sin estado ni
 * hooks, así se puede usar desde server o client components. En esta HU solo
 * se monta en /design-system.
 */
export function SiteFooter({ extraColumns = [] }: SiteFooterProps) {
  const year = new Date().getFullYear();

  return (
    <footer className="border-t border-fx-border bg-fx-surface-1">
      <div className="mx-auto max-w-[1400px] px-4 py-10 sm:px-6">
        <div className="grid grid-cols-1 gap-8 sm:grid-cols-2 lg:grid-cols-4">
          <section aria-labelledby="footer-producto" className="space-y-3">
            <h2 id="footer-producto" className="text-fx-label uppercase text-fx-text-2">Producto</h2>
            <img src="/logo-theme-dark.svg" alt="Factum" className="hidden h-6 w-auto dark:block" />
            <img src="/logo-theme-white.svg" alt="Factum" className="block h-6 w-auto dark:hidden" />
            <p className="text-fx-body-sm font-semibold text-fx-text"><span translate="no">Factum</span> v{APP_VERSION}</p>
            <p className="max-w-[32ch] text-fx-body-sm text-fx-text-3">
              Adquisición forense de evidencia digital en dispositivos móviles
            </p>
          </section>

          <section aria-labelledby="footer-institucion" className="space-y-3">
            <h2 id="footer-institucion" className="text-fx-label uppercase text-fx-text-2">Institución</h2>
            <div className="flex items-center gap-4">
              <img src="/MPF_logo.png" alt="Ministerio Público Fiscal de Salta" className="h-10 w-auto" />
              <img src="/GFD_logo.png" alt="Gabinete Forense Digital" className="h-10 w-auto" />
            </div>
            <p className="max-w-[32ch] text-fx-body-sm text-fx-text-3">
              Ministerio Público Fiscal de Salta · Gabinete Forense Digital
            </p>
          </section>

          {extraColumns.map((col, i) => (
            <nav key={col.title} aria-labelledby={`footer-extra-${i}`} className="space-y-3">
              <h2 id={`footer-extra-${i}`} className="text-fx-label uppercase text-fx-text-2">{col.title}</h2>
              <ul className="space-y-2">
                {col.links.map((link) => (
                  <li key={link.href}>
                    <a href={link.href} className={LINK}>{link.label}</a>
                  </li>
                ))}
              </ul>
            </nav>
          ))}
        </div>

        <div className="mt-10 border-t border-fx-border pt-6">
          {/* El año puede diferir entre SSR y cliente justo en el cambio de año. */}
          <p className="text-fx-body-sm text-fx-text-3" suppressHydrationWarning>© {year} MPF Salta – GFD</p>
        </div>
      </div>
    </footer>
  );
}
