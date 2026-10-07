import type { InputTextareaPassThroughOptions } from "primereact/inputtextarea";
import { cn } from "@/lib/utils";
import { inputRootClasses } from "./inputtext";

/**
 * InputTextarea: el mismo look que InputText, sin resize manual. Como en
 * `inputtext`, `props.className` va al final para que gane el consumidor.
 */
export const inputtextarea: InputTextareaPassThroughOptions = {
  root: ({ props, context }) => ({
    className: cn(inputRootClasses({ props, context }), "resize-none min-h-[5rem]", props.className),
  }),
};
