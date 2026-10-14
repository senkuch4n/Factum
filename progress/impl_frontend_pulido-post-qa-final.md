# impl_frontend — pulido-post-qa-final (HU #73)

**Estado:** done (M1 **no reproduce**: sin §8.1 y sin §9.4) · **Rama:** `feat/pulido-post-qa-final` · **Sin commit**
(lo hace el orquestador) · **Fecha:** 2026-10-13 · **Implementador:** implementer-frontend (Claude Opus 5.5)

Alcance: checklist §10.2 de `Refactorizaciones/pulido-post-qa-final.md`, **solo `client/`**. No se tocó `agent-ui/`,
`server/`, `docs/`, `progress/sesiones/` ni el Project. No se reinició el `next dev` de :3000 ni el Tatana de :8765.

---

## 1. M1: medición (D-U1 = B) → **NO REPRODUCE**

### Cómo se midió

- **Tatana:** el build de `develop` (`62618df`, sin cambios de esta HU), compilado desde un `git worktree` en el
  scratchpad y corriendo en **mock en :8766** con `--data` temporal propio. El :8765 real no se tocó.
- **Videos:** dos MKV tipo scrcpy generados con ffmpeg (H.264 720×1600 a 30 fps, unos 8 Mbit/s, más Opus 128k):
  - `rec_180.mkv`: 3 min, 184 MB.
  - `rec_600.mkv`: 10 min, 612 MB.
- **Navegador:** Google Chrome (canal estable, por los códecs propietarios) con Playwright. Pestaña **visible**
  (`document.visibilityState === "visible"`, `hidden === false` en cada corrida). `<video preload="metadata">`, como
  la `VideoCard`.
- **Escenarios** (2 corridas cada uno). En cada corrida se copia el archivo a la raíz de Tatana como si fuera una
  grabación recién detenida:
  - **`current`**: reproduce el flujo agent de hoy. Monta el `<video>` con la URL de la raíz
    (`:8766/files/<nombre>`) y **a la vez** llama a `POST /cases/{id}/evidence/import` (move + SHA-256). Cuando el
    import responde, remonta con la URL del caso (`:8766/cases/{id}/files/<nombre>`), que es lo que hace `key={url}`
    al pasar a `storedInCase`.
  - **`root_only`**: solo la raíz. **Sin Range**: responde `200` a un pedido con `Range`, sin `Accept-Ranges`, con
    `Content-Disposition: attachment` y con `Content-Type: application/octet-stream` (`.mkv` no está en el
    provider), verificado con `curl -D -`.
  - **`case_only`**: solo la URL del caso, **con Range** (`enableRangeProcessing`).

### Resultados (ms desde el montaje; `meta` = `loadedmetadata`, la duración ya está; `frame` = `loadeddata`, el primer cuadro)

| Video | Escenario | Corrida | URL final | Accept-Ranges | meta | primer cuadro | Notas |
|---|---|---|---|---|---|---|---|
| 3 min | current | 0 | caso | sí | 523 | 523 | `<video>` de la raíz: error code 4 a los 361 ms (404, el archivo ya se había movido). Import: 512 ms |
| 3 min | current | 1 | caso | sí | 111 | 112 | raíz: error a los 36 ms. Import: 107 ms |
| 3 min | root_only | 0 | raíz | **no** | 31 | 458 | |
| 3 min | root_only | 1 | raíz | **no** | 17 | 26 | |
| 3 min | case_only | 0/1 | caso | sí | 9 / 11 | 9 / 11 | |
| 10 min | current | 0 | caso | sí | 290 | 290 | raíz: error a los 9 ms. Import: 285 ms |
| 10 min | current | 1 | caso | sí | 303 | 303 | raíz: error a los 8 ms. Import: 299 ms |
| 10 min | root_only | 0/1 | raíz | **no** | 15 / 15 | 24 / 23 | |
| 10 min | case_only | 0/1 | caso | sí | 10 / 10 | 10 / 11 | |

Duración reportada: 180 s y 600 s en todos los casos. **Máximo: 0,52 s, muy por debajo del umbral de 3 s → M1 "no
reproduce".** Se saltearon §8.1 (`VideoCard`/`CaptureStep`, `previewPending`) y, por D-T9, tampoco corresponde §9.4
(Range en `GET /files`). **No hace falta relanzar al backend para §9.4.**

Observaciones para el orquestador y el usuario (no cambian el veredicto):
- **La carrera de §3 existe:** en el flujo `current`, el `<video>` de la raíz falla **siempre** con 404 porque el
  import ya movió el archivo. Se recupera solo porque la URL cambia al pasar a `storedInCase`. Con la pestaña visible,
  esa recuperación es inmediata. El síntoma de la QA (pestaña oculta) es coherente con que Chrome posterga la carga de
  medios en segundo plano.
- La medición es aproximada (D-U1 = B): mock, sin scrcpy ni remux reales, SSD de una Mac. En un Windows con disco
  lento, el SHA-256 de 600 MB tarda más, pero mientras tanto el `<video>` ya está en error y se recarga al terminar el
  import. **La confirmación física con el Samsung queda para la prueba manual de §11.2.1.**

---

## 2. Archivos tocados (`client/`)

| Archivo | Ítem | Qué |
|---|---|---|
| `src/components/shell/AppNavbar.tsx` | M2 | `SECTIONS` suma `Icon` (`ClipboardList` y `ChartColumn` de lucide 1.x; la app no tenía íconos propios para estas secciones). Por debajo de **`md`** se ve el `<Link>` solo con ícono, `h-9 w-9`, `aria-label`, `FxTip side="bottom"` y `aria-current`. Desde `md`, el `<Link>` con texto de siempre. Centrado seguro: el contenedor del centro ya no lleva `justify-center` y la `<nav>` usa `mx-auto min-w-0`. |
| `src/components/CaseFormStep.tsx` | B1 | `tipo_causa`: `placeholder: "Ej.: disciplinaria, civil, penal"` y el `hint` de §8.3, textuales. |
| `src/components/form/CatalogAutoComplete.tsx` | B2 | `pt.input.root.onBlur`: cierra el panel si `relatedTarget` es enfocable y está fuera de `getElement()` y de `getOverlay()` (§8.4, sin timers). |
| `src/app/dashboard/page.tsx` | B3, D9 | `go(n)`: si `step === 3 && n !== 3`, apaga `isResuming` y limpia `screenBlock`. `useEvidenceNotices(…, { agentStorage: currentStorage === "agent" })`. D9: estado `screenBlock` (con `serial`), `handleScreenshot(opts?)`, `runRecordAction` traduce `screen_blocked`, `runScreenBlockAction(skip)` (Reintentar o Capturar/Grabar igual) y la limpieza. |
| `src/hooks/useEvidenceNotices.ts` | B3 | Tercer parámetro `opts?: { agentStorage?: boolean }`. Con `agentStorage` y `!storedInCase` hace `continue` sin marcar `seenRef`. La cola guarda solo `name` y `pump` resuelve la URL con `filesRef`. El flujo servidor queda igual. |
| `src/components/CaseHistory.tsx` | B4 | `countText` pluraliza por `cases.length`. |
| `src/lib/agent.ts` | D9 | `AgentErrorCode` suma `"screen_off" \| "screen_locked"`. `AgentErrorBody.screen_state?: "off" \| "locked"`. Nuevo `ScreenCheckOptions`. `takeScreenshot(…, opts?)` hace `q.set("skip_screen_check", "true")`, lee el cuerpo una sola vez y lanza `AgentError` solo para `screen_*`; el resto lanza el `Error` de siempre. `startRecording(…, caseId, opts?)` manda `skip_screen_check: true` solo si viene. `screenBlockOf(e)` exportado. |
| `src/lib/agent-messages.ts` | D9 | `screenBlockMessage(action)` (los textos de §8.7) y los dos códigos en `agentErrorMessage`. |
| `src/hooks/useRecording.ts` | D9 | `RecordOutcome` suma `{ kind: "screen_blocked"; state }`. `startRecording(…, iosMode?, startOpts?)`: si es `screen_*`, no llama a `onError` y devuelve `screen_blocked`. `handleToggleRecord(device, onError, startOpts?)` lo reenvía en la rama Android. `opInFlightRef` no cambia. |
| `src/components/CaptureStep.tsx` | D9 | Props `screenBlock`, `onScreenRetry`, `onScreenForce` y `onDismissScreenBlock`. Aviso arriba de la botonera Pantalla/Grabar (`key={action}`). `busy` = `loading.screenshot` o `recordStarting`. Al irse el aviso, el foco vuelve al tile de la acción si quedó en `<body>`. |
| `src/components/capture/ScreenBlockNotice.tsx` (nuevo) | D9 | `FxBanner tone="warn" role="alert"`, ícono `Lock` (`aria-hidden`), "Reintentar" (primario, foco al aparecer) y "Capturar igual"/"Grabar igual" (secundario), cierre con la X. |

**No se tocaron** (M1 no reproduce): `VideoCard.tsx` y la parte de `previewPending` en `CaptureStep.tsx`.

## 3. Contrato compartido (§6): coincide con la SDD

| Campo | Client | Verificado contra el Tatana del backend |
|---|---|---|
| `skip_screen_check` (query `"true"`) | `agent.takeScreenshot` | `POST …/screenshot?…&skip_screen_check=true` → 200 |
| `skip_screen_check` (body bool) | `agent.startRecording` | body `…,"skip_screen_check":true}` → 200; sin el campo → 409 |
| `code: "screen_off" \| "screen_locked"` | `AgentErrorCode`, `screenBlockOf` | 409 con `off` y con `locked` |
| `screen_state: "off" \| "locked"` | `AgentErrorBody.screen_state` | leído por `screenBlockOf` |
| `screen_check_v1` | **no se lee** (D-T5) | aparece en `/health` del Tatana nuevo y no en el de develop |

## 4. Verificación

### Comandos de §11.1

```
$ cd client && npx tsc --noEmit
(sin salida, exit 0)

$ cd client && npm run lint
→ no existe el script `lint` en client/package.json (y no hay eslint instalado): no aplica.
```

### Entorno de prueba (todo en el scratchpad y ya desarmado)

- Copia de `client/` en el scratchpad (`node_modules` clonado con APFS) con `next dev -p 3001`,
  `NEXT_PUBLIC_AGENT_URL=http://localhost:8767`. Sirve para no reiniciar el :3000 del usuario. El build de `develop`
  quedó en :3002 como línea de base para M2.
- Tatana en mock en **:8767**, compilado desde una copia de `server/` con los cambios del implementer-backend (el
  `obj/` del checkout principal no se tocó) y con `Agent__MockAndroidScreenState=off|locked|unknown|on`. Para la
  compatibilidad, el build de `develop` en el mismo puerto.
- Backend :8080 compartido (`Auth:Mode=dev`), usuario QA DNI `99000001`. Playwright con
  `--disable-web-security` solo en ese navegador de prueba, porque el CORS del backend no incluye :3001.

### D9 (Playwright, paso 3 retomando un caso QA del flujo agent)

| Escenario | Resultado |
|---|---|
| `off` → "Pantalla" | 409. Aviso: "La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para capturar." Foco en "Reintentar". **Sin** banner de error global y **sin** `capture_failed` (solo se ve `POST /api/agent-events` de `startup`). No se creó ningún archivo. |
| "Reintentar" con doble clic (pantalla todavía apagada) | **Un solo** `POST …/screenshot` → 409. El aviso sigue y el foco sigue en "Reintentar" (por eso los botones usan `aria-disabled` y no `disabled`, ver §6). |
| "Capturar igual" | `…&skip_screen_check=true` → 200. Se agrega la captura, se manda `agent-events: screenshot`, el aviso se va y el foco vuelve a "Pantalla". |
| `off` → "Grabar" | 409. Aviso "…para grabar." con "Grabar igual". Tab desde "Reintentar" lleva a "Grabar igual" y Enter → body con `skip_screen_check: true` → 200. Empieza a grabar (`capture_start`) y el foco queda en "Detener". |
| Cierre con la X | El aviso se va y el foco vuelve a "Pantalla". |
| `locked` | El mismo recorrido, con los mismos resultados (409 → aviso, Reintentar → 409, igual → 200). |
| `unknown` | Captura y graba sin aviso (200). Tatana loguea "Chequeo de pantalla no concluyente (no_signals); se captura sin chequeo". |
| `on` | Captura y graba sin aviso. |
| **Tatana de develop** (sin `screen_check_v1`) | Captura, graba y detiene como hoy, sin errores nuevos (el flag se ignora). |

### B1–B4 (Playwright)

- **B1:** en el paso 2 se ve el `hint` "Solo el tipo, sin la palabra «causa»…" y el placeholder nuevo.
- **B2:**
  - Abrir la lista de "Parte denunciante" (1 panel) y hacer clic en el input de "Parte denunciada" → **0 paneles**.
  - Abrir "Parte denunciada" y salir con Tab → 0 paneles.
  - Clic con el mouse en una opción: el valor cambia de "QA denunciada" a "QA proponente", el panel se cierra y el
    foco queda en el input. La selección sigue funcionando.
- **B3, banner:** al retomar, el banner "Retomando inspección" está en el paso 3. Ir al paso 2 y volver con
  "Cancelar" → no aparece.
- **B3, 404:** el `fetch` de `isMostlyBlack` ya no pide la raíz: en Network no hay 404 de tipo `fetch`. **Queda un
  404 de tipo `image`** de `/files/screenshot_…png`, que es el `<img>` de la miniatura o del escenario montado con la
  URL de la raíz antes del move. Es el caso que prevé §8.5. **No agregué el `onError` porque ya existe el
  equivalente:** `SafeImg` guarda el `src` que falló (`erroredSrc`). Cuando el archivo pasa a `storedInCase`, el `src`
  cambia a `agent.caseFileURL` y la imagen se vuelve a pedir una vez y carga. Un `onError` propio haría lo mismo dos
  veces. El 404 es una línea de consola sin efecto visible (el placeholder dura unos 100 ms). Pasa lo mismo con
  `[media] /files/grabacion_….mkv` de la `VideoCard` (la carrera de M1, que se recupera sola).
- **B4:** filtrando "QA-ROBUSTEZ" con un solo caso en total → "1 de 1 resultado" (singular por `cases.length`).

### M2: anchos verificados (dashboard en historial con chip de Tatana, tema y usuario; Métricas sin usuario)

Posiciones en px. `brand.r` = borde derecho de la marca y `right.l` = borde izquierdo del bloque derecho.

| Ancho | Dashboard nuevo | Dashboard develop | Métricas nuevo | Métricas develop |
|---|---|---|---|---|
| 360 | íconos 102–178, marca hasta 65, derecha desde 215: **OK** | texto 40–240: **pisa la marca y el chip** | íconos 120–196: OK | pisa |
| 390 | íconos 117–193: OK | pisa | íconos 135–211: OK | OK |
| 640 | íconos 125–201: OK | pisa la marca | íconos 259–335: OK | OK |
| 768 | texto 127–327, derecha desde 381: OK | igual | texto: OK | igual |
| 1024 | texto 188–388 | **idéntico** | texto 322–522 | **idéntico** |
| 1440 | texto 396–596 | **idéntico** | texto 530–730 | **idéntico** |

`scrollWidth` igual al ancho en todos los casos (sin scroll horizontal). **Decisión D-T11:** con el corte en `sm`, a
640 px el dashboard desbordaba las píldoras con texto sobre el chip "2 equipos conectados" (Métricas terminaba en 290
y el bloque derecho empezaba en 253). Por eso subí el corte a **`md`**, como prevé la SDD. Revisé las capturas de 390
(dashboard), 360 (Métricas) y 768 (dashboard). Quedaron en el scratchpad (`shots/m2_*`) y no van al repo.

### Datos de desarrollo (regla dura)

- **Creado por la prueba y borrado solo por `_id`** en `factum_dev`:
  - el caso `9715f4c1-9e2b-43bd-a65f-33aa2b0a3cd4` (QA-ROBUSTEZ-D9);
  - sus 26 `case_events` y 27 `agent_events`, todos con `CaseId` de ese caso y posteriores al inicio de la prueba;
  - 5 `catalog_entries` creadas a las 23:45:05 (ids listados antes de borrar);
  - `expert_profiles` `_id: "99000001"`, creado por la prueba (antes `exists: false`).
- **No se tocó lo que ya existía:**
  - `users` 625d… (DNI 99000001, de 2026-10-06);
  - `catalog_seeds` 99000001 (de las 19:01);
  - `catalog_entries` "Teléfono celular" (de las 19:01).
- Los archivos de las pruebas quedaron solo en las carpetas `--data` temporales del scratchpad, ya borradas.
  `~/Factum/Evidencia` no se usó (no se generaron ZIP). Se borraron el worktree, las copias y los procesos de prueba
  (:3001, :3002, :8766, :8767, :8790).

## 5. Skills invocadas y hallazgos

- **`ui-ux-pro-max`** (antes del JSX final). Consultas UX:
  - "icon only button accessible label": no aplicar solo color → la pestaña activa conserva fondo y peso, no solo color.
  - "loading placeholder retry timeout": feedback de carga sin parpadeo → spinner solo en el botón tocado y
    `aria-busy`.
  - "alert banner focus management": foco visible en todo control → Prime `pt` + `fx-focus-ring`.

  Hallazgo aplicado: el estado de las pestañas no se transmite solo con color. Hallazgo no aplicado: el mínimo de 44 px
  de toque. Las pestañas de ícono quedan en 36 px (`h-9 w-9`) porque lo fija la SDD y son coherentes con el resto de
  la píldora (tema y usuario `h-10`, sin lugar a 360 px). Los botones del aviso sí usan `min-h-11` en táctil, como los
  demás avisos del paso 3.
- **`senior-frontend`**:
  - Estado derivado en vez de efectos para invalidar el aviso (`activeScreenBlock` por serial, `deviceOffline` y
    `agentOnline`), más un único efecto que lo olvida si queda viejo.
  - URL resuelta en el momento de usar (`filesRef`).
  - Sin timers en B2.
  - Cuerpo de la respuesta leído una sola vez en `takeScreenshot`.
- **`3d-web-experience`** (como criterio): revisado, sin hallazgos aplicables. No se agregó 3D ni animaciones; el
  aviso usa la entrada `fx-fade-in` existente de `FxBanner` (con `motion-safe`).
- **`ui-styling`** (se tocaron clases Tailwind): mobile-first (`inline-flex md:hidden` / `hidden md:inline-flex`),
  clases literales y sin nombres dinámicos, tokens `fx-*` existentes.
- **`mblode-agent-skills-ui-animation`**: no se invocó porque no hay transiciones ni gestos nuevos (el fade del
  banner ya existía).
- **`web-design-guidelines`** (autochequeo final sobre `AppNavbar.tsx`, `ScreenBlockNotice.tsx`, `CaptureStep.tsx` y
  `CatalogAutoComplete.tsx`, con las reglas de vercel-labs/web-interface-guidelines):
  - Pasa: botones de ícono con `aria-label`, íconos decorativos con `aria-hidden`, `<Link>` para navegar, foco visible,
    hover en las pestañas, mensaje de error con la acción a seguir ("Encendela y desbloqueala…"), sin
    `transition: all` y `motion-safe` en spinners.
  - Única desviación: la guía pide terminar los placeholders con `…`, pero el de `tipo_causa` queda **sin** `…`
    porque la SDD (§8.3) lo fija textual.

## 6. Decisiones no obvias

1. **Corte de M2 en `md` y no en `sm`** (D-T11, ver la tabla de M2).
2. **`aria-disabled` en vez de `disabled` en los botones del aviso:** con `disabled`, Chrome saca el foco del botón
   mientras el pedido está en vuelo y el teclado queda en `<body>`. Con `aria-disabled`, más el no-op en el click y
   la opacidad, el botón sigue deshabilitado para la tecnología asistiva y el foco no se pierde. El doble toque lo
   frenan además `screenshotInFlightRef` y `opInFlightRef` (verificado: un solo pedido).
3. **El aviso guarda el `serial`.** Deja de mostrarse con otro celular, con el celular desconectado
   (`deviceOffline`) o con Tatana caído, y un efecto lo borra para que no reaparezca si vuelve el mismo equipo. Además
   se limpia al salir del paso 3 (`go`), en el reset del wizard, con la X y con cualquier captura o inicio exitoso.
4. **Anuncio único por aparición:** `ScreenBlockNotice` se monta con `key={action}`. Un reintento que vuelve a dar
   409 no lo remonta: el texto no cambia y el lector no lo repite. Si cambia de captura a grabación, se remonta, se
   anuncia y vuelve a tomar el foco.
5. **"Grabar igual" y "Reintentar" solo inician:** si mientras tanto ya hay una grabación propia (`isRecording`),
   descartan el aviso y no llaman a `handleToggleRecord`, que la detendría.
6. **`screen_blocked` en `useRecording`** también resetea `iosRecordMode` si llegara en iOS. No debería llegar
   (Tatana no chequea iOS), pero así el estado queda coherente.
7. **B3, captura cuyo guardado falló (flujo agent):** como nunca pasa a `storedInCase`, no se chequea si salió negra.
   Es lo que pide §8.5 (`continue` sin marcar `seenRef`). El archivo sigue en la raíz y el error de guardado ya se
   muestra aparte.
8. **El fallback de vista previa del paso 2** (`CaseFormStep` → `agent.takeScreenshot(…, null)`, solo con un Tatana
   sin `/preview`) no se tocó. Esos Tatana son anteriores al chequeo, así que nunca llega un 409 por ahí.

## 7. Observaciones fuera de alcance (no tocadas)

- Después de tocar el tile "Pantalla", el foco cae en `<body>` porque el tile se deshabilita (`disabled`) mientras
  captura. Pasa también en `develop`. Si se quiere, va como HU aparte.
- Release de Tatana: ver §12 de la SDD. No hace falta §9.4 porque M1 no se reprodujo.
