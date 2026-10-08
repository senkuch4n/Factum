import { Fragment } from "react";
import { cn } from "@/lib/utils";

/** Retraso entre palabras del saludo (efecto escalonado). */
const WORD_STAGGER_MS = 70;

interface GreetingHeadlineProps {
  greeting: string;
  name: string;
}

/**
 * Saludo del dashboard con entrada palabra por palabra (opacity + blur + translateY).
 * Las palabras van aria-hidden y el h1 lleva el texto completo en aria-label, así el
 * lector de pantalla lo lee de corrido. Con prefers-reduced-motion no se anima.
 */
export function GreetingHeadline({ greeting, name }: GreetingHeadlineProps) {
  const words = [
    ...`${greeting},`.split(" ").map(text => ({ text, accent: false })),
    ...name.trim().split(/\s+/).map(text => ({ text, accent: true })),
  ];

  return (
    <h1
      aria-label={`${greeting}, ${name}`}
      className="text-fx-display text-fx-text break-words text-balance"
    >
      {words.map((word, i) => (
        // La key incluye el texto: si cambia el saludo (p. ej. a las 20 h) esa palabra vuelve a entrar.
        <Fragment key={`${i}-${word.text}`}>
          {i > 0 && " "}
          <span
            aria-hidden="true"
            className={cn(
              "inline-block motion-safe:animate-[fx-word-in_600ms_var(--fx-ease-out)_both]",
              word.accent && "text-fx-accent-text",
            )}
            style={{ animationDelay: `${i * WORD_STAGGER_MS}ms` }}
          >
            {word.text}
          </span>
        </Fragment>
      ))}
    </h1>
  );
}
