import type { ElementType } from "react";

export interface Brand {
  id: string;
  name: string;
  emoji: string;
  note: string;
  steps: Step[];
}

export interface Step {
  icon: ElementType | string;
  title: string;
  detail: string;
  mockup?: Mockup;
  tip?: string;
}

export interface Mockup {
  header: string;
  section?: string;
  rows: { text: string; value?: string; highlight?: boolean }[];
}

export type IOSStep =
  | { type: "step"; icon: ElementType | string; title: string; detail: string; tip?: string; mockup?: Mockup }
  | { type: "dialog"; icon: ElementType | string; title: string; detail: string; tip?: string; dialog: { title: string; message: string; confirm: string } }
  | { type: "terminal"; icon: ElementType | string; title: string; detail: string; tip?: string; command: string };

export type AndroidStep =
  | { type?: "step"; icon: ElementType | string; title: string; detail: string; mockup?: Mockup; tip?: string }
  | { type: "mtp"; icon: ElementType | string; title: string; detail: string; tip?: string }
  | { type: "trust"; icon: ElementType | string; title: string; detail: string; tip?: string };
