import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { DesignSystemShowcase } from "@/components/design-system/DesignSystemShowcase";

export const metadata: Metadata = {
  title: "Sistema de diseño · Factum",
  robots: { index: false, follow: false },
};

/**
 * Showcase del sistema de diseño (tokens --fx-*, tipografía, radios y
 * componentes PrimeReact tematizados). Solo en desarrollo:
 * en `next build` se renderiza como 404.
 */
export default function DesignSystemPage() {
  if (process.env.NODE_ENV === "production") notFound();
  return <DesignSystemShowcase />;
}
