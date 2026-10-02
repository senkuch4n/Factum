# impl_frontend — grabacion-android-windows

**Estado:** done (sin commit, como pidió el orquestador)
**App:** solo `client/`. No se tocó `agent-ui/`, `server/`, `deploy/`, `backlog.json` ni `progress/current.md`.
**Rama:** `feat/grabacion-android-windows`

## Checklist §10.4

- [x] F1. Leí `client/AGENTS.md`. El cambio no usa APIs de Next (solo `fetch` y un hook de React), así que no hizo falta consultar la guía de `node_modules/next/dist/docs/`.
- [x] F2. `client/src/lib/agent.ts`: `AGENT_UNREACHABLE` y `readAgentError(res, fallback)`, con el código exacto de §7, aplicados en `startRecording` y `stopRecording`. Si `fetch` lanza, se tira `AGENT_UNREACHABLE`. Si `!res.ok`, se tira `readAgentError` con los fallbacks `"Error iniciando grabación"` y `"Error deteniendo grabación"`. Los demás métodos no se tocaron.
- [x] F3. `client/src/hooks/useRecording.ts`: se sacó el `.catch(() => null)` del stop y se agregó un `catch` que llama a `onError("No se pudo guardar la grabación: <motivo>")`. `setRecording(false)` y `setIosRecordMode(null)` se siguen ejecutando después del try. La rama de start no cambió: ya hacía `onError(e.message)` y ahora le llega el texto de Tatana. iOS usa los mismos métodos y también se beneficia.
- [x] F4. Skills: abajo.
- [x] F5. `npx tsc --noEmit`: limpio.
- [x] F6. Este archivo.

## Archivos tocados

- `client/src/lib/agent.ts`
- `client/src/hooks/useRecording.ts`

## Contrato compartido (§5.1): coincide

- Leo solo `error` (string, en minúscula) del cuerpo no-2xx de `POST /devices/{serial}/record/start` y `/record/stop`. Lo leo de forma defensiva: `typeof data.error === "string"` y sin vacíos.
- No se agregó ningún tipo en `client/src/types/`.
- El client **no** consume `/health.tools`, como dice la SDD. No hay tipo TS para eso.

## Verificación

- `cd client && npx tsc --noEmit` → exit 0, sin salida.
- `npm run build` → **lo corrí en un `git worktree` temporal en el scratchpad**, no en el checkout principal, para no pisar el `.next` del `npm run dev` del usuario (es una regla dura del agente). Le copié los 2 archivos y corrí `npm ci`. Dio `✓ Compiled successfully` y TypeScript OK, con las rutas `/`, `/_not-found`, `/dashboard` y `/design-system` estáticas. El worktree se borró después (`git worktree remove --force` + `prune`) y `git worktree list` solo muestra el principal.
- Chequeo de lógica: importé el `agent.ts` real con `node --experimental-strip-types` (Node 24) y un `fetch` simulado, sin red, sin Mongo y sin Tatana real:
  - 500 `{"error":"No hay una grabación en curso."}` → start y stop: `No hay una grabación en curso.`
  - 500 con cuerpo que no es JSON → `Error iniciando grabación` / `Error deteniendo grabación`
  - 500 con `{"error":"  "}` → los mismos fallbacks
  - `fetch` que lanza `TypeError` → `No se pudo conectar con Tatana. Revisá que esté abierto en esta PC.`
  - Del lado del hook, el stop queda como `No se pudo guardar la grabación: No hay una grabación en curso.`, que es el banner esperado en V9(a).
- No corrí e2e con navegador: el cambio es de lógica y no tiene UI nueva. No levanté dev servers ni procesos. No escribí en MongoDB.
- `client/next-env.d.ts`: no quedó modificado (`git status` no lo lista).
- V9 (manual con el agente en mock) queda para el usuario o el reviewer, según §11.1.

## Cómo se ve y accesibilidad

- No hay cambios visuales. El aviso sale en el `FxBanner tone="error"` que ya existe para `globalError` en `client/src/app/dashboard/page.tsx` (L586 y L662). Ese banner ya tiene `role="alert"`, con aria-live assertive vía el pt de Prime, ícono `aria-hidden` y un botón de cierre con `aria-label`. Por eso es accesible sin tocar nada.
- Los textos de Tatana (E1, E4, E6) ya incluyen el siguiente paso ("Actualizá o reinstalá Tatana…"), y `AGENT_UNREACHABLE` también ("Revisá que esté abierto…"). Eso cumple la regla de "error con fix/next step".

## Skills invocados

- **ui-ux-pro-max**: lo invoqué y busqué `"error message clarity cause" --domain ux`. La regla que aplica es "Error messages must be announced → role=alert / aria-live", y ya se cumple con `FxBanner`. Sin hallazgos aplicables más allá de eso.
- **senior-frontend**: lo invoqué. Usé `try/catch` alrededor del `fetch` para distinguir "no hay red" de "respuesta de error". Elegí `let res: Response` en vez de un `.catch` encadenado para no mezclar los dos casos. El `catch` del stop no vuelve a lanzar, así el `finally` y el reseteo del estado siempre corren. Sin otros hallazgos aplicables.
- **3d-web-experience**: lo invoqué como criterio. No se agregó 3D ni animaciones; la animación de entrada del banner ya existía y respeta `motion-safe`. Sin hallazgos aplicables.
- **web-design-guidelines**: lo invoqué. Bajé las reglas de `vercel-labs/web-interface-guidelines` y revisé `agent.ts`, `useRecording.ts` y el uso de `FxBanner` en `dashboard/page.tsx`. Cumplen "async updates need aria-live" (role=alert) y "error messages include fix/next step". Sin hallazgos que corregir.

## Observación (fuera de scope, no se tocó)

Si el mismo error se repite con el banner abierto, `setGlobal` recibe el mismo string, React no vuelve a renderizar y el lector de pantalla no lo anuncia de nuevo. Ya pasaba antes con cualquier `globalError`. Si se quiere, se puede ver en otra HU (por ejemplo, limpiar y volver a setear, o usar una key).
