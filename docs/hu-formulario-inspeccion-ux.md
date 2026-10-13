# HU: Pulido de UX del formulario de inspección (paso 2 "Datos de la causa")

**Slug:** `formulario-inspeccion-ux` · **Issue:** #52
**Apps afectadas:** solo `client/` (web, Next.js): paso 2 del wizard de `/dashboard`.
**No cambian:** backend API (`Factum.Backend`), Tatana (`Factum.Agent`), `agent-ui/`, la
plantilla del informe, los contratos JSON.
**Origen:** el usuario pidió revisar la UI del formulario de inspección (2026-10-12). El
orquestador armó un mapa del paso con 7 hallazgos; esta HU toma 6 de ellos (el 7.º, el
tamaño de los componentes, queda como duda).

**Como** perito que carga los datos de la causa con el celular conectado a la PC
**quiero** que el paso 2 me diga la verdad sobre el estado del equipo, me avise si algo
falló y me deje reintentar, se envíe con Enter como cualquier formulario y no me esconda
los campos en una pantalla angosta
**para que** no cargue una causa creyendo que el equipo está conectado cuando no lo está,
no me quede sin saber por qué no hay captura o sugerencias, y complete el paso más rápido.

---

## Contexto

### Qué es "el formulario de inspección" (arqueología del 2026-10-12, sobre `5320ca6`)

No tiene ruta propia. Es el **paso 2 ("Datos de la causa")** del wizard de seis pasos que
vive dentro de `/dashboard` (`client/src/app/dashboard/page.tsx`, ~1117 líneas). El paso
está en estado de React, no en la URL. Lo renderiza `client/src/components/CaseFormStep.tsx`
(598 líneas), montado en `page.tsx:945-966` solo si hay `selDevice`.

Al paso 2 se llega:

- **Caso nuevo:** paso 1 (`DeviceConnect`) → elegir celular → paso 2 (`mode="create"`, POST).
- **Edición:** desde el paso 3 se vuelve al 2 con el caso ya creado (`go(2)`,
  `page.tsx:314-317`, `mode="edit"`, PUT). También al "Retomar" un caso del historial
  (`ResumeDeviceModal` → paso 3 → volver a 2). En edición **el celular puede no estar
  conectado** (se retoma un caso viejo, o se desenchufó).

El componente ya pasó por dos HU: `rediseno-dashboard` (layout de dos columnas, tokens
`--fx-*`, PrimeReact con pt propio, secciones plegables) y `formulario-caso-catalogos`
(autocompletado con catálogos del perito e integrantes como lista). La accesibilidad de
base es buena: `htmlFor`, `aria-invalid`, `aria-describedby`, `role="alert"` en errores,
foco al primer error (`focusErrorsTick`), objetivos táctiles `min-h-11`.

Estructura actual (`CaseFormStep.tsx:396-597`): grilla `grid-cols-1 lg:grid-cols-[1fr_1.75fr]`
(L398). Izquierda: **panel del equipo** (L401-458) con encabezado (logo de plataforma,
versión de SO, indicador USB), `PhoneFrame` con la captura de pantalla, modelo,
"Especificaciones" (IMEI, serial, nombre) y, si el IMEI no se detectó, el aviso
"IMEI no detectado automáticamente". Derecha: **formulario** (L461-584) con progreso n/m de
obligatorios, aviso de catálogos, `ExpertProfileCard`, secciones Actuación / Partes / Equipo
y `StepActions`.

### Hallazgos verificados en el código

**1. "USB conectado" es un texto fijo, siempre verde.**
`CaseFormStep.tsx:412-414`:
`<span className="… text-fx-success"><Usb …/> USB conectado</span>`. No depende de nada
(la SDD de `rediseno-dashboard-wizard` L290 lo especificó así, estático).

- El estado real existe en `page.tsx`: `useAgentConnection` (`hooks/useAgentConnection.ts`)
  expone `agentOnline` y `devices`, actualizados por el WebSocket de Tatana
  (`devices_changed`) y con reintento cada 3 s si Tatana se cae (entonces `devices` queda
  en `[]`).
- Pero `handleWsEvent` (`page.tsx:240-251`) solo evalúa "¿el equipo seleccionado sigue
  conectado?" **en el paso 3** (`stepRef.current === 3`), y `go()` limpia `deviceOffline`
  al salir del paso 3 (`page.tsx:312`). En el paso 2 no hay ninguna señal.
- `CaseFormStep` no recibe `agentOnline` ni `devices`; solo `device` (el seleccionado).
- Precedente visual: el paso 3 (`CaptureStep.tsx:405-432`) ya muestra "conectado /
  desconectado" con `Wifi`/`WifiOff` y un `FxBanner tone="error"` "Dispositivo
  desconectado — Reconectá el cable USB para continuar. La pantalla se actualizará sola."

**2. No hay `<form>`: Enter no envía.**
El paso es un `<div className="space-y-4">` (L397). El botón principal es
`<Button type="button" … onClick={onSubmit}>` (L574-582). Enter en un campo no hace nada.
Otros formularios del cliente sí usan `<form noValidate onSubmit=…>` (`LoginForm.tsx:166`,
`ChangePasswordForm.tsx:100`, `AccountFormDialog.tsx:279`, `BrandingDialog.tsx:328`), que es
el patrón a seguir.

Interacciones de Enter que ya existen y no se pueden romper:

- `IntegrantesField.tsx:108-113`: Enter en la última fila agrega otra; hace
  `preventDefault()` siempre, así que no enviaría el formulario (bien).
- `CatalogAutoComplete` (AutoComplete de Prime): con la lista abierta, Enter elige la
  opción resaltada (`CatalogAutoComplete.tsx:19`). Hay que asegurar que **con la lista
  abierta** Enter no envíe el formulario.
- `Calendar` de Prime (fecha de intervención): Enter dentro del selector abierto.
- `CatalogManageDialog` (diálogo "Administrar") se monta dentro del componente (L587-595);
  su Enter de edición ya hace `preventDefault()` (`CatalogManageDialog.tsx:229`).
- La validación es propia (`validateCaseForm` en `lib/pericial.ts`, `handleSaveCase` en
  `page.tsx:376-421`): el `<form>` tiene que ser `noValidate` para no mezclar los globos del
  navegador con los mensajes propios.

**3. La captura de pantalla traga los errores.**
`CaseFormStep.tsx:258-265`:
```ts
} catch { /* silently ignore — device may be locked */ }
```
Si la captura automática al entrar al paso (L267-272) o "Actualizar captura" falla, el
marco muestra "Sin captura" (`PhoneFrame.tsx:67-71`), igual que si nunca se hubiera
intentado. `agent.takeScreenshot` (`lib/agent.ts:351-362`) ya tira un `Error` con el motivo
real de Tatana (`readAgentError`, `{ error, code }`) o `AGENT_UNREACHABLE` si Tatana no
responde, y existe `agentErrorMessage` (`lib/agent-messages.ts:59`) para traducirlos. El
botón "Actualizar captura" de `PhoneFrame` (L74-83) ya funciona como reintento; lo que
falta es **decir que falló y por qué**.

Detalle menor: si la primera captura falla y después una funciona, o si "Actualizar"
falla después de una captura buena, hoy se ve la captura vieja como si fuera actual
(`screenshotSrc` no se limpia en el error).

**4. En pantallas angostas el panel del equipo empuja el formulario muy abajo.**
Con `grid-cols-1` por debajo de `lg` (1024 px), el panel del equipo va primero en el DOM:
encabezado + `PhoneFrame` (`max-w-[160px]` con proporción 236/470 ≈ 320 px de alto) +
modelo + especificaciones + aviso de IMEI. Son unos 550-650 px antes del primer campo. No
es solo "celular": una ventana de navegador a media pantalla en una notebook también queda
por debajo de 1024 px. El aviso "IMEI no detectado" vive en ese panel y es accionable
(obliga a cargar el IMEI en la sección Equipo), así que no se puede perder.

**5. El aviso de catálogos que no cargaron es poco visible y no tiene reintento.**
`CaseFormStep.tsx:486-488`:
`<p role="status" className="m-0 text-xs text-fx-text-3">No se pudieron cargar tus sugerencias</p>`.

- **Contraste:** medido con los tokens de `globals.css`, `--fx-text-3` sobre
  `--fx-surface-1` da ≈ 5,9:1 en claro (`#5c656e` sobre `#ffffff`) y ≈ 6,5:1 en oscuro
  (`#949ca6` sobre `#141619`): **ya cumple AA** para texto chico. El problema real no es
  el contraste sino la **jerarquía**: texto de 12 px gris, sin ícono ni tono de aviso,
  entre el encabezado y la tarjeta del perfil, fácil de no ver. Lo mismo dice cada campo
  con catálogo al abrir la lista (`CatalogAutoComplete.tsx:148`).
- **Reintento:** `useCatalogs` (`hooks/useCatalogs.ts:22-38`) carga una sola vez al montar y
  no expone ninguna función para volver a pedir; la única forma de reintentar es salir y
  volver a entrar al paso. Mientras tanto, el botón "Administrar" de cada campo se oculta
  (`catalogStatus === "ready"`, L327) y los campos funcionan como texto libre (eso está
  bien y se mantiene, por `formulario-caso-catalogos`).

**6. La URL del agente se arma a mano en dos lugares.**
`lib/agent.ts:8-9` ya exporta `AGENT_URL` y `agentFileURL(filename)`. Sin embargo:

- `CaseFormStep.tsx:225`: `const agentBase = process.env.NEXT_PUBLIC_AGENT_URL || "http://localhost:8765";`
  y L262 `${agentBase}/files/${filename}`.
- `hooks/useAgentConnection.ts:32`: el mismo fallback repetido para `/health`.

(Las menciones en `DesignSystemShowcase.tsx:563,902` son textos de ejemplo, no URLs.)
Es un cambio técnico chico, sin efecto visible.

**7. (No incluido) Tamaño de los componentes.** `page.tsx` tiene ~1117 líneas y
`CaptureStep` recibe ~60 props. Esta HU no lo refactoriza (ver Duda 6).

---

## Criterios de aceptación

```gherkin
Feature: Pulido de UX del paso 2 "Datos de la causa"

  Background:
    Given soy un perito autenticado en /dashboard
    And estoy en el paso 2 "Datos de la causa"

  # ── 1. Estado real de la conexión ──

  Scenario: Equipo conectado
    Given Tatana está corriendo y el celular seleccionado figura en su lista de dispositivos
    Then el encabezado del panel del equipo dice "USB conectado" en tono de éxito

  Scenario: El celular se desconecta mientras completo el formulario
    Given el indicador dice "USB conectado"
    When desenchufo el celular
    Then en pocos segundos, sin recargar la página, el indicador pasa a "Desconectado" en tono de error
    And veo un aviso que explica que la captura de pantalla no está disponible y cómo reconectar
    And los datos que ya escribí se conservan
    And el cambio se anuncia a lectores de pantalla una sola vez (región viva educada)

  Scenario: El celular se vuelve a conectar
    Given el indicador dice "Desconectado"
    When vuelvo a conectar el mismo celular
    Then el indicador vuelve a "USB conectado" y el aviso desaparece

  Scenario: Tatana no está corriendo
    Given Tatana se cerró
    Then el indicador dice "Agente sin conexión" (distinto de "Desconectado")
    And el aviso indica abrir Tatana

  Scenario: Editar un caso con el celular desconectado
    Given volví al paso 2 de un caso ya creado y el celular no está conectado
    Then el indicador dice "Desconectado" desde que entro
    And puedo editar y guardar el caso igual (según Duda 1)

  Scenario: El estado no depende del color
    Then cada estado del indicador tiene texto e ícono propios, no solo un color distinto

  # ── 2. Formulario real con Enter ──

  Scenario: Enter en un campo de texto envía el paso
    Given completé todos los obligatorios
    When aprieto Enter en un campo de texto común (por ejemplo "Número de causa / expediente")
    Then pasa lo mismo que al tocar "Crear caso y continuar" (o "Guardar y continuar" en edición)

  Scenario: Enter con faltantes muestra los errores
    Given falta un campo obligatorio
    When aprieto Enter en cualquier campo de texto común
    Then veo los mismos mensajes de error que hoy y el foco va al primer campo con error
    And el navegador no muestra sus propios globos de validación

  Scenario: Enter dentro de las sugerencias elige, no envía
    Given la lista de sugerencias de "Destinatario" está abierta con una opción resaltada
    When aprieto Enter
    Then se elige la opción y la lista se cierra
    And el paso no se envía

  Scenario: Enter en integrantes sigue agregando filas
    When aprieto Enter en la última fila de integrantes con texto
    Then se agrega una fila nueva y el foco va a ella, como hoy
    And el paso no se envía

  Scenario: Enter en el selector de fecha no envía
    Given el calendario de "Fecha de intervención" está abierto
    When aprieto Enter
    Then el paso no se envía

  Scenario: Enter en el diálogo "Administrar" no envía el paso
    Given tengo abierto el diálogo "Administrar" de un catálogo y estoy editando una entrada
    When aprieto Enter
    Then se guarda la entrada del catálogo como hoy
    And el paso no se envía

  Scenario: No hay envíos dobles
    Given el paso se está guardando ("Guardando…")
    When aprieto Enter otra vez o vuelvo a tocar el botón
    Then no se manda una segunda creación ni edición del caso

  Scenario: Los botones secundarios no envían
    When toco "Atrás", "Cancelar", "Actualizar captura", un encabezado de sección o un botón de integrantes
    Then el paso no se envía

  # ── 3. Error de captura de pantalla ──

  Scenario: Falla la captura automática al entrar
    Given el celular está bloqueado (o Tatana devuelve un error)
    When entro al paso 2 y la captura automática falla
    Then el marco del teléfono muestra un estado de error distinto de "Sin captura"
    And veo el motivo en palabras simples (el que manda Tatana, o "No se pudo conectar con el agente")
    And tengo un botón "Reintentar" a mano
    And el formulario se puede completar y enviar igual

  Scenario: Reintentar con éxito
    Given la captura falló
    When desbloqueo el celular y toco "Reintentar"
    Then veo "Capturando…" y después la pantalla nueva con "Pantalla capturada"
    And el mensaje de error desaparece

  Scenario: Falla una actualización después de una captura buena
    Given ya hay una captura en el marco
    When toco "Actualizar captura" y falla
    Then veo el error y queda claro que la imagen que se muestra no es actual (según Duda 3)

  Scenario: Sin equipo conectado no se intenta capturar
    Given el indicador dice "Desconectado" o "Agente sin conexión"
    Then "Actualizar captura" / "Reintentar" está deshabilitado con el motivo visible
    And al reconectarse el equipo se puede volver a capturar

  # ── 4. Pantallas angostas ──

  Scenario: Los campos aparecen enseguida por debajo de 1024 px
    Given la ventana mide 375 px de ancho (o cualquier ancho menor a 1024 px)
    When entro al paso 2
    Then sin hacer scroll veo el modelo del equipo, el estado de conexión y el título "Datos de la causa"
    And el primer campo del formulario está a no más de una pantalla de distancia del borde superior del paso
    And puedo ver la captura de pantalla y las especificaciones completas desplegando el panel del equipo

  Scenario: El aviso de IMEI no se pierde en el modo compacto
    Given el equipo no expuso el IMEI
    And la ventana mide menos de 1024 px
    Then el aviso "IMEI no detectado automáticamente" se ve sin desplegar nada

  Scenario: Escritorio sin cambios de layout
    Given la ventana mide 1024 px o más
    Then el paso se ve en dos columnas como hoy (panel del equipo a la izquierda, formulario a la derecha)

  Scenario: Sin scroll horizontal
    When miro el paso 2 en 320, 375, 768 y 1024 px de ancho
    Then no hay scroll horizontal en ninguno

  # ── 5. Sugerencias que no cargaron ──

  Scenario: Aviso visible y reintentable
    Given la carga de mis catálogos de sugerencias falla
    Then veo un aviso en tono de advertencia, con ícono, que dice que no se pudieron cargar las sugerencias y que puedo escribir igual
    And el aviso tiene un botón "Reintentar"
    And el texto cumple contraste AA (4,5:1) en tema claro y oscuro

  Scenario: Reintento exitoso
    Given el aviso de sugerencias está visible
    When toco "Reintentar" y la carga funciona
    Then el aviso desaparece
    And los campos con catálogo muestran sugerencias y el botón "Administrar"
    And lo que ya había escrito en esos campos no cambia

  Scenario: Reintento que vuelve a fallar
    When toco "Reintentar" y falla otra vez
    Then el botón muestra un estado de carga mientras intenta y el aviso sigue visible
    And puedo reintentar de nuevo

  Scenario: El paso nunca se bloquea por los catálogos
    Given los catálogos no cargaron
    Then puedo completar y enviar el paso con texto libre, como hoy

  # ── 6. URL del agente centralizada ──

  Scenario: Una sola fuente para la URL de Tatana
    Given NEXT_PUBLIC_AGENT_URL apunta a otro puerto
    Then la captura del paso 2 y el chequeo de salud del agente usan esa URL
    And en client/src no queda ningún fallback "http://localhost:8765" fuera de lib/agent.ts

  # ── No regresión ──

  Scenario: Lo que ya funciona sigue igual
    Then los mensajes de validación, el foco al primer error, el contador n/m de obligatorios, las secciones plegables, los catálogos, los integrantes y el perfil del perito se comportan como hoy
    And los datos que se guardan en el caso son exactamente los mismos que hoy
```

---

## Datos que se registran

No aplica. La HU no agrega ni cambia campos del caso, del perfil ni de los catálogos, ni
toca endpoints. Los cuerpos de `POST`/`PUT /api/cases` quedan idénticos.

---

## Diseño UX/UI (`client/`, paso 2 de `/dashboard`)

Todo con los tokens `--fx-*` y los componentes existentes (`FxBanner`, `Tag`, `Button` de
Prime con su pt). Sin 3D. Animaciones solo con `motion-safe:` y las duraciones `--fx-dur-*`.

**Indicador de conexión (encabezado del panel del equipo, hoy L412-414).** Tres estados,
cada uno con ícono y texto, en una región `role="status"`:

| Estado | Ícono | Texto | Tono |
|---|---|---|---|
| Conectado | `Usb` | "USB conectado" | `text-fx-success` |
| Equipo desconectado | `Unplug` o `WifiOff` (coherente con el paso 3) | "Desconectado" | `text-fx-danger` |
| Tatana caído | `PlugZap`/`ServerOff` | "Agente sin conexión" | `text-fx-warning` |

Cuando no está conectado, debajo del encabezado del panel aparece un `FxBanner` breve
(tono `error` o `warn`) con el mismo lenguaje que el paso 3: "Reconectá el cable USB. La
captura se actualiza sola al reconectar." / "Abrí Tatana en esta PC." No bloquea el
formulario (Duda 1).

**Captura de pantalla (`PhoneFrame`).** Cuarto estado del marco, además de cargando /
imagen / "Sin captura": **error**, con ícono de advertencia, "No se pudo capturar" y el
motivo en una línea (`text-xs`, color con contraste AA). El botón de abajo pasa de
"Actualizar captura" a "Reintentar" en ese estado. Si había una captura previa, el
tratamiento de la imagen vieja lo define la Duda 3. El mensaje se anuncia con
`role="status"` (no `alert`: no interrumpe el tipeo).

**Formulario real.** El contenedor de la columna derecha (o el paso entero, lo decide la
SDD) pasa a ser `<form noValidate onSubmit>`; el botón principal pasa a `type="submit"`; el
resto de los botones quedan `type="button"`. Sin cambios visuales.

**Pantallas angostas (< `lg`).** Propuesta (Duda 2): el panel del equipo se convierte en
una **tarjeta compacta** arriba del formulario: una fila con la miniatura chica (o el
ícono de plataforma), modelo, versión de SO, indicador de conexión y un botón
"Ver equipo" (`aria-expanded`, `min-h-11`) que despliega la captura y las especificaciones.
El aviso de IMEI faltante se muestra siempre, fuera de lo plegable. En `lg` y más, layout
de dos columnas como hoy.

**Aviso de sugerencias (hoy L486-488).** Se reemplaza el `<p>` gris por un `FxBanner
tone="warn"` compacto con ícono: "No se pudieron cargar tus sugerencias. Podés escribir
igual." y un botón secundario chico "Reintentar" (`min-h-11` en táctil, con estado de
carga). Ubicación: donde está hoy, sobre la tarjeta del perfil. El mensaje vacío dentro de
cada `CatalogAutoComplete` puede seguir como está (Duda 4).

**Feedback de guardado.** Sin cambios: "Guardando…" en el botón principal y los errores
del servidor como hoy.

---

## Fuera de alcance

- Refactor grande de `page.tsx` (≈1117 líneas) o de `CaptureStep` (≈60 props), extraer un
  contexto o un store del wizard (ver Duda 6).
- Llevar el paso del wizard a la URL (hoy un refresh vuelve al historial).
- Cambios en los pasos 1, 3, 4, 5 y 6, salvo lo mínimo para pasar el estado de conexión al
  paso 2 desde `page.tsx`.
- Cambiar campos, validaciones, mensajes de error de campos, orden o secciones del
  formulario.
- Captura de pantalla automática periódica o en vivo (espejo) en el paso 2.
- Cambios en Tatana, en el backend o en `agent-ui/`.
- Bloquear el guardado del caso por estar desconectado (salvo que la Duda 1 diga otra cosa).
- Tocar los textos de `DesignSystemShowcase` que mencionan `localhost:8765`.

---

## Notas de implementación (mínimas; el detalle lo escribe el `architect`)

- **Estado de conexión:** `page.tsx` ya tiene `agentOnline` y `devices`
  (`useAgentConnection`). Lo más simple es derivar en `page.tsx`
  `devices.some(d => d.serial === selDevice.serial)` y pasárselo a `CaseFormStep` como
  prop junto con `agentOnline`, sin tocar `handleWsEvent` ni `deviceOffline` (que son del
  paso 3). La SDD confirma.
- **Captura al reconectar:** si el equipo vuelve, se puede disparar una captura nueva si
  la última falló o no existe (la SDD decide; no capturar en loop).
- **`<form>`:** ojo con el `AutoComplete` de Prime: confirmar que con el panel abierto
  Enter hace `preventDefault()`; si no, interceptarlo en `CatalogAutoComplete`. Lo mismo con
  el `Calendar`. Confirmar si `CatalogManageDialog` (Dialog de Prime) se portalea fuera del
  DOM del `<form>`; si no, su contenido no debe disparar el submit del paso. El guard de
  doble envío puede apoyarse en `loading`.
- **`useCatalogs`:** exponer un `reload()` (y que `loading` vuelva a `true` / `error` a
  `false` al reintentar). No toca el formulario.
- **URL del agente:** usar `agentFileURL` en `CaseFormStep` y `AGENT_URL` en
  `useAgentConnection`.
- **Errores de captura:** traducir con `agentErrorMessage` (`lib/agent-messages.ts`) o
  usar el `message` del `Error` que ya arma `readAgentError`; la SDD elige para ser
  coherente con el paso 3.
- **Skills de frontend obligatorias** (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience` como criterio, `web-design-guidelines`).
- Sin tests en `client/` por ahora; verificación con `npx tsc --noEmit` y prueba manual en
  320/375/768/1024 px, tema claro y oscuro, con el celular conectado, desconectado y con
  Tatana cerrado.

---

## Dudas para validar con el usuario

**1. Si el celular está desconectado en el paso 2, ¿se puede guardar el caso?**
- **A (recomendada):** sí. Se avisa con el indicador y el banner, pero "Crear caso y
  continuar" / "Guardar y continuar" siguen habilitados. *Por qué:* guardar el caso no
  necesita el celular (los datos del equipo ya se leyeron en el paso 1), y en modo edición
  es normal retomar un caso viejo sin el equipo enchufado. El paso 3 ya bloquea la
  captura cuando el equipo no está.
- B: bloquear el envío en modo "crear" (caso nuevo) hasta que se reconecte, y permitirlo
  en edición. Evita arrancar una inspección con el equipo suelto, pero frena al perito
  por algo que se resuelve en el paso 3.
- C: bloquear siempre. Rompe la edición de casos retomados.

**2. ¿Cómo se ve el panel del equipo por debajo de 1024 px?**
- **A (recomendada):** tarjeta compacta arriba (miniatura chica o ícono, modelo, SO,
  estado de conexión) con "Ver equipo" para desplegar captura y especificaciones; plegada
  por defecto. El aviso de IMEI faltante siempre visible. *Por qué:* el perito ve qué
  equipo está inspeccionando y si está conectado, y los campos quedan casi arriba; nada se
  pierde.
- B: mover el panel completo **debajo** del formulario en pantallas angostas (solo cambia
  el orden). Más simple, pero el estado de conexión y el modelo quedan al final, lejos de
  la vista.
- C: dejarlo como está y solo achicar el `PhoneFrame`. Gana poco espacio.

**3. Si "Actualizar captura" falla habiendo una captura anterior, ¿qué se muestra?**
- **A (recomendada):** se mantiene la imagen anterior atenuada, con una etiqueta "Captura
  anterior" y el error debajo. *Por qué:* no se pierde la referencia visual, y queda claro
  que no es la pantalla actual.
- B: se reemplaza la imagen por el estado de error. Más simple, pero se pierde la última
  captura buena.

**4. Aviso de sugerencias: ¿también se cambia el mensaje dentro de cada campo?**
- **A (recomendada):** no. El aviso general (banner con "Reintentar") alcanza; dentro de
  la lista de cada campo se deja "No se pudieron cargar tus sugerencias" como hoy. *Por
  qué:* un solo lugar para reintentar, sin repetir botones en cada desplegable.
- B: sumar también un enlace "Reintentar" dentro de la lista vacía de cada campo.

**5. El hallazgo de "bajo contraste" del aviso de sugerencias.**
Medido con los tokens actuales, el texto ya cumple AA (≈ 5,9:1 en claro y ≈ 6,5:1 en
oscuro). El problema es que es chico, gris y sin ícono.
- **A (recomendada):** tratarlo como problema de **jerarquía** (banner de advertencia con
  ícono, como en "Diseño UX/UI"), sin tocar el token `--fx-text-3`. *Por qué:* cambiar el
  token afecta a toda la app y no hace falta.
- B: además subir el contraste de `--fx-text-3` globalmente. Fuera del alcance de esta HU;
  si se quiere, va en una HU de sistema de diseño.

**6. ¿Se refactoriza `page.tsx` / `CaptureStep` en esta HU?**
- **A (recomendada):** no. Esta HU solo agrega dos props a `CaseFormStep` y toca lo mínimo
  de `page.tsx`. El refactor (extraer el estado del wizard a un contexto/hook, partir
  `CaptureStep`) va como HU técnica aparte si se quiere. *Por qué:* mezclar un refactor
  de ~1100 líneas con cambios de UX hace la revisión difícil y arriesga regresiones en
  los pasos 3-6, que esta HU no necesita tocar.
- B: incluir un refactor acotado (por ejemplo, un `WizardContext` con el estado del
  equipo y la conexión) porque esta HU ya pasa estado nuevo al paso 2.
- C: dar de alta ahora la HU técnica de refactor en el Backlog para no perderla.
  (Compatible con A.)

**7. ¿Partir la HU?**
- **A (recomendada):** no. Los seis puntos son del mismo componente, solo `client/`, sin
  backend, y se revisan juntos con una sola prueba manual. *Por qué:* partirla serían
  varios ciclos de SDD y revisión sobre el mismo archivo.
- B: sacar el punto 6 (URL del agente) y hacerlo ya fuera del arnés, como el arreglo del
  ícono del calendario en `formulario-caso-catalogos`. Es un cambio de dos líneas sin
  decisiones.
