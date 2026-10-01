import type { PrimeReactPTOptions } from "primereact/api";
import { button } from "./button";
import { dialog } from "./dialog";
import { inputtext } from "./inputtext";
import { menu } from "./menu";
import { toast } from "./toast";

/**
 * Preset pass-through de Factum para PrimeReact 10 en modo `unstyled`.
 * Estructura tomada del preset oficial `primereact/passthrough/tailwind`
 * (no se importa): todos los colores son clases `fx-*` → tokens `--fx-*`,
 * así un cambio de token o de `.dark` re-tematiza todos los componentes.
 * Cada HU de página que use un componente Prime nuevo lo suma acá.
 */
export const fxPassThrough: PrimeReactPTOptions = {
  button,
  inputtext,
  dialog,
  menu,
  toast,
};
