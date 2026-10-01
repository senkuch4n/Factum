import type { Metadata, Viewport } from "next";
import "./globals.css";
import { Inter, IBM_Plex_Mono } from "next/font/google";
import { cn } from "@/lib/utils";
import { AppProviders } from "@/components/shell/AppProviders";

/* Fuentes del sistema de diseño: se cargan solo acá, con next/font (sin
   @import a Google Fonts). Inter es variable (todos los pesos); IBM Plex Mono
   no lo es, por eso lleva `weight`. --font-sans mantiene el nombre que ya
   leen el body legacy y el bloque .theme de shadcn. */
const inter = Inter({ subsets: ["latin"], variable: "--font-sans", display: "swap" });
const plexMono = IBM_Plex_Mono({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-mono",
  display: "swap",
});

/* Íconos explícitos (no la convención app/icon.*): los genera
   ops/brand/build-brand.mjs en public/. No debe existir app/favicon.ico. */
export const metadata: Metadata = {
  title: "Factum",
  description: "Adquisición forense de evidencia digital en dispositivos móviles",
  icons: {
    icon: [
      { url: "/logo-app.ico", sizes: "any" },
      { url: "/icon.svg", type: "image/svg+xml" },
      { url: "/icon-512.png", type: "image/png", sizes: "512x512" },
    ],
    apple: [{ url: "/apple-icon.png", sizes: "180x180" }],
  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
};

/* Anti-FOUC: arranca en oscuro salvo que el usuario haya elegido claro
   (localStorage['ev-theme'] === 'light'). Si localStorage no está disponible
   (modo privado, política), queda en oscuro. */
const themeScript = `
  (function(){
    var light = false;
    try { light = localStorage.getItem('ev-theme') === 'light'; } catch (e) {}
    if (!light) document.documentElement.classList.add('dark');
  })();
`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="es" className={cn("font-sans", inter.variable, plexMono.variable)} suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body>
        <AppProviders>{children}</AppProviders>
      </body>
    </html>
  );
}
