import type { InputTextareaPassThroughOptions } from "primereact/inputtextarea";
import { cn } from "@/lib/utils";
import { inputRootClasses } from "./inputtext";

/** InputTextarea: el mismo look que InputText, sin resize manual. */
export const inputtextarea: InputTextareaPassThroughOptions = {
  root: ({ props, context }) => ({
    className: cn(inputRootClasses({ props, context }), "resize-none min-h-[5rem]"),
  }),
};
