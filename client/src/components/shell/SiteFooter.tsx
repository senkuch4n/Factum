import { APP_VERSION } from "@/lib/version";

export interface SiteFooterColumn {
  title: string;
  links: { label: string; href: string }[];
}

export interface SiteFooterOrganization {
  name: string | null;
  logoSrc: string | null;
}

interface SiteFooterProps {
  /** Columnas extra (soporte, contacto, legales) que suma cada HU de página. */
  extraColumns?: SiteFooterColumn[];
  /** Organización emisora (Branding del backend; ver usePublicConfig). */
  organization?: SiteFooterOrganization;
}

const LINK =
  "rounded-fx-sm text-fx-body-sm text-fx-text-3 no-underline transition-colors duration-fx-fast ease-fx hover:text-fx-text fx-focus-ring";

/**
 * Footer multicolumna (landmark contentinfo): producto Factum, la
 * organización emisora (solo si viene `organization` con nombre o logo) y las
 * columnas extra de cada página. Sin estado ni hooks, así se puede usar desde
 * server o client components: quien lo monta le pasa la organización. Hoy
 * solo se monta en /design-system.
 */
export function SiteFooter({ extraColumns = [], organization }: SiteFooterProps) {
  const year = new Date().getFullYear();
  const orgName = organization?.name ?? null;
  const orgLogo = organization?.logoSrc ?? null;

  return (
    <footer className="border-t border-fx-border bg-fx-surface-1">
      <div className="mx-auto max-w-[1400px] px-4 py-10 sm:px-6">
        <div className="grid grid-cols-1 gap-8 sm:grid-cols-2 lg:grid-cols-4">
          <section aria-labelledby="footer-producto" className="space-y-3">
            <h2 id="footer-producto" className="text-fx-label uppercase text-fx-text-2">Producto</h2>
            <img src="/logo-theme-dark.svg" alt="Factum" width={289} height={64} className="hidden h-6 w-auto dark:block" />
            <img src="/logo-theme-white.svg" alt="Factum" width={289} height={64} className="block h-6 w-auto dark:hidden" />
            <p className="text-fx-body-sm font-semibold text-fx-text"><span translate="no">Factum</span> v{APP_VERSION}</p>
            <p className="max-w-[32ch] text-fx-body-sm text-fx-text-3">
              Adquisición forense de evidencia digital en dispositivos móviles
            </p>
          </section>

          {(orgName || orgLogo) && (
            <section aria-labelledby="footer-organizacion" className="space-y-3">
              <h2 id="footer-organizacion" className="text-fx-label uppercase text-fx-text-2">Organización</h2>
              {orgLogo && (
                <img
                  src={orgLogo}
                  alt={orgName ? "" : "Logo de la organización"}
                  className="h-10 w-auto max-w-[200px] object-contain"
                />
              )}
              {orgName && <p translate="no" className="max-w-[32ch] break-words text-fx-body-sm text-fx-text-3">{orgName}</p>}
            </section>
          )}

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
          <p className="text-fx-body-sm text-fx-text-3" suppressHydrationWarning>© {year} Factum</p>
        </div>
      </div>
    </footer>
  );
}
