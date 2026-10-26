import type { Metadata } from "next";
import { ExtractionDetailScreen } from "@/components/extraction/ExtractionDetailScreen";

export const metadata: Metadata = {
  title: "Extracción · Factum",
};

/**
 * `/extracciones/[id]` (extraccion-logica-android §10.5). En Next.js 16 los
 * `params` de una página dinámica son una `Promise`: se resuelven en el server
 * component y el `id` se pasa a la pantalla cliente.
 */
export default async function ExtractionDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ExtractionDetailScreen id={id} />;
}
