/** Import de texto crudo de Vite (`?raw`): el script de "Preparar esta PC" embebido en main (D-T10). */
declare module '*.sh?raw' {
  const s: string
  export default s
}
