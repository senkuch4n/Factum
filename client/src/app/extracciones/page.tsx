import type { Metadata } from "next";
import { ExtractionsScreen } from "@/components/extraction/ExtractionsScreen";

export const metadata: Metadata = {
  title: "Extracciones · Factum",
};

/**
 * `/extracciones` (extraccion-logica-android §10.1). Server component mínimo:
 * la pantalla es cliente (sesión en localStorage, Tatana por WS/HTTP local),
 * igual que `/metricas`.
 */
export default function ExtractionsPage() {
  return <ExtractionsScreen />;
}
