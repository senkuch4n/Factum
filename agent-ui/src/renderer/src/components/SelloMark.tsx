interface SelloMarkProps {
  /** Lado en px (la marca es cuadrada). */
  size?: number
  className?: string
}

/**
 * Marca "Sello" de Factum (viewBox 64×64, misma geometría que
 * ops/brand/build-brand.mjs). Los colores salen de --sello-bg y
 * --sello-fg (globals.css), que cambian con el tema. Es decorativa: quien la
 * usa pone el texto accesible al lado.
 */
export function SelloMark({ size = 28, className }: SelloMarkProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 64 64"
      className={className}
      aria-hidden="true"
      focusable="false"
    >
      <rect width="64" height="64" rx="14" fill="var(--sello-bg)" />
      <path d="M20 14H40L46 20V23H30V29H40V37H30V50H20Z" fill="var(--sello-fg)" />
    </svg>
  )
}
