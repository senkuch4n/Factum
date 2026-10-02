// Beacon de Faro (el sistema de tokens de soporte): un punto de luz que converge
// en un haz. Se usa como disparador de soporte en cualquier sistema
// integrado — la idea es que el mismo símbolo se reconozca en todos lados,
// en vez de un ícono genérico (salvavidas, signo de pregunta) que no dice
// nada sobre a dónde te lleva.
export function FaroIcon({ className, style }: { className?: string; style?: React.CSSProperties }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true" className={className} style={style}>
      <circle cx="12" cy="7" r="2.3" fill="#f2b544" />
      <path d="M12 7 L16.3 19.5" stroke="currentColor" strokeWidth="3.4" strokeLinecap="butt" />
      <path d="M12 7 L7.7 19.5" stroke="currentColor" strokeWidth="3.4" strokeLinecap="butt" />
    </svg>
  );
}
