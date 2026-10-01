import type { PrimeReactPTOptions } from "primereact/api";
import { button } from "./button";
import { calendar } from "./calendar";
import { checkbox } from "./checkbox";
import { column } from "./column";
import { datatable } from "./datatable";
import { dialog } from "./dialog";
import { dropdown } from "./dropdown";
import { inputtext } from "./inputtext";
import { inputtextarea } from "./inputtextarea";
import { menu } from "./menu";
import { message } from "./message";
import { paginator } from "./paginator";
import { password } from "./password";
import { progressbar } from "./progressbar";
import { selectbutton } from "./selectbutton";
import { tag } from "./tag";
import { toast } from "./toast";
import { tooltip } from "./tooltip";

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
  password,
  message,
  tag,
  selectbutton,
  paginator,
  datatable,
  column,
  calendar,
  tooltip,
  inputtextarea,
  dropdown,
  progressbar,
  checkbox,
};
