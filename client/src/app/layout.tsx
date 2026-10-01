import type { Metadata, Viewport } from "next";
import "./globals.css";
import { Geist, Inter } from "next/font/google";
import { cn } from "@/lib/utils";
import { ThemeProvider } from "@/components/ThemeProvider";
import { TooltipProvider } from "@/components/ui/tooltip";
import { SystemFooter } from "@/components/SystemFooter";
import { Toaster } from "sonner";

const geist = Geist({ subsets: ["latin"], variable: "--font-sans" });
const inter = Inter({ subsets: ["latin"], variable: "--font-inter" });

export const metadata: Metadata = {
  title: "Factum - Gabinete Forense Digital",
  description: "Ministerio Público Fiscal · Inspecciones de dispositivos moviles",
  icons: {
    icon: "/logo-app.ico",

  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
};

/* Prevents FOUC by applying theme class before React hydrates */
const themeScript = `
  (function(){
    var t = localStorage.getItem('ev-theme');
    var d = window.matchMedia('(prefers-color-scheme: dark)').matches;
    if (t === 'dark' || (!t && d)) document.documentElement.classList.add('dark');
  })();
`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="es" className={cn("font-sans", geist.variable, inter.variable)} suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body>
        <ThemeProvider>
          <TooltipProvider>{children}</TooltipProvider>
          <SystemFooter />
        </ThemeProvider>
        <Toaster
          position="bottom-right"
          toastOptions={{
            classNames: {
              toast: "font-sans text-sm",
            },
          }}
        />
      </body>
    </html>
  );
}
