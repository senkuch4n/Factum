/**
 * Id de esta carga de página (wizard-concurrencia-pestanas DT2). Viaja como
 * `origin_id` en los pedidos a Tatana y en los mensajes entre pestañas, para
 * que cada pestaña sepa qué eventos pidió ella.
 *
 * Es por **carga**, no por `sessionStorage`: "Duplicar pestaña" copia el
 * `sessionStorage` y dejaría dos pestañas con el mismo id. Recargar cambia el
 * id (para eso los eventos también llevan `case_id`, ver `agent-event-filter`).
 */

let originId: string | null = null;

function randomHex(bytes: number): string {
  const buf = new Uint8Array(bytes);
  crypto.getRandomValues(buf);
  return Array.from(buf, b => b.toString(16).padStart(2, "0")).join("");
}

/**
 * Id de la pestaña, generado una sola vez (lazy). `crypto.randomUUID` necesita
 * contexto seguro (`localhost`/HTTPS); en `http://<IP>` cae a 16 bytes al azar
 * en hex. Siempre matchea `^[A-Za-z0-9_-]{1,64}$` (lo valida Tatana).
 */
export function getTabOriginId(): string {
  if (originId) return originId;
  if (typeof crypto === "undefined") {
    // Solo en SSR (nunca se manda a Tatana desde el servidor): id efímero.
    return "ssr";
  }
  originId = typeof crypto.randomUUID === "function" ? crypto.randomUUID() : randomHex(16);
  return originId;
}
