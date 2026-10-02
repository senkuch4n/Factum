import type { MenuPassThroughOptions } from "primereact/menu";
import { cn } from "@/lib/utils";

/**
 * Menu (popup o inline). El ítem activo por teclado lo marca Prime con
 * `data-p-focused="true"` en el <li>; el hover usa la misma superficie.
 * Los ítems `disabled` con template (identidad/organización del UserMenu)
 * llevan `className: "fx-menu-static"` y no se atenúan ni se resaltan.
 */
export const menu: MenuPassThroughOptions = {
  root: {
    className: cn(
      "min-w-[15rem] py-1 origin-top-right",
      "bg-fx-surface-2 text-fx-text border border-fx-border rounded-fx-lg shadow-fx-2",
    ),
  },
  menu: { className: "m-0 p-0 list-none outline-none" },
  menuitem: ({ context }) => ({
    className: cn(
      "transition-colors duration-fx-fast ease-fx",
      !context?.item?.disabled && "hover:bg-fx-surface-3 data-[p-focused=true]:bg-fx-surface-3",
    ),
  }),
  content: { className: "" },
  action: ({ context }) => ({
    className: cn(
      // Sin color propio: hereda del <li>, así `item.className` (p. ej.
      // "text-fx-danger" en "Cerrar sesión") define el color del ítem.
      "flex items-center gap-2.5 px-3.5 py-2.5 text-fx-body-sm font-medium no-underline select-none",
      context?.item?.disabled ? "opacity-50 cursor-not-allowed" : "cursor-pointer",
    ),
  }),
  icon: { className: "shrink-0" },
  label: { className: "min-w-0 truncate" },
  submenuHeader: { className: "m-0 px-3.5 pt-2 pb-1 text-fx-label uppercase text-fx-text-3" },
  separator: { className: "my-1 border-t border-fx-border" },
  transition: {
    timeout: { enter: 120, exit: 100 },
    classNames: {
      enter: "opacity-0 motion-safe:scale-95",
      enterActive: "!opacity-100 motion-safe:!scale-100 transition-[opacity,transform] duration-fx-fast ease-fx",
      exit: "opacity-100",
      exitActive: "!opacity-0 transition-opacity duration-100 ease-fx",
    },
  },
};
