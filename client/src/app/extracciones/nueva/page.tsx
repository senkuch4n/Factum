import type { Metadata } from "next";
import { NewExtractionWizard } from "@/components/extraction/NewExtractionWizard";

export const metadata: Metadata = {
  title: "Nueva extracción · Factum",
};

/** `/extracciones/nueva` (extraccion-logica-android §10.4): asistente de 4 pasos. */
export default function NewExtractionPage() {
  return <NewExtractionWizard />;
}
