# HU: Tatana no se traba por un WebSocket lento, se apaga rápido y solo atiende pedidos dirigidos a esta PC

**Slug:** `tatana-ws-y-hosts` · **Issue:** #60
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent`). `agent-ui/` y `client/`
sin cambios previstos (el aviso de config local de la ventana y la reconexión del WS del front ya existen; ver "Diseño
UX/UI").

**Como** perito que usa Factum con Tatana en su PC
**quiero** que Tatana siga respondiendo aunque una pestaña o un programa conectado al WebSocket no lea los eventos, que
se cierre al instante cuando se reinicia o actualiza, que no le entregue mis archivos ni los datos del celular a una
página de internet y que una configuración local mal escrita no lo deje sin arrancar
**para que** una grabación, una captura o el ZIP de evidencia no queden colgados, la actualización automática pueda
entrar y la evidencia no se filtre fuera de la PC.

Origen: QA de robustez de Tatana del 2026-10-13 (`docs/qa/2026-10-13/tatana.md`, hallazgos T-01 a T-04, más las bajas
que se decidan en D5; `docs/qa/2026-10-13/INFORME.md`, ítem 3).

---

## Contexto

### Qué existe hoy

- **WebSocket de eventos** (`/ws`, `WebSockets/AgentWebSocketHub.cs`). Guarda los clientes en un
  `ConcurrentDictionary`. Cada cliente tiene un loop de ping cada 25 s y un loop de lectura que solo sirve para detectar
  el cierre (`HandleAsync`, líneas 29-70).
  - `BroadcastAsync` (líneas 72-89) serializa el evento y hace `await ws.SendAsync(...)` **cliente por cliente, en serie
    y sin timeout**. Lo llaman los controllers de grabación, captura, webcam y dispositivos (con el `ct` de la request),
    `CaseZipService` (progreso del ZIP, `:440`), `IosService` (AirPlay, grabación), `VideoPostprocessService` (`:652`) y
    el polling de dispositivos de `Program.cs:316-342`.
  - Interfaz `IAgentEventSink`, que existe para probar el post-proceso con un sink falso.
  - **T-01 (Alta):** un cliente que no lee llena su buffer TCP (unos 600 KB, unos 3.000 eventos) y a partir de ahí
    todas las requests que emiten eventos quedan colgadas: stop de grabación más de 300 s, ZIP y captura sin respuesta.
    `/agent/state` queda `busy: true` y `POST /agent/maintenance` responde 409 `agent_busy`, **lo que bloquea la
    actualización automática**. Los demás clientes dejan de recibir eventos. Todo se destraba recién cuando ese cliente
    se desconecta.
- **Apagado.** `Program.cs:302-311` cancela el post-proceso en `ApplicationStopping`, pero no cierra los WebSocket.
  El loop de lectura del hub usa el `RequestAborted` de la conexión, que no se cancela al apagar. El polling de
  dispositivos es un `while (true)` sin token.
  - **T-02 (Media):** con un WS abierto (el front siempre tiene uno), SIGTERM espera el `ShutdownTimeout` de 30 s. Sin
    clientes tarda 0,1 s. Esto le pega a cada reinicio y a cada actualización que hace la ventana de Tatana
    (`agent-ui`).
- **Guarda de origen** (`Program.cs:182-222`, `Common/OriginPolicy`, `zip-local-informe-servidor` §6.1). Rechaza con
  403 cualquier `Origin` que no esté en `Agent:AllowedOrigins`, incluido el WS. **Las requests sin `Origin` pasan**
  (procesos locales, Electron y los GET same-origin del navegador). `appsettings.json` tiene `"AllowedHosts": "*"` y no
  hay filtro de `Host`.
  - **T-03 (Media):** con DNS rebinding, una página de internet puede hacer GET same-origin (sin `Origin`) y leer
    `/files` y bajar grabaciones y capturas, `/devices` (IMEI), `/devices/{s}/explorer` (archivos del celular),
    `/health` (rutas), `/cases/{id}/files` y `/cases/{id}/zip/file`. En la prueba devolvió el ZIP completo. POST, DELETE
    y WS sí quedan protegidos porque ahí el navegador manda `Origin`.
  - Quién llama hoy a Tatana y con qué `Host`:
    - el front web en `NEXT_PUBLIC_AGENT_URL`, que por defecto es `http://localhost:8765` en `docker-compose.yml`, el
      deploy de nube (`FACTUM_AGENT_URL`), el paquete Windows y `client/next.config.ts`;
    - la ventana Electron, que usa `http://localhost:<port>` (`agent-ui/src/main/ipc.ts:69`, `agent-api.ts`).
  - Con `Agent:BindAddress` en una IP (o `0.0.0.0`), Tatana puede quedar expuesto a la red y se le llega por esa IP
    (README:242).
- **Config local** (`appsettings.Local.json`, D9 y **D-T16** de `Refactorizaciones/tatana-instalador-autoupdate.md:160`).
  D-T16 dice: "Si el JSON es inválido, se **ignora** con un log de error y queda en `/agent/state.local_config.error`:
  Tatana arranca igual, no entra en el bucle de reinicios de Electron".
  - `Common/LocalConfigInspector.cs` solo valida la **sintaxis** (y que sea un objeto). Si está bien, el archivo se
    suma como fuente de configuración (`Program.cs:59-81`).
  - Después, el binder de `AgentOptions` (`:84-95`, `:115`), `OriginPolicy.Parse` (`:109-113`) y los chequeos de
    `MaxUploadBytes`/`MinFreeBytes` (`:116-119`) tiran excepción sin distinguir de qué fuente vino el valor.
  - **T-04 (Media):** `{"Agent":{"MaxUploadBytes":"16GB"}}` o un origen con path en la config local hacen que Tatana
    aborte al arrancar. En el instalado, Electron lo relanza en bucle: justo lo que D-T16 quería evitar.
  - La ventana de Tatana ya muestra el aviso cuando `local_config.error` viene con texto (`agent-ui/src/main/app-info.ts:77-86`).
- **Reconexión del front.** `client/src/hooks/useAgentConnection.ts:48-57` reintenta solo en `onclose`/`onerror` y, al
  reconectar, vuelve a pedir `/devices`. Si Tatana corta un cliente lento, el front se recupera solo.
- **Tests.** Existe `server/tests/Factum.Agent.Tests` (por ejemplo `LocalConfigInspectorTests.cs`).

### Qué es nuevo

1. **Envío por WebSocket que no frena al agente:** un cliente lento o muerto se descarta y no traba ni a la request que
   emite el evento ni a los otros clientes (D1).
2. **Apagado rápido:** al apagarse, Tatana cierra los WS con un close frame, corta el polling de dispositivos y termina
   en un par de segundos.
3. **Filtro de `Host`:** solo se atienden requests dirigidas a esta PC (D2, D3).
4. **Config local con valores inválidos = config local ignorada**, igual que con JSON roto, como pide D-T16 (D4).
5. Las bajas del QA que se acuerden en D5.

---

## Criterios de aceptación

```gherkin
Feature: Tatana robusto frente a clientes WS lentos, apagado y pedidos ajenos

  Background:
    Given Tatana corriendo en la PC del perito con su configuración por defecto

  # ── T-01 ───────────────────────────────────────────────────────────────
  Scenario: Un cliente WebSocket que no lee no traba las operaciones
    Given un cliente conectado a /ws que nunca lee (buffer de recepción mínimo)
    And otro cliente WS que sí lee (el front)
    When se generan miles de eventos (start/stop de grabación mock) hasta superar el límite de D1
    And se pide POST /devices/{s}/record/stop, POST /devices/{s}/screenshot y POST /cases/{id}/zip
    Then cada request responde en su tiempo normal, sin quedar esperando al cliente lento
    And el cliente que sí lee sigue recibiendo todos los eventos
    And el cliente lento queda desconectado según D1 y Tatana lo registra en el log
    And /agent/state vuelve a busy:false al terminar las operaciones

  Scenario: La actualización automática no se bloquea por un cliente lento
    Given un cliente WS que no lee y ya superó el límite de D1
    And no hay ninguna operación real en curso
    When la ventana de Tatana pide POST /agent/maintenance
    Then no responde 409 agent_busy por culpa de ese cliente

  Scenario: Un evento nunca espera a la red
    When cualquier parte de Tatana emite un evento (polling de dispositivos, progreso del ZIP, post-proceso, AirPlay)
    Then la emisión vuelve sin esperar que el evento llegue a ningún cliente

  Scenario: El orden de eventos se mantiene por cliente
    Given un cliente WS que lee normalmente
    When Tatana emite una secuencia de eventos (por ejemplo zip_progress hashing, zipping, verifying)
    Then el cliente los recibe en el mismo orden en que se emitieron

  Scenario: El front se recupera si Tatana lo desconecta
    Given el front conectado por WS
    When Tatana cierra su conexión por lento
    Then el front reconecta solo (comportamiento actual de useAgentConnection) y vuelve a ver los dispositivos

  # ── T-02 ───────────────────────────────────────────────────────────────
  Scenario: Apagado rápido con clientes conectados
    Given 1 o más clientes WS conectados
    When Tatana recibe SIGTERM (o la ventana lo reinicia para actualizar)
    Then cada cliente WS recibe un close frame (1001 "going away")
    And el proceso termina en menos de 2 segundos (más lo que ya tarda hoy en cancelar el post-proceso, tope 5 s)
    And el polling de dispositivos deja de correr y de loguear durante el apagado

  # ── T-03 ───────────────────────────────────────────────────────────────
  Scenario Outline: Se rechaza un Host que no es esta PC
    When llega un GET <ruta> con header Host "rebind.attacker.example:8765" y sin Origin
    Then Tatana responde 400 sin datos (ni listado, ni archivo, ni IMEI)

    Examples:
      | ruta                        |
      | /files                      |
      | /devices                    |
      | /devices/{s}/explorer       |
      | /health                     |
      | /cases/{id}/files           |
      | /cases/{id}/zip/file        |
      | /ws                         |

  Scenario Outline: Se aceptan los Host locales
    When llega un GET /health con header Host <host>
    Then Tatana responde 200

    Examples:
      | host               |
      | localhost:8765     |
      | 127.0.0.1:8765     |
      | [::1]:8765         |

  Scenario: Front web y ventana de Tatana siguen funcionando
    Given el front web (local o en VPS) con NEXT_PUBLIC_AGENT_URL = http://localhost:8765
    And la ventana de Tatana (agent-ui)
    When usan Tatana normalmente (dispositivos, grabación, archivos, ZIP, WS, mantenimiento)
    Then todo funciona igual que hoy

  Scenario: Tatana escuchando en una IP de la red
    Given Agent:BindAddress configurado con una IP concreta
    When un cliente llama usando esa IP como Host
    Then se acepta (según D3)

  # ── T-04 ───────────────────────────────────────────────────────────────
  Scenario Outline: Config local con valores inválidos se ignora y se avisa
    Given un appsettings.Local.json con JSON válido pero <valor>
    When arranca Tatana
    Then arranca con la configuración sin ese archivo (según D4)
    And /agent/state.local_config tiene loaded:false y un error que dice qué clave está mal
    And la ventana de Tatana muestra el aviso de config local (el que ya existe)

    Examples:
      | valor                                                    |
      | {"Agent":{"MaxUploadBytes":"16GB"}}                      |
      | {"Agent":{"MaxUploadBytes":0}}                           |
      | {"Agent":{"MinFreeBytes":-1}}                            |
      | {"Agent":{"AllowedOrigins":["http://localhost:3000/app"]}} |
      | {"Agent":{"AllowedOrigins":["*"]}}                       |
      | {"Agent":{"Port":"ocho"}}                                |

  Scenario: La config principal inválida sigue impidiendo arrancar
    Given appsettings.json, una variable de entorno o la línea de comandos con un valor inválido
    When arranca Tatana
    Then no arranca y muestra el mensaje claro de hoy (no cambia)

  # ── Bajas (según D5; ver la tabla de la duda) ──────────────────────────
  Scenario: Un .part huérfano del ZIP se limpia al arrancar (T-05)
    Given un <EvidenceDirectory>/<caso>/.factum/pendiente/evidencia_X.zip.part que dejó un kill -9
    When arranca Tatana
    Then el .part se borra y se loguea cuántos borró

  Scenario: Un ZIP que falla antes de escribir no deja carpetas (T-06)
    When POST /cases/{id}/zip falla por archivo inexistente, evidencia cambiada o falta de espacio
    Then no queda ninguna carpeta nueva en la carpeta de evidencia

  Scenario: Códigos de error específicos del ZIP (T-07)
    When un archivo desaparece entre el hash y el zip
    Then responde 409 evidence_changed
    When el disco se llena a mitad del zip
    Then responde 507 insufficient_storage
    And en los dos casos la evidencia queda intacta y el .part se borra (como hoy)

  Scenario Outline: Contraseña del ZIP fuera de rango (T-08, T-09)
    When POST /cases/{id}/zip llega con password <pw>
    Then responde 400 con un code de validación, sin crear nada

    Examples:
      | pw                                   |
      | ""                                   |
      | una de más de 99 bytes en UTF-8      |

  Scenario: Nombres con controles bidi o C1 se rechazan (T-13)
    When se sube o importa un archivo cuyo nombre contiene U+202E u otro control bidi o C1
    Then responde 400 como hoy con los caracteres < 0x20

  Scenario: Detener una grabación inexistente da 409 con code (T-14)
    When POST /devices/{s}/record/stop sin grabación activa (Android o iOS)
    Then responde 409 con un code estable, no 500 con el mensaje crudo
```

---

## Datos que se registran

No hay datos nuevos en la base. No cambia nada en MongoDB ni en el expediente. Cambios observables en Tatana:

| Dato | Obligatorio | Uso |
|---|---|---|
| `local_config.error` en `GET /agent/state` (existente) | Cuando el archivo se ignora | Ahora también se llena con valores o tipos inválidos, no solo con JSON roto. El texto nombra la clave. |
| Log de cliente WS desconectado por lento | Sí | Diagnóstico: id del cliente, motivo (cola llena o timeout). |
| Config opcional de hosts extra (solo si D3 = B) | No | Hosts adicionales permitidos (por ejemplo una IP de red). |

---

## Diseño UX/UI

- **`client/` (web):** sin cambios. El front ya reconecta el WS en `onclose`/`onerror`. Si Tatana lo corta por lento,
  se ve el mismo estado de "reconectando" que hoy. Con `NEXT_PUBLIC_AGENT_URL = http://localhost:8765` el filtro de
  `Host` no lo afecta.
- **`agent-ui/` (Electron):** sin cambios de pantalla.
  - Para T-04 se reutiliza el aviso de config local que ya existe (`app-info.ts`): aparece porque
    `local_config.error` ahora también viene con texto. Lo único que cambia es ese texto. Tiene que decir en castellano
    qué clave falló, por ejemplo: "Agent:MaxUploadBytes no es un número: '16GB'. Se ignoró la configuración local.".
  - Lo que se nota en la práctica: actualizar y reiniciar Tatana tarda segundos, no 30.
- **Errores visibles para el perito (bajas):** cambian solo los códigos HTTP y los `code` de algunos errores del ZIP y
  de la grabación (T-07, T-08, T-09, T-14). La SDD revisa si `client/src/components/dashboard/GenerateStep.tsx` ya
  mapea `evidence_changed` e `insufficient_storage` a mensajes. Si los mapea, mejoran solos. Si no, queda como nota y
  no se toca el cliente en esta HU (ver D6).

---

## Fuera de alcance

- Autenticación entre el front o los procesos locales y Tatana (T-17: es así por diseño, localhost sin auth). Como
  mucho, una nota en el README para PCs compartidas.
- Pedir un header propio en los GET sensibles (lo sugiere el QA para T-03). Ver D2: rompería las URLs directas que usa
  el front (`agentFileURL`, `zipFileURL` en `<a href>`/`<img>`/`<video>`).
- Cambiar el contrato o los tipos de los eventos WS. Solo cambia cómo se envían.
- Validar el `appsettings.json` principal, las variables de entorno o la CLI de otra forma: siguen abortando con
  mensaje claro.
- Las bajas que D5 deje afuera. Las informativas T-15 (curl `100` en frío), T-16 (`nosniff` en `/files`) y T-18
  (redondeo del 413) quedan afuera salvo que D5 diga otra cosa.
- Cambios en `Factum.Backend` y en `client/` (salvo lo que decida D6).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- El ping del hub (`HandleAsync`) también hace `SendAsync` sobre el mismo socket. Con una cola por cliente, el ping
  tiene que pasar por esa misma cola (o usar el `KeepAliveInterval` de `UseWebSockets`, que ya está en 25 s) para que
  no haya dos envíos concurrentes.
- `IAgentEventSink.BroadcastAsync` mantiene la firma para no tocar a sus llamadores. Que devuelva una `Task` completada
  apenas encola es un cambio de semántica que la SDD tiene que dejar escrito.
- Host filtering: `AllowedHosts` de ASP.NET compara sin puerto y acepta `[::1]`. El valor tiene que salir de
  `BindAddress` (D3) y no del `"*"` actual. Ojo: `appsettings.Local.json` podría pisar `AllowedHosts` (D4 decide si eso
  se permite).
- T-04: lo natural es validar la fuente local **aislada** (bindear `Agent` sobre una `ConfigurationBuilder` con solo
  ese archivo, más `OriginPolicy.Parse` y los límites) antes de sumarla. Los tests van en
  `server/tests/Factum.Agent.Tests` junto a `LocalConfigInspectorTests`.
- Tests que valen la pena:
  - hub con un `WebSocket` falso que nunca completa `SendAsync`: el broadcast vuelve, el cliente se descarta y los
    demás reciben;
  - cierre de WS en `ApplicationStopping`;
  - filtro de Host;
  - config local inválida por tipo y por valor.
- Repro del QA reutilizable: `scratchpad/qa/tatana-scripts/` (`ws_stall.py`, `ws_stall_zip3.py`,
  `shutdown_time.py`) en el scratchpad de la sesión de QA. Si ya no está, se rearma con lo que describe la tabla de
  `tatana.md`.
- Regla dura de datos (`AGENTS.md`): las pruebas usan una instancia propia (`--mock --port <otro> --data <scratch>`
  y `Agent__EvidenceDirectory=<scratch>`) y no tocan `~/Factum/Evidencia`, el `agent-data` real ni la instancia en
  :8765.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. Cómo se envían los eventos WS a cada cliente (T-01).**
- A) **Cola acotada por cliente con un enviador propio.**
  - `BroadcastAsync` solo encola en la cola de cada cliente y vuelve.
  - Cada cliente tiene una tarea que envía en orden, con un **timeout por envío** (por ejemplo 10 s).
  - Si la cola se llena (por ejemplo 256 eventos) o un envío vence el timeout, el cliente se **desconecta** con close
    1008 y se saca de la lista. El front reconecta solo.
- B) Envío en paralelo a todos los clientes con un timeout común (`Task.WhenAll` + timeout); el que vence se descarta.
- C) Cola acotada, pero al llenarse se **descartan eventos** (los más viejos) sin desconectar al cliente.
- **Recomendada: A.** Es la única opción en la que quien emite (una request de grabación, el ZIP, el polling) nunca
  espera a la red y se mantiene el orden por cliente.
  - B sigue haciendo esperar a la request hasta el timeout en cada evento; con miles de eventos se vuelve a trabar.
  - C deja al cliente creyendo que está al día cuando perdió, por ejemplo, el `devices_changed` o el último
    `zip_progress`.
  - Desconectar obliga a reconectar, y el front ya resincroniza el estado al reconectar.
  - Los números exactos (tamaño de cola, timeout) los fija la SDD, salvo que quieran otros.

**D2. Cómo se cierra el DNS rebinding (T-03).**
- A) **Solo filtro de `Host`**: se acepta únicamente un `Host` que sea esta PC (D3) y el resto recibe 400. Vale para
  todas las rutas, incluidos `/ws` y `/health`.
- B) Filtro de `Host` **y además** un header propio obligatorio en los GET sensibles.
- C) Solo el header propio.
- **Recomendada: A.** Con DNS rebinding, el `Host` que manda el navegador es siempre el dominio del atacante, así que
  el filtro alcanza para cortarlo, y no requiere cambios en el front ni en la ventana.
  - B y C rompen los links directos que hoy usa el front: `agentFileURL` y `zipFileURL` van en `href`/`src` y no pueden
    mandar headers. Además obligan a sumar el header a `Access-Control-Allow-Headers` y a tocar `client/`.

**D3. Qué hosts se permiten.**
- A) **Fijos `localhost`, `127.0.0.1` y `[::1]` (cualquier puerto), más la IP de `Agent:BindAddress` si es una IP
  concreta.** Con `0.0.0.0`/`::` (expuesto a la red) se suman las IP locales de la PC, o se exige configurarlas (lo
  decide la SDD).
- B) Lo mismo que A, más una lista opcional `Agent:AllowedHosts` (en `appsettings.Local.json`) para casos raros, por
  ejemplo un front con `NEXT_PUBLIC_AGENT_URL` apuntando a un nombre como `tatana.local`. Un `"*"` impide arrancar,
  igual que pasa hoy con `AllowedOrigins`.
- C) Solo los tres locales; con `BindAddress` en IP, el que quiera usarla la configura a mano.
- **Recomendada: B.** Por defecto no cambia nada para nadie: el front en VPS o local usa `http://localhost:8765`
  (`docker-compose.yml`, deploy de nube, paquete Windows) y la ventana usa `localhost`.
  - `AllowedOrigins` no se ve afectado: valida de qué sitio viene la request (`Origin`). El `Host` valida a quién va
    dirigida, y para Tatana es siempre la propia PC aunque la web esté en un VPS.
  - La lista extra cubre a quien haya puesto `FACTUM_AGENT_URL` con otro nombre sin obligarlo a tocar código.
  - Pregunta: ¿saben de alguna instalación con `FACTUM_AGENT_URL` / `NEXT_PUBLIC_AGENT_URL` distinto de
    `http://localhost:8765`?

**D4. Qué hacer con una config local con valores inválidos (T-04).**
- A) **Ignorar el archivo entero** (como con JSON roto): Tatana arranca con `appsettings.json` + env + CLI, y
  `local_config.loaded = false` con un error que nombra la clave. El aviso de la ventana ya existente lo muestra.
- B) Ignorar solo las claves inválidas y aplicar el resto.
- C) Mantener que no arranque, pero con un mensaje claro.
- **Recomendada: A.** Es lo que pide D-T16 y evita el bucle de reinicios de Electron.
  - B es más cómodo pero difícil de razonar. Por ejemplo, si se descarta un `AllowedOrigins` mal escrito y se aplica
    el resto, Tatana queda con los orígenes por defecto y la web configurada deja de conectar sin una causa obvia.
    Ignorar todo el archivo es predecible y el aviso dice qué arreglar.
  - Duda derivada: si la config local define `AllowedHosts` (raíz de ASP.NET) en vez de `Agent:AllowedHosts`, ¿se
    ignora esa clave? Recomendado: sí, solo cuenta la de D3-B.

**D5. Qué bajas del QA entran en esta HU.**

| ID | Qué es | Propuesta |
|---|---|---|
| T-05 | `.part` del ZIP huérfano tras crash (puede pesar GB) | **Entra**: limpieza al arrancar, igual que `.upload-tmp` |
| T-06 | Carpetas vacías tras un ZIP fallido | **Entra**: crear la carpeta recién al escribir |
| T-07 | 500 genérico en lugar de 409 `evidence_changed` / 507 `insufficient_storage` | **Entra** |
| T-08 | `password: ""` → 500 | **Entra**: 400 de validación |
| T-09 | Contraseña de 100 bytes o más: el ZIP no abre en 7-Zip | **Entra**: 400 si supera 99 bytes UTF-8 (hoy el backend manda 16, así que no rompe nada) |
| T-13 | Nombres con RTL override o C1 (spoofing `exe.jpg`) | **Entra**: rechazar controles bidi y C1 en `AgentFileNames` |
| T-14 | `record/stop` sin grabación → 500 con el mensaje crudo | **Entra**: 409 con `code` |
| T-10 | Cuerpo `x-www-form-urlencoded` descartado en upload/maintenance | Afuera: solo afecta a scripts con curl; ningún cliente nuestro manda form |
| T-11 | `ffmpeg` huérfano tras `kill -9` | Afuera: necesita Job Object en Windows o process group en macOS; HU propia de post-proceso |
| T-12 | Dos subidas simultáneas con el mismo nombre: las dos 200 | Afuera: implica definir la semántica de concurrencia (409 o serializar); el ZIP ya lo detecta |
| T-15 a T-18 | Informativas | Afuera (T-17 como nota en README si se quiere) |

- A) **La propuesta de la tabla** (T-05, T-06, T-07, T-08, T-09, T-13, T-14).
- B) Solo T-01 a T-04 (las bajas, a otra HU).
- C) Todas las bajas.
- **Recomendada: A.** Son cambios chicos que tocan los mismos archivos que la HU ya revisa (`CaseZipService`,
  `EvidenceZip`, `AgentFileNames`, `RecordingController`, `Program.cs`) y que mejoran la cadena de custodia: GB
  huérfanos, ZIP que no abre, nombres engañosos. T-10, T-11 y T-12 necesitan decisiones de diseño propias.

**D6. ¿Se toca el cliente para los nuevos códigos de error?**
- A) **No.** Si `GenerateStep.tsx` / `agent.ts` ya mapean `evidence_changed`, `insufficient_storage` y los 409 de
  grabación, mejoran solos. Si no, se anota para otra HU.
- B) Sí, ajustar los mensajes en `client/` dentro de esta HU.
- **Recomendada: A.** Mantiene la HU solo en Tatana (una sola rama de trabajo, un implementador). Los casos que llegan
  a esos códigos son raros (borrar evidencia a mano, disco lleno a mitad), y hoy igual se muestra un error.

## Validación (2026-10-13)
Resuelta por el orquestador por delegación explícita del usuario ("resolvé vos las técnicas"): todas las dudas con la opción recomendada.
