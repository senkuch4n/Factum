"use client";

/**
 * Accordion — adaptado de scrollxui (docs/components/accordion).
 *
 * El original usa `@radix-ui/react-accordion` con tokens Tailwind v4, un ícono
 * "+" que rota y una clase `animate-shake-smooth` decorativa. Acá se reconstruye
 * sobre `@base-ui/react` (dep ya instalada) con tokens de factum, chevron de
 * lucide y una transición de altura sobria (sin shake).
 *
 * Uso:
 *   <Accordion>
 *     <AccordionItem value="a" title="Título">contenido…</AccordionItem>
 *   </Accordion>
 */

import * as React from "react";
import { Accordion as BaseAccordion } from "@base-ui/react/accordion";
import { ChevronDown } from "lucide-react";
import { cn } from "@/lib/utils";

export function Accordion({
  children,
  className,
  defaultValue,
  multiple = false,
}: {
  children: React.ReactNode;
  className?: string;
  defaultValue?: string[];
  multiple?: boolean;
}) {
  return (
    <BaseAccordion.Root
      defaultValue={defaultValue}
      multiple={multiple}
      className={cn("divide-y", className)}
      style={{ borderColor: "var(--border)" }}
    >
      {children}
    </BaseAccordion.Root>
  );
}

export function AccordionItem({
  value,
  title,
  children,
  className,
}: {
  value: string;
  title: React.ReactNode;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <BaseAccordion.Item value={value} className={className} style={{ borderColor: "var(--border)" }}>
      <BaseAccordion.Header>
        <BaseAccordion.Trigger
          className={cn(
            "group flex w-full items-center justify-between gap-3 py-3 text-left text-[13px] font-medium",
            "transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--blue-lg)]",
          )}
          style={{ color: "var(--text-primary)" }}
        >
          {title}
          <ChevronDown
            className="h-4 w-4 shrink-0 transition-transform duration-200 group-data-[panel-open]:rotate-180"
            style={{ color: "var(--text-muted)" }}
            aria-hidden="true"
          />
        </BaseAccordion.Trigger>
      </BaseAccordion.Header>
      <BaseAccordion.Panel
        className={cn(
          "overflow-hidden text-[13px]",
          "h-[var(--accordion-panel-height)] transition-[height] duration-200 ease-out",
          "data-[starting-style]:h-0 data-[ending-style]:h-0",
        )}
        style={{ color: "var(--text-secondary)" }}
      >
        <div className="pb-3 pt-0.5">{children}</div>
      </BaseAccordion.Panel>
    </BaseAccordion.Item>
  );
}

export { BaseAccordion };
