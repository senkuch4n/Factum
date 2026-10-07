# HU: Instalar Factum en una PC con poca RAM (4 GB) con advertencia y memoria acotada

**Slug:** `instalacion-poca-ram`
**Apps afectadas:** solo despliegue (`deploy/windows/`: `scripts/_comun.ps1`, `scripts/instalar.ps1`,
quizá `scripts/diagnostico.ps1`, `docker-compose.yml` o un compose adicional, `.env.example`,
`tests/comun.Tests.ps1`) y la guía `docs/instalacion-windows.md`. **Sin cambios** en `client/`,
`agent-ui/`, la API ni Tatana.
**Origen:** pedido del usuario (2026-10-02). La PC del estudio es un **Intel i3-7100U con 4 GB de
RAM, Windows 10**. Textual: "yo confío con que 4 GB de RAM anda, y si anda lento no hay drama".
**Depende de:** `instalacion-local-docker` (aprobada 2026-10-02).

**Como** desarrollador de Factum que instala el producto en el estudio
**quiero** que el instalador me deje seguir en una PC de 4 GB, avisándome claramente que va a
andar lento, y que en esa PC los contenedores usen la menor memoria razonable
**para que** el estudio pueda usar Factum en la PC que tiene, sin que el instalador lo bloquee y
sin cambiar nada en las PCs de 8/16 GB.

---

## Contexto

### Qué existe hoy (arqueología sobre `cfb97c8`)

**Chequeo de RAM (Q3)** — `deploy/windows/scripts/_comun.ps1`, `Test-Requisitos` (L640-648):
lee `Win32_ComputerSystem.TotalPhysicalMemory`; **< 7.5 GB = falla** ("Docker + Factum necesitan
al menos 8 GB"); 7.5 a < 15 GB = aviso ("recomendable 16 GB"). Q3 solo corre en modo `Instalar`
(también con `-Reparar`); `actualizar.ps1` usa `-Modo Actualizar`, que **no** chequea RAM.
Fijado así en la SDD (`Refactorizaciones/instalacion-local-docker.md` §6.5.1, Q3) y en la guía
(`docs/instalacion-windows.md` §2: tabla de requisitos "8 GB (recomendado 16)" y fila Q3 "Con
menos de 8 GB no se puede").

**Flujo de avisos en `instalar.ps1`** (paso 2, L44-60): si hay fallas, lista todas y corta sin
tocar nada; si solo hay avisos, los muestra y pregunta **una sola vez** `Confirm-SN '¿Continuar
igual con la instalación?'` (S/N). O sea, el mecanismo "aviso + confirmación S/N" **ya existe**:
bajar Q3 de falla a aviso alcanza para que el instalador pregunte. Lo que falta es que el aviso
de poca RAM sea explícito y no se pierda entre otros avisos (Q5 espacio, Q11 autoarranque).
**No hay modo desatendido** en `instalar.ps1` (`Read-Host` siempre; solo `backup.ps1` tiene
`-Desatendido`).

**Compose de producción** — `deploy/windows/docker-compose.yml`: `mongo:7.0.43`, backend y
frontend, **sin ningún límite de memoria** (`mem_limit`/`deploy.resources`), sin
`--wiredTigerCacheSizeGB`, sin `NODE_OPTIONS` ni variables del GC de .NET. Todas las llamadas a
compose pasan por `Invoke-FactumCompose` (`-f <home>\docker-compose.yml --env-file config\.env`).
`.env.example` no tiene ninguna clave de memoria.

**Actualizaciones** — `actualizar.ps1` (L117-129) agrega al `.env` las claves nuevas de
`.env.example` **con el valor por defecto del ejemplo**, sin pisar las existentes. Una clave
nueva de perfil de memoria llegaría así a las instalaciones previas: su default tiene que ser el
perfil normal.

**Imágenes** — backend: `aspnet:10.0` (ASP.NET Core usa **Server GC** por defecto: un heap por
núcleo lógico, más memoria; el i3-7100U tiene 2 núcleos / 4 hilos). .NET en contenedor respeta
el límite de memoria del cgroup (el heap toma por defecto ~75 % del límite), así que con un
`mem_limit` ya queda acotado sin `DOTNET_GCHeapHardLimit`. Frontend: `node:22-alpine` con Next
`standalone` (`node server.js`): proceso liviano (~100-150 MB). Mongo 7: el cache de WiredTiger
por defecto es el mayor entre 256 MB y 50 % de (RAM − 1 GB) de lo que ve el contenedor.

**WSL2 / Docker Desktop** — con el backend WSL2, Docker Desktop **no** tiene sliders de memoria:
la VM se limita con `%UserProfile%\.wslconfig`. **Sin `.wslconfig`, WSL2 ya limita la VM al 50 %
de la RAM de Windows (≈ 1.9 GB en esta PC) y usa un swap del 25 % (≈ 1 GB)**. O sea: en 4 GB la VM
ya queda acotada por defecto; un `.wslconfig` aporta poco en tope de memoria y su valor real
sería otro (más swap, devolver memoria cacheada a Windows), a costa de tocar un archivo del
usuario que afecta a todas sus distros de WSL y de exigir `wsl --shutdown` (que apaga el motor
de Docker). Nada del repo escribe hoy `.wslconfig`.

**Memoria que pide la generación del informe** (`ReportService.GenerateAsync`):
- ZIP de evidencia (`EvidenceZip.WriteAsync`/`VerifyAsync`, SharpZipLib): **streaming** con
  buffers de 80 KB; el SHA-256 también es por stream. No depende del tamaño de la evidencia
  (las grabaciones de varios GB no suben la RAM).
- DOCX (`GenerateDocxAsync`): `WordprocessingDocument.Open(outputPath, isEditable: true)` sobre
  el archivo. Con el paquete abierto en edición, System.IO.Packaging trabaja sobre un
  `ZipArchive` en modo Update, que **mantiene en memoria las partes que se escriben**: cada
  captura embebida en el informe (y las referenciadas en los textos, HU
  `editor-imagenes-informe`) queda en RAM hasta guardar. Pico aproximado ≈ suma de las imágenes
  embebidas × 1-2, más el runtime (~150 MB). Un caso con 50 capturas PNG de 2-3 MB ronda los
  100-300 MB extra; 150 capturas pueden pasar los 500 MB. Es el único punto donde un límite
  bajo del backend puede causar OOM.
- **Si el contenedor muere por OOM a mitad de la generación:** el caso queda en `Generating`
  (`CaseService` L379), los archivos sueltos de la evidencia **no se borraron** (se borran recién
  con el ZIP verificado, paso 9), `restart: unless-stopped` levanta el backend de nuevo y un
  reintento borra los restos del intento anterior (paso 1) y vuelve a generar (`Generating` no
  bloquea `GenerateAsync`, solo `Completed`). **No se pierde evidencia**, pero el perito ve un
  error y tiene que reintentar. Este riesgo hay que medirlo, no suponerlo (ver D4 y D8).

**Presupuesto estimado en la PC de 4 GB** (Windows reporta ≈ 3.8-3.9 GB): Windows 10 ≈ 1.5-2 GB,
Docker Desktop (procesos de Windows) ≈ 0.3-0.5 GB, navegador con Factum ≈ 0.4-0.8 GB, Tatana +
Python/adb ≈ 0.1-0.2 GB, VM de WSL ≈ hasta 1.9 GB. Suma > 4 GB: **Windows va a paginar a disco**.
Funciona, lento; con disco mecánico (HDD) mucho más lento que con SSD. Es justamente lo que el
usuario acepta.

### Qué es nuevo

1. Q3 deja de bloquear con 4 GB: aviso explícito de rendimiento + confirmación; piso duro por
   debajo de ese valor.
2. Un **perfil de memoria** ("poca RAM" vs. "normal") elegido por la RAM detectada al instalar,
   guardado en `config\.env`, que acota Mongo, backend y frontend solo en el perfil poca RAM.
   Las PCs de 8/16 GB quedan con la configuración efectiva **idéntica** a la de hoy.
3. Opcional según D5: `.wslconfig` en el perfil poca RAM.
4. Guía: requisitos actualizados, consejos para PCs de 4 GB, cómo cambiar de perfil y cómo
   revertir.

---

## Criterios de aceptación

```gherkin
Feature: Instalación en una PC con poca RAM

  Background:
    Given el paquete de instalación de Factum con esta HU
    And una PC que cumple todos los demás requisitos (Windows 10 22H2+, virtualización, Docker Desktop, puertos, disco)

  Scenario: PC con 4 GB de RAM - aviso explícito y el usuario sigue
    Given la PC reporta entre el piso duro y 7.5 GB de RAM (por ejemplo 3.8 GB)
    When se corre el instalador
    Then el paso de requisitos muestra un aviso propio de poca RAM que dice cuánta RAM tiene, que Factum va a andar lento y que se recomiendan 8 GB
    And pide confirmación S/N específica para seguir con poca RAM
    When la persona responde S
    Then la instalación sigue y config\.env queda con el perfil de memoria "poca RAM"
    And los contenedores arrancan con los límites del perfil poca RAM
    And http://localhost:3000 muestra el login de Factum

  Scenario: PC con 4 GB - el usuario no acepta
    Given la PC reporta entre el piso duro y 7.5 GB de RAM
    When se corre el instalador y la persona responde N al aviso de poca RAM
    Then el instalador termina sin haber creado carpetas ni configuración
    And explica que puede volver a ejecutarlo cuando quiera

  Scenario: Debajo del piso duro sigue bloqueando
    Given la PC reporta menos RAM que el piso duro (ver D1)
    When se corre el instalador
    Then Q3 es una falla (no un aviso) y no se instala nada
    And el mensaje dice cuánta RAM tiene y cuál es el mínimo

  Scenario: PC con 8 GB o más no cambia
    Given la PC reporta 7.5 GB de RAM o más
    When se corre el instalador
    Then config\.env queda con el perfil de memoria "normal"
    And la configuración efectiva de docker compose (docker compose config) es la misma que antes de esta HU
    And no se escribe ni modifica ningún .wslconfig

  Scenario: Reparar en una PC de poca RAM
    Given Factum instalado con el perfil "poca RAM"
    When se corre el instalador con -Reparar
    Then se muestra el aviso de poca RAM y se pide confirmación igual que en la instalación
    And el perfil de memoria guardado en config\.env no cambia

  Scenario: Actualizar conserva el perfil
    Given Factum instalado con el perfil "poca RAM"
    When se corre "Actualizar Factum" con un paquete nuevo
    Then el perfil de memoria de config\.env se conserva
    And la actualización no vuelve a preguntar por la RAM

  Scenario: Actualizar una instalación previa a esta HU
    Given Factum instalado antes de esta HU (sin clave de perfil en config\.env) en una PC de 8 GB o más
    When se actualiza
    Then se agrega la clave de perfil con el valor "normal" y nada cambia en el uso de memoria

  Scenario: Generar un informe grande con el perfil poca RAM
    Given Factum con el perfil "poca RAM"
    And un caso con la cantidad de capturas de referencia definida en D4
    When el perito genera el informe
    Then el informe y el ZIP se generan sin que el backend se reinicie por falta de memoria
    And el SHA-256 del ZIP verifica igual que en el perfil normal

  Scenario: Si igual se queda sin memoria, no se pierde evidencia
    Given el backend se reinicia por falta de memoria a mitad de una generación
    When el perito vuelve a generar el informe de ese caso
    Then la evidencia original sigue en la carpeta del caso y la generación puede reintentarse

  Scenario: Cambiar de perfil después (por ejemplo, si le agregan RAM a la PC)
    Given Factum instalado con el perfil "poca RAM"
    When se sigue el procedimiento de la guía para pasar al perfil "normal"
    Then los contenedores se recrean sin límites de memoria y sin perder datos

  Scenario: Diagnóstico muestra el perfil
    Given Factum instalado
    When se corre "Diagnostico de Factum"
    Then muestra la RAM de la PC, el perfil de memoria configurado y el uso de memoria de cada contenedor
```

---

## Datos que se configuran por instalación

| Dato | Obligatorio | Dónde vive | Uso |
|---|---|---|---|
| Perfil de memoria (p. ej. `FACTUM_PERFIL_MEMORIA=normal\|poca`) | Sí (default `normal`) | `config\.env`, lo fija `instalar.ps1` según la RAM detectada (o forzado, ver D6) | Decide si se aplican los límites de memoria. `actualizar.ps1` lo conserva. |
| Valores de cada límite (cache de Mongo, límite por contenedor, heap de Node) | Según D3 | En el compose del perfil o en `.env` (ver D3) | Acotar memoria en poca RAM. |
| `.wslconfig` | Según D5 | `%UserProfile%\.wslconfig` (fuera de `C:\Factum`) | Tope/swap de la VM de WSL2. |

---

## Diseño de la experiencia (consola de los scripts; no hay pantallas en `client/` ni `agent-ui/`)

**Paso 2 de `instalar.ps1` con 4 GB** (texto orientativo; el tono es el de los demás mensajes):

```
[2/13] Revisando los requisitos de la PC...
  AVISO: Quedan 38 GB libres en C: ...            <- otros avisos, si hay

  POCA MEMORIA: la PC tiene 3,8 GB de RAM (se recomiendan 8 GB).
  Factum va a funcionar, pero LENTO: abrir pantallas y generar informes puede tardar
  bastante más, sobre todo con muchas capturas. Para que ande mejor:
   - cerrá otros programas y pestañas mientras uses Factum,
   - no dejes abierta la ventana de Docker Desktop (alcanza con la ballena junto al reloj).
  Factum se va a configurar para usar la menor memoria posible.
  ¿Instalar igual con poca memoria? (S/N)
```

- La pregunta de poca RAM es **separada** de la genérica "¿Continuar igual?" (que sigue
  existiendo para los demás avisos), para que no pase desapercibida.
- S/N y no "SI" en mayúsculas: no es destructiva (convención de `Confirm-SN` vs. `Confirm-Si`).
- Con N: `Stop-Factum 'Instalación cancelada.'` antes del paso 3 (no se tocó el disco).
- El resumen final (paso 13) agrega una línea: "Perfil de memoria: poca RAM (ver guía §X para
  cambiarlo)". Si se escribió `.wslconfig` (D5), lo dice con su ruta.
- `diagnostico.ps1`: nueva sección "Memoria" con RAM total, perfil del `.env` y
  `docker stats --no-stream` (uso por contenedor). Solo lectura.

**Guía `docs/instalacion-windows.md`:**
- §2 Requisitos: RAM "**8 GB recomendado**; con 4 GB funciona con perfil de poca memoria, más
  lento" + piso duro (D1). Fila Q3 reescrita (aviso de poca RAM / falla bajo el piso).
- Sección nueva "PC con poca memoria (4 GB)": qué hace el perfil, qué esperar (tiempos), consejos
  (cerrar programas y pestañas, cerrar el dashboard de Docker Desktop, SSD vs. disco mecánico,
  reiniciar la PC al empezar el día), y qué hacer si un informe falla por memoria (reintentar;
  la evidencia no se pierde; casos muy grandes → ver D4).
- Cómo cambiar de perfil (p. ej. al agregar RAM) y cómo revertir lo hecho por esta HU (incluido
  el `.wslconfig` si se escribió).

---

## Fuera de alcance

- Optimizar el consumo de memoria del código (p. ej. generar el DOCX sin tener las imágenes en
  RAM, achicar capturas). Si D8 muestra OOM con casos reales, va como HU propia.
- Cambiar requisitos que no sean RAM (disco, Windows, virtualización) o los umbrales de Q5.
- Modo desatendido del instalador.
- Instalación sin Docker (plan C de la HU anterior) como alternativa para PCs chicas.
- Limitar la memoria de Tatana, del navegador o de Docker Desktop del lado de Windows.
- Cambios en `client/`, `agent-ui/`, la API o Tatana.

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- Reglas existentes de los scripts (SDD `instalacion-local-docker` R1-R9): PowerShell 5.1,
  UTF-8 con BOM + CRLF, toda llamada a compose por `Invoke-FactumCompose`.
- Separar la decisión en una función pura testeable (p. ej. `Get-PerfilMemoria -Gb <n>` →
  `bloquea | poca | normal`) para cubrirla en `deploy/windows/tests/comun.Tests.ps1` (Pester), igual
  que hoy `Read-EnvFile`/`New-HashManifest`.
- `Test-Requisitos` hoy devuelve `Fallas`/`Avisos`; el aviso de poca RAM necesita llegar a
  `instalar.ps1` distinguido de los demás (para la pregunta propia y para fijar el perfil).
- Compose: `mem_limit` a nivel de servicio funciona con `docker compose` v2 sin swarm; mongo con
  `command: ["mongod", "--wiredTigerCacheSizeGB", "0.25"]` (mínimo admitido 0.25).
- Backend: considerar `DOTNET_gcServer=0` (Workstation GC) en el perfil poca RAM; con
  `mem_limit` el GC ya respeta el límite.
- Frontend: `NODE_OPTIONS=--max-old-space-size=<n>` por debajo del `mem_limit`.
- Regla dura de datos (`AGENTS.md`): cualquier prueba con contenedores usa otro
  `COMPOSE_PROJECT_NAME` y otra carpeta, nunca la base ni la evidencia de desarrollo.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. Piso duro de RAM.**
- A) **< 3.5 GB bloquea**; de 3.5 a < 7.5 GB aviso de poca RAM con confirmación; ≥ 7.5 GB como hoy.
- B) Sin piso: cualquier RAM pasa con aviso.
- C) Piso en 3 GB.
- **Recomendada: A.** Una PC de 4 GB reporta ≈ 3.7-3.9 GB (la gráfica integrada del i3-7100U
  reserva una parte), así que 3.5 deja pasar la PC del estudio con margen. Debajo de eso Windows
  + Docker Desktop + WSL2 no entran ni con swap: el instalador fallaría más adelante de forma
  confusa (timeouts de healthcheck) en vez de explicar el motivo.

**D2. Confirmación y modos no interactivos.**
- A) Pregunta S/N **propia** de poca RAM (separada de la genérica de avisos). Con `-Reparar` se
  vuelve a mostrar y preguntar, sin cambiar el perfil guardado. `actualizar.ps1` no pregunta
  (no chequea RAM, como hoy). Sin modo desatendido (no existe hoy).
- B) Reusar la pregunta genérica "¿Continuar igual?" que ya existe (cambio mínimo: falla →
  aviso).
- C) Agregar un parámetro `-AceptarPocaRam` para saltear la pregunta.
- **Recomendada: A.** B funciona, pero el aviso queda mezclado con otros (espacio, autoarranque) y
  una sola S acepta todo. C no tiene uso: la instalación la hacés vos en persona (D3 de la HU
  anterior) y no hay modo desatendido.

**D3. Cómo se aplica el perfil sin tocar las PCs de 8/16 GB.**
- A) **Compose adicional** `docker-compose.poca-ram.yml` con los límites, que
  `Invoke-FactumCompose` suma con un segundo `-f` solo si `config\.env` dice
  `FACTUM_PERFIL_MEMORIA=poca`. El `docker-compose.yml` actual no cambia.
- B) Un solo compose con todos los valores en variables del `.env` (`FACTUM_MONGO_CACHE_GB`,
  `FACTUM_BACKEND_MEM`, …) con defaults "altos" para el perfil normal.
- **Recomendada: A.** Garantiza que en el perfil normal la config efectiva sea **idéntica** a la
  de hoy (B obliga a inventar valores "sin límite" para Mongo/Node que cambian el comportamiento
  actual, p. ej. `--wiredTigerCacheSizeGB` no admite "vacío"). Cambiar o revertir el perfil es
  editar una línea del `.env` y recrear los contenedores. Todos los scripts (backup, restaurar,
  diagnóstico, actualizar) ya pasan por `Invoke-FactumCompose`, así que lo heredan.

**D4. Valores del perfil poca RAM.**

| Servicio | A) Propuesta del orquestador | B) Recomendada |
|---|---|---|
| Mongo | cache 0.25 GB | `--wiredTigerCacheSizeGB 0.25` + `mem_limit: 512m` |
| Backend | 512 MB | `mem_limit: 768m` + `DOTNET_gcServer=0` |
| Frontend | 256-384 MB | `mem_limit: 384m` + `NODE_OPTIONS=--max-old-space-size=256` |
| Total de topes | ≈ 1.1-1.3 GB | ≈ 1.6 GB (entra en los ≈ 1.9 GB de la VM de WSL) |

- **Recomendada: B.** El único consumo que crece es el DOCX: las imágenes embebidas quedan en
  memoria hasta guardar (ver Contexto). Con 512 MB, un caso de 100+ capturas puede matar el
  backend a mitad de la generación; 768 MB da margen sin cambiar nada del lado de Windows (los
  topes son máximos, no reservas: en reposo los tres usan bastante menos). El ZIP no influye
  (es streaming). Los números finales se confirman con la medición de D8; **¿cuántas capturas
  tiene un caso típico del estudio, y cuál fue el más grande?** Eso define el "caso de
  referencia" del criterio de aceptación.

**D5. `.wslconfig`.**
- A) **No escribirlo.** WSL2 ya limita la VM al 50 % de la RAM (≈ 1.9 GB) y usa ≈ 1 GB de swap por
  defecto. La guía explica cómo crearlo a mano si hiciera falta.
- B) Escribirlo **solo en el perfil poca RAM y solo si no existe** (`memory=2GB`, `swap=2GB`,
  sin `processors`), avisando que hay que reiniciar WSL (`wsl --shutdown`, que apaga Docker) y
  reintentando arrancar Docker Desktop. Si ya existe uno, no se toca: solo un aviso con el
  contenido sugerido.
- C) Escribirlo siempre, con merge si ya existe.
- **Recomendada: A.** En 4 GB el tope por defecto ya es el que querríamos poner; el límite real
  que importa es el de cada contenedor (D4). Escribir `.wslconfig` toca un archivo del usuario
  fuera de `C:\Factum`, afecta otras distros de WSL, obliga a un `wsl --shutdown` en medio de la
  instalación y es otra cosa que revertir. C además implica parsear/mezclar un INI ajeno.
  Si la medición de D8 muestra que la VM se queda corta, se pasa a B.

**D6. Elección del perfil.**
- A) Automática por la RAM detectada (`< 7.5 GB` → poca; si no, normal), con un parámetro
  `-PerfilMemoria poca|normal` en `instalar.ps1` para forzarlo (p. ej. probar el perfil en tu PC
  o en una VM).
- B) Solo automática.
- **Recomendada: A.** El parámetro cuesta casi nada y permite probar el perfil poca RAM en una
  PC con más memoria antes de ir al estudio (ver D8). El cambio posterior (si le agregan RAM a la
  PC) se documenta en la guía: editar `config\.env` y reiniciar Factum.

**D7. Diagnóstico.**
- A) Agregar a `diagnostico.ps1` una sección "Memoria" (RAM total, perfil, `docker stats
  --no-stream`).
- B) Sin cambios en el diagnóstico.
- **Recomendada: A.** Si el estudio reporta "anda lento" o "falló el informe", el archivo del
  diagnóstico muestra de entrada si fue memoria. Son pocas líneas y solo lectura.

**D8. Cómo verificarlo sin una PC Windows de 4 GB.**
- A) Tres niveles: (1) Pester en tu Mac (`pwsh`) para la función de perfil (3.4 / 3.8 / 7.4 /
  7.6 / 16 GB) y para la elección del archivo de compose; (2) `docker compose config` con los dos
  perfiles: el normal tiene que dar **exactamente** la salida de hoy, el de poca RAM solo las
  diferencias esperadas; (3) en tu Mac, levantar la pila con el perfil poca RAM (otro
  `COMPOSE_PROJECT_NAME` y otra carpeta, regla dura de datos) y generar un informe del caso de
  referencia de D4 mirando el pico con `docker stats`; ajustar los valores con eso. La prueba
  final en la PC real queda en la checklist manual de la guía.
- B) Solo la prueba manual en la PC del estudio.
- **Recomendada: A.** El riesgo real (OOM del backend con muchas imágenes) se puede medir en
  Docker de la Mac porque los límites del contenedor se comportan igual; lo único que no se
  puede reproducir es la lentitud por paginación de Windows, que el usuario ya aceptó.

**D9. ¿La PC del estudio tiene SSD o disco mecánico (HDD)?**
- No cambia la implementación, pero con 4 GB Windows va a paginar y con HDD la diferencia de
  velocidad es grande. Si es HDD, la guía lo marca como la mejora más barata (un SSD), y
  conviene saberlo para calibrar las expectativas del estudio.
- **Recomendada:** que me lo confirmes (o lo veas en Administrador de tareas > Rendimiento >
  Disco, "Tipo"); el texto de la guía va igual para los dos casos.

## Validación (2026-10-02)

D1–D8 en la **recomendada** (usuario). D4: caso típico del estudio = 15–20 fotos + videos; caso de referencia para medir = 40 capturas (margen 2x) + videos (los videos van al ZIP por streaming y no al DOCX). D9: la PC tiene **SSD**.
