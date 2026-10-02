# HU: Instalación local de Factum en la PC Windows de un estudio (Docker + Tatana nativo)

**Slug:** `instalacion-local-docker`
**Apps afectadas:** despliegue (`docker-compose*.yml`, `Dockerfile`s, scripts nuevos de
instalación/actualización/backup en PowerShell, guía de instalación). Código de producción
solo en cambios chicos de empaquetado (ver Dudas: `client/Dockerfile` + `.dockerignore`,
`server/src/Factum.Backend/Dockerfile`, quizá `next.config.ts` y el bind de
`server/src/Factum.Agent/Program.cs`). Sin cambios funcionales en `client/`, `agent-ui/` ni
en la API.
**Origen:** pedido del usuario (desarrollador de Factum), 2026-10-02: "el estudio jurídico
quiere que le instale este programa de manera local; yo tenía pensado hacerlo con Docker;
necesitaría que me ayudes a dockerizar cada parte del sistema".
**Confirmado por el usuario:** una sola PC con Windows; servidor, navegador y celular por USB,
todo en la misma máquina.

**Como** desarrollador de Factum que instala el producto en un estudio jurídico
**quiero** un paquete de instalación reproducible para una PC Windows (servidor en Docker y
agente Tatana nativo), con configuración por instalación, arranque automático, backup y
actualización
**para que** el estudio use Factum sin depender de un servidor externo, sin perder evidencia
ni casos, y para que yo pueda instalar, actualizar y restaurar sin improvisar en la PC del
cliente.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-02, sobre `d31126e` + árbol de trabajo)

**`docker-compose.yml` (raíz)** — ya levanta tres servicios, pensado para desarrollo/demo:

| Servicio | Estado actual | Problema para una instalación real |
|---|---|---|
| `mongo` (`mongo:7`) | Volumen `mongo-data`, healthcheck con `mongosh`, `restart: unless-stopped`. | Publica `27017:27017` en **todas** las interfaces (sin usuario/contraseña de Mongo). |
| `backend` | `build:` desde `server/src/Factum.Backend`; `ASPNETCORE_ENVIRONMENT=Production`, `Auth__Mode=dev`, `Jwt__Secret=factum-dev-secret-change-in-production` (el mismo valor commiteado en `appsettings.json`), `Storage__DataDirectory=/data` en el volumen `backend-data`. | `8080` en todas las interfaces; secreto JWT público; `dev` acepta cualquier contraseña; no monta `appsettings.Local.json` ni `branding/`. |
| `frontend` | `build:` desde `client/`; `environment: NEXT_PUBLIC_BACKEND_URL/AGENT_URL`. | Esas variables en `environment:` **no tienen efecto** (ver abajo); `3000` en todas las interfaces. |

Comentario en el compose: el agente "NO está en docker-compose porque corre localmente en la
PC del fiscal (necesita acceso USB)".

**`server/src/Factum.Backend/Dockerfile`** — multi-stage `sdk:10.0` → `aspnet:10.0`. Instala
`libreoffice-writer` y `tzdata`. El comentario dice que `ReportService` convierte el DOCX a
PDF con `soffice`, pero en `ReportService.cs` (L1293-1295) esa conversión está marcada
**"Sin uso: el informe se entrega en DOCX. Se conserva (T16 …; sacarlo, junto con
libreoffice-writer del Dockerfile, es de otra HU)"**. O sea: la imagen carga LibreOffice
(cientos de MB) sin usarlo. `tzdata` sí hace falta (`Report:TimeZone`).
`.dockerignore` del backend ya excluye `appsettings.Local.json`, `branding/`, `bin/`, `obj/`,
`dev-data/`. El `.csproj` copia `Templates/**` (plantillas v4/v6 y `factum-sello.png`) al
publish, así que la plantilla viaja dentro de la imagen.

**Fuentes del informe.** El DOCX se arma con OpenXML (`DocumentFormat.OpenXml`); no se
renderiza en el contenedor, así que **no hacen falta fuentes en la imagen Linux**. La v6 usa
Century Gothic (títulos) y Arial (cuerpo); se ven bien al abrir el DOCX en Word sobre Windows
(Arial viene con Windows; Century Gothic viene con Office). Si el estudio abre el informe con
LibreOffice/WPS en vez de Word, Century Gothic se sustituye (ya documentado en
`Refactorizaciones/informe-diseno-modelo.md`). No afecta a la generación.

**`client/Dockerfile`** — `node:22-alpine`, `npm ci` + `npm run build` + `npm start`, copiando
`node_modules` completo a la imagen final (imagen grande). **No hay `client/.dockerignore`**:
`COPY . .` mete en el contexto `node_modules/`, `.next/` y **`client/.env.local`** del
desarrollador. `next.config.ts` hornea `NEXT_PUBLIC_BACKEND_URL` y `NEXT_PUBLIC_AGENT_URL` en
el build (`env:` con default `http://localhost:8080` / `http://localhost:8765`); por eso el
`environment:` del compose en runtime no cambia nada en el navegador. Para una instalación
de una sola PC los valores correctos son justamente los defaults, así que **hoy funciona por
casualidad**, salvo que un `.env.local` de desarrollo con otra IP se cuele en el build.
`allowedDevOrigins` tiene IPs de la red del desarrollador (solo aplica a `next dev`).

**`server/src/Factum.Agent/Dockerfile`** — publica el agente sobre `aspnet:10.0` y expone
8765. **No lo referencia nada** (ni el compose, ni el CI, ni el README). Dentro de un
contenedor Linux no tendría adb, pymobiledevice3, ffmpeg ni acceso USB: solo serviría en
modo `Mock`. Es un resto; candidato a borrarse (ver Dudas).

**Configuración del backend.** `Program.cs` (L21-43) carga `appsettings.Local.json` (opcional,
ignorado por git) después de `appsettings.{Env}.json` y antes de las variables de entorno.
El README ya documenta el uso en Docker: montarlo de solo lectura en
`/app/appsettings.Local.json` y el logo en `/app/branding/…`. Ahí van `Branding:*`
(nombre, logo, isotipo, contacto, colores) y `Report:*` (zona horaria, domicilio
constituido, textos por defecto, `EncryptZip`). El logo se lee una sola vez al arrancar.
Config inválida de `Auth`/`Integrations` = el backend **no arranca** (fail-fast, sale en
`docker logs`).

**Autenticación.** Solo hay dos modos (`AuthSettings.cs`): `dev` (cualquier DNI de 7-8
dígitos + cualquier contraseña no vacía; warning al arrancar fuera de `Development`) y
`external` (proveedor HTTP externo). **No existen usuarios locales**: la HU
`auth-e-integraciones-sin-mpf` los dejó explícitamente fuera de alcance ("Usuarios propios de
Factum (colección `users`, alta/baja, roles…) … para una HU futura"). Un estudio sin
proveedor de identidad hoy solo puede usar `dev`.

**Evidencia y secretos.** `Storage:DataDirectory` (`/data` en Docker) guarda
`cases/<caseId>/…`: el ZIP de evidencia (cifrado AES-256, contraseña aleatoria **por caso**)
y el DOCX. Tras generar, el ZIP es la **única copia** de la evidencia. La contraseña de cada
ZIP se guarda en Mongo (`cases.ZipPassword`) y el hash del informe en `report_hash`. **No hay
una clave de cifrado global por instalación**: el único secreto de instalación es
`Jwt:Secret`. Consecuencia: perder la base = perder las contraseñas de todos los ZIP; el
backup de Mongo y el de la carpeta de evidencia van juntos o no sirven.

**Esquema de Mongo.** No hay framework de migraciones; los repositorios
(`MongoRepository.cs`, `CatalogRepository.cs`, `AgentEventRepository.cs`) crean índices al
arrancar. Los casos llevan `schema_version`. Una actualización de imagen hoy no necesita
paso de migración explícito.

**Tatana (agente) y su empaquetado para Windows** — ya existe, por CI
(`.gitlab-ci.yml`, por tag `v*`):

| Artefacto | Qué trae | Limitaciones |
|---|---|---|
| **Instalador NSIS** (`agent-ui`, electron-builder, job `build-installer-win`) | UI Electron + `Factum.Agent.exe` self-contained en `resources/agent` (`agent-process.ts` lo lanza con `--port` y `--data` en `userData`). Autoupdate con `electron-updater` contra `{backend}/tatana/updates/`. | **No trae adb, Python/pymobiledevice3 ni ffmpeg**: la pantalla "Librerías" los instala con `winget install Google.PlatformTools` / `pip install pymobiledevice3` (`ipc.ts`), o sea necesita internet y un Python ya instalado. `config.ts` tiene `serverUrl` por defecto `http://localhost:5000` (el backend está en 8080; solo afecta el indicador "conectado al servidor"). |
| **Portátil** (`packaging/portable/`, job `build-portable-win`) | `Factum.Agent.exe` + `tools/platform-tools` (adb), `tools/python-embed` (Python 3.11 embebido + pymobiledevice3), `tools/ffmpeg`, `tools/uxplay` (solo si está `UXPLAY_WIN_ARTIFACT_URL`). `install-portable.bat` copia a `%LOCALAPPDATA%\Programs\Tatana`, crea acceso directo en `Startup` (sin admin); `launch-tatana.bat` arranca el agente minimizado y abre `CLIENT_URL` (default `http://localhost:3000`). Autoupdate best-effort vía `update-portable.ps1` si `UPDATE_URL` no está vacío. | Sin UI Electron (consola minimizada). |

`AdbService`/`IosService` resuelven las herramientas por ruta relativa al exe (`tools/`)
antes que por `PATH`. El agente escucha en **`0.0.0.0:8765`** (`Program.cs` L46), **sin
autenticación** y con CORS `*`: cualquiera en la red local puede listar dispositivos y
disparar capturas. En la copia de trabajo actual `server/src/Factum.Agent/appsettings.json`
tiene **`"Mock": true`** (archivo con cambios sin commitear): un build de entrega con ese
valor simularía los dispositivos.

**Actualizaciones de Tatana vía backend** (`TatanaUpdates:*`, `TatanaUpdatesService`) espejan
la última release de un proyecto GitLab con `PrivateToken`. En una PC de estudio sin acceso a
ese GitLab no aplica.

**Drivers en Windows (fuera del código).** Android: la mayoría de los equipos necesitan el
driver USB del fabricante (Samsung, Motorola…) o el Google USB Driver para que `adb` los vea.
iOS: pymobiledevice3 en Windows necesita el servicio **Apple Mobile Device Support** (viene
con iTunes o con la app "Dispositivos de Apple" de la Microsoft Store); para iOS 17+ las
funciones que abren túnel (developer/DVT) suelen requerir privilegios de administrador.

### Por qué Tatana no puede ir en Docker (validación de la propuesta)

Docker Desktop en Windows corre los contenedores dentro de una VM de WSL2 y **no pasa
dispositivos USB** a los contenedores. La única vía es **usbipd-win**: `usbipd bind`
(admin, una vez) + `usbipd attach --wsl` del dispositivo a la distro de WSL, y después
`--device`/`privileged` en el contenedor. Para el flujo de Factum es frágil:

- El attach se pierde cada vez que el celular se desconecta, se reinicia o **re-enumera el
  USB** (pasa al aceptar "Confiar en esta computadora" en iOS, al cambiar el modo USB en
  Android, al reiniciar en modo depuración). Hay `--auto-attach`, pero depende de una
  consola abierta y falla con reconexiones rápidas.
- iOS en Linux necesita además `usbmuxd` dentro del contenedor y el pairing se haría contra
  el contenedor, no contra Windows.
- La webcam (`WebcamService`, ffmpeg con dispositivos de Windows) y AirPlay (uxplay, red
  local multicast) no funcionan desde el contenedor.
- Requiere un kernel de WSL con los módulos USB/IP y permisos de admin en cada sesión.

Conclusión: la propuesta **Docker Compose para MongoDB + Backend + Frontend, y Tatana nativo
en Windows** es correcta, y el empaquetado nativo de Tatana **ya existe** (portátil). Lo
nuevo de esta HU es convertir el compose de desarrollo en un despliegue de producción local
y acompañarlo de scripts y guía.

### Qué es nuevo

1. Un compose de producción local (puertos en `127.0.0.1`, Mongo sin publicar, imágenes por
   tag de versión, `.env` por instalación, `appsettings.Local.json` y `branding/` montados
   de solo lectura, evidencia en carpeta visible de Windows).
2. Imágenes preconstruidas `linux/amd64` exportables a un `.tar` (la PC de desarrollo es
   Apple Silicon: sin `--platform linux/amd64` la imagen no corre en la PC del estudio).
3. Scripts PowerShell: instalar, actualizar, backup, restaurar (y diagnóstico).
4. Guía de instalación paso a paso para Windows.
5. Ajustes chicos de empaquetado (`.dockerignore` del client, sacar LibreOffice, etc.;
   ver Dudas).

---

## Criterios de aceptación

```gherkin
Feature: Instalación local de Factum en una PC Windows

  Background:
    Given una PC con Windows 10/11 de 64 bits con virtualización habilitada
    And Docker Desktop instalado y funcionando con el backend WSL2
    And el paquete de instalación de Factum de una versión X (imágenes, compose, scripts, guía)

  Scenario: Instalación limpia sin internet
    Given la PC no tiene acceso a internet ni al repositorio
    When el instalador corre el script de instalación
    Then se cargan las imágenes desde el paquete sin descargar nada
    And se crea la carpeta de instalación con la configuración, los datos y los backups
    And se genera un Jwt:Secret aleatorio propio de esa instalación
    And se levantan mongo, backend y frontend
    And http://localhost:3000 muestra el login de Factum
    And el script termina mostrando la URL, la carpeta de datos y los próximos pasos

  Scenario: Faltan requisitos
    Given Docker Desktop no está instalado o no está corriendo
    When se corre el script de instalación
    Then el script se detiene antes de tocar nada
    And explica qué falta y cómo resolverlo, en castellano

  Scenario: Identidad del estudio
    Given la instalación tiene appsettings.Local.json con Branding y Report del estudio y el logo en la carpeta de branding
    When un usuario genera un informe
    Then el informe sale con el nombre, logo, contacto y colores del estudio
    And esos archivos no están dentro de ninguna imagen Docker

  Scenario: Solo accesible desde la propia PC
    Given Factum instalado
    When otra computadora de la red intenta abrir los puertos 3000, 8080 o 27017 de la PC
    Then la conexión es rechazada
    And MongoDB no está publicado fuera de la red interna de Docker

  Scenario: Los datos sobreviven a reinicios
    Given hay casos generados con su ZIP y su DOCX
    When se reinicia Windows
    Then al iniciar sesión Docker Desktop arranca solo y los tres servicios vuelven a levantarse
    And los casos, sus ZIP, sus DOCX y sus contraseñas siguen disponibles

  Scenario: Backup completo
    Given Factum instalado con casos generados
    When se corre el script de backup con un destino (por ejemplo un disco externo)
    Then se genera un backup fechado con el volcado de MongoDB, la carpeta de evidencia y la configuración de la instalación
    And el backup incluye un manifiesto con el SHA-256 de cada archivo
    And ningún archivo de evidencia original se modifica

  Scenario: Restauración en la misma u otra PC
    Given un backup generado por el script
    When se corre el script de restauración sobre una instalación vacía de la misma versión
    Then se verifican los SHA-256 del manifiesto antes de restaurar
    And los casos, la evidencia y las contraseñas de los ZIP quedan como estaban
    And un ZIP restaurado tiene el mismo SHA-256 que figura en su informe

  Scenario: Restauración con backup corrupto
    Given un backup con un archivo modificado o faltante
    When se corre el script de restauración
    Then el script se detiene antes de tocar la instalación y lista los archivos que no coinciden

  Scenario: Actualización sin pérdida de datos
    Given Factum versión X con datos
    When se corre el script de actualización con el paquete de la versión Y
    Then primero se hace un backup automático
    And se cargan las imágenes de Y y se recrean los contenedores
    And los casos y la evidencia previos siguen disponibles
    And si el backend de Y no arranca, el script vuelve a la versión X y lo informa

  Scenario: Configuración inválida
    Given un appsettings.Local.json o un .env con un valor inválido
    When se levanta el sistema
    Then el script informa que el backend no arrancó y muestra las últimas líneas del log

  Scenario: Tatana detecta un celular Android
    Given Tatana instalado de forma nativa en Windows y el driver USB del fabricante instalado
    And un Android con depuración USB autorizada conectado por USB
    When el perito abre el wizard de Factum en http://localhost:3000
    Then el dispositivo aparece en el wizard y se puede capturar pantalla

  Scenario: Tatana detecta un iPhone
    Given Tatana instalado de forma nativa y Apple Mobile Device Support instalado
    And un iPhone que confía en la PC conectado por USB
    When el perito abre el wizard
    Then el dispositivo aparece en el wizard y se puede capturar pantalla

  Scenario: Tatana arranca con Windows y no está en modo simulado
    Given Tatana instalado
    When se inicia sesión en Windows
    Then el agente escucha en localhost:8765 sin intervención del usuario
    And /health informa que no está en modo mock
```

---

## Datos que se configuran por instalación

| Dato | Obligatorio | Dónde vive | Uso |
|---|---|---|---|
| `Jwt:Secret` | Sí | `.env` (generado al instalar, aleatorio ≥ 64 bytes) | Firma de los tokens de sesión. Nunca el valor commiteado. |
| Versión de imágenes (`FACTUM_VERSION`) | Sí | `.env` | Tag de `factum-backend` / `factum-frontend`; permite volver atrás. |
| `Auth:Mode` | Sí | `.env` | `dev` o `external` (ver Dudas D7). |
| Carpeta de instalación / datos | Sí | `.env` (default sugerido `C:\Factum\`) | Raíz de config, evidencia y backups. |
| `Branding:*` (nombre, logo, isotipo, contacto, colores) | No (sin esto el informe sale sin membrete) | `appsettings.Local.json` montado `:ro` + carpeta `branding/` montada `:ro` | Identidad del estudio en informe y login. |
| `Report:DomicilioConstituido`, `Report:DefaultTexts:*`, `Report:TimeZone` | No | `appsettings.Local.json` | Textos y datos del informe. |
| `Report:EncryptZip` | No (default `true`) | `appsettings.Local.json` | Cifrado del ZIP. |
| `Audit:AdminDnis` | No | `appsettings.Local.json` | Quién puede leer la auditoría del agente. |
| Destino de backup | Sí para backup | parámetro del script / `.env` | Carpeta o disco externo. |
| Contraseña de Mongo | No (ver D10) | `.env` | Solo si se decide habilitar auth en Mongo. |

No se configura ninguna "clave de cifrado del ZIP" por instalación: la contraseña es por
caso y vive en Mongo.

---

## Diseño de la experiencia de instalación (no hay pantallas nuevas en `client/` ni en `agent-ui/`)

**Paquete entregable** (una carpeta o ZIP, p. ej. `Factum-Instalacion-vX.Y.Z/`):

```
Factum-Instalacion-vX.Y.Z/
├── LEEME.txt                      ← 10 líneas: "abrí docs/instalacion-windows.md"
├── instalacion-windows.md / .pdf  ← guía paso a paso
├── imagenes/factum-vX.Y.Z.tar     ← backend + frontend + mongo:7 (docker save)
├── docker-compose.yml             ← compose de producción local
├── .env.example
├── appsettings.Local.example.json
├── scripts/
│   ├── instalar.ps1
│   ├── actualizar.ps1
│   ├── backup.ps1
│   ├── restaurar.ps1
│   └── diagnostico.ps1            ← estado de contenedores, logs, puertos, versión
└── tatana/Tatana-Portable-vX.Y.Z-Windows.zip   (o el instalador, según D4)
```

**Carpeta en la PC** (sugerida): `C:\Factum\` con `config\` (`.env`,
`appsettings.Local.json`, `branding\`), `evidencia\` (bind mount de `/data`), `backups\`,
`logs\` (salida de los scripts).

**Scripts (consola PowerShell):**
- Se lanzan con doble clic vía acceso directo o `.bat` envoltorio (evita pelear con
  `ExecutionPolicy`).
- Cada paso numerado (`[1/7] Verificando Docker Desktop…  OK`), en castellano, sin jerga
  innecesaria.
- Antes de cualquier operación destructiva (restaurar, volver de versión) piden confirmación
  escribiendo "SI".
- Errores: mensaje humano + qué hacer + ruta del log completo. Nunca un stack trace solo.
- Al terminar `instalar.ps1`: crea en el escritorio los accesos "Factum" (abre
  `http://localhost:3000`), "Backup de Factum" y, opcional, programa el backup en el
  Programador de tareas.

**Guía `instalacion-windows.md`:** requisitos de hardware/SO, habilitar virtualización,
instalar Docker Desktop (con "Start when you sign in"), licencia de Docker Desktop, correr
`instalar.ps1`, completar la identidad del estudio, instalar Tatana y drivers (Android /
Apple), prueba de humo (login, caso de prueba, captura, generar informe), backup y
restauración, actualización, diagnóstico y problemas comunes (WSL2 desactualizado, puerto
ocupado, celular no detectado).

---

## Fuera de alcance

- Usuarios locales con contraseña / roles (ver D7: se propone como HU propia).
- Acceso desde otras PCs de la red, HTTPS, dominio, reverse proxy (una sola PC confirmada).
- Meter Tatana en Docker (ver Contexto y D2).
- Cambios funcionales en `client/`, `agent-ui/`, la API o el informe.
- Instalador "todo en uno" que también instale Docker Desktop y los drivers de forma
  desatendida.
- Backups remotos/en la nube y cifrado de los backups (el ZIP de evidencia ya está cifrado;
  ver D9).
- Integración de soporte (Faro) y `TatanaUpdates` contra GitLab en la PC del estudio
  (quedan apagados/vacíos).
- Migrar el CI de GitLab a otro proveedor; publicar imágenes en un registry público.
- Firmar digitalmente el instalador de Tatana (SmartScreen va a avisar; se documenta).
- Borrar el código muerto de conversión a PDF en `ReportService.cs` (solo se toca el
  Dockerfile si se aprueba D13).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- Imágenes con `docker buildx build --platform linux/amd64` y tag por versión;
  `docker save factum-backend:X factum-frontend:X mongo:7 -o factum-vX.tar`. Fijar `mongo:7`
  a un tag concreto (`mongo:7.0.x`) para que restaurar un backup no cambie de minor.
- Compose de producción: `ports: "127.0.0.1:3000:3000"` y `"127.0.0.1:8080:8080"`; Mongo sin
  `ports:`; `env_file: .env`; montajes `:ro` de `appsettings.Local.json` y `branding/`.
- El navegador corre en Windows: `NEXT_PUBLIC_BACKEND_URL=http://localhost:8080` y
  `NEXT_PUBLIC_AGENT_URL=http://localhost:8765` se hornean en el build de la imagen
  (build args), no en runtime.
- Backup de Mongo: `docker compose exec mongo mongodump --archive --gzip` hacia el host;
  restaurar con `mongorestore --drop` solo sobre una instalación vacía/confirmada.
- Regla dura de datos de `AGENTS.md`: las pruebas de los scripts se hacen contra una
  instalación descartable (otro `COMPOSE_PROJECT_NAME` y otra carpeta), nunca contra la base
  ni la evidencia de desarrollo del usuario.
- No hay tests automatizados de despliegue; la verificación es una prueba de humo
  documentada en la guía (idealmente en una VM Windows limpia).

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. ¿Una HU o dos?**
- A) Esta HU cubre el servidor en Docker + scripts + guía, y para Tatana **usa el portátil
  que ya existe** (sin código nuevo, solo documentado en la guía y con `Mock=false`). Si
  después hace falta la UI Electron con herramientas incluidas, va en una HU aparte
  (`instalador-tatana-windows`).
- B) Una sola HU con todo, incluido rehacer el instalador NSIS de `agent-ui` para que traiga
  adb/Python/ffmpeg.
- C) Dos HU desde ya: `instalacion-local-docker` (servidor) e `instalador-tatana-windows`.
- **Recomendada: A.** El portátil ya resuelve lo difícil (adb, Python embebido con
  pymobiledevice3, ffmpeg, autostart sin admin). Rehacer el NSIS es trabajo de `agent-ui`
  + CI que no bloquea la instalación del estudio, y mezclarlo agranda la HU sin necesidad.

**D2. ¿Tatana nativo o en Docker con usbipd-win + WSL2?**
- A) Nativo en Windows (lo que hay hoy).
- B) Contenedor con USB vía usbipd-win.
- C) Todo nativo, sin Docker (Mongo como servicio de Windows, backend como servicio, Next
  con Node): evita WSL2/virtualización, pero son tres instalaciones manuales y más pasos al
  actualizar.
- **Recomendada: A (para Tatana) + Docker para el resto.** B es frágil (se pierde el attach
  en cada reconexión o re-enumeración del USB, necesita admin y usbmuxd para iOS, y la webcam
  y AirPlay no funcionan; ver Contexto). C es válida si la PC no soporta virtualización:
  conviene dejarla como plan B en la guía, no como camino principal.

**D3. ¿Quién hace la instalación y quién la mantiene?**
- A) La instalás vos (en sitio o remoto); el estudio solo usa los accesos directos de
  backup/abrir Factum. La guía está escrita para alguien técnico.
- B) La instala personal del estudio siguiendo la guía.
- **Recomendada: A.** Hay pasos que necesitan criterio (BIOS/virtualización, drivers,
  primer pairing de iPhone, identidad del estudio). Afecta el tono de la guía y cuánto hay
  que automatizar.

**D4. ¿Qué forma de Tatana se entrega?**
- A) **Portátil** (`Tatana-Portable-…-Windows.zip`): trae las herramientas, no requiere
  admin ni internet, arranca con la sesión. Sin la ventana Electron.
- B) **Instalador NSIS** de `agent-ui`: tiene UI, pero instala adb/pymobiledevice3 con
  `winget`/`pip` (requiere internet y Python) y su `serverUrl` por defecto apunta a
  `localhost:5000`.
- **Recomendada: A.** Es la única variante que hoy funciona sin internet y sin instalar
  Python. Si el estudio quiere la UI, ver D1.

**D5. ¿Cómo llegan las imágenes a la PC del estudio?**
- A) **Preconstruidas y exportadas a `.tar`** (`docker save`) dentro del paquete; la PC hace
  `docker load`. Build en tu Mac con `--platform linux/amd64`.
- B) Build en la PC del estudio (necesita el repo, internet y descargar SDK de .NET + Node;
  deja código fuente en el cliente).
- C) Registry privado (GitLab Container Registry, etc.) con credenciales en la PC.
- **Recomendada: A.** No depende de internet ni del repo, no deja el código fuente en el
  cliente y la versión instalada es exactamente la probada. C es cómoda para actualizar,
  pero agrega credenciales y dependencia de red.

**D6. ¿Dónde vive la configuración por instalación?**
- A) `.env` (secretos y parámetros del compose: versión, `Jwt__Secret`, `Auth__Mode`,
  rutas) generado por `instalar.ps1`, + `appsettings.Local.json` y `branding/` montados de
  solo lectura (lo que ya documenta el README), todo en `C:\Factum\config\`.
- B) Todo como variables de entorno en un `docker-compose.override.yml`.
- **Recomendada: A.** Reusa el mecanismo existente (`Program.cs` + README), las listas
  (`ContactLines`) y los textos largos de `Report:DefaultTexts` son mucho más cómodos en JSON
  que en variables `__0`, y el `.env` separa lo secreto. `ASPNETCORE_ENVIRONMENT=Production`
  fijo.

**D7. Autenticación en la PC del estudio (hoy no hay usuarios locales).**
- A) Instalar con `Auth:Mode=dev` y aceptar el riesgo **documentado**: los puertos solo
  escuchan en `127.0.0.1`, así que la barrera real es la sesión de Windows. Crear después una
  HU `usuarios-locales` (alta de usuarios con contraseña hasheada en Mongo, un tercer
  `Auth:Mode=local`).
- B) Bloquear esta HU hasta tener usuarios locales.
- C) `external` contra algún proveedor del estudio (no consta que tengan uno).
- **Recomendada: A**, con la condición de que la guía lo diga claro y que cada perito use su
  propio DNI/usuario (la autoría de los casos y la auditoría dependen de eso). Con una sola
  PC y acceso solo local, `dev` no abre la puerta a la red; sí permite que cualquiera con la
  sesión de Windows abierta entre como cualquier perito. **Necesito tu decisión** porque
  para un uso forense real puede no ser aceptable.

**D8. Persistencia: ¿volúmenes Docker o carpetas visibles de Windows?**
- A) **Evidencia** en carpeta de Windows (bind mount `C:\Factum\evidencia` → `/data`);
  **Mongo** en volumen Docker con nombre.
- B) Las dos en volúmenes Docker (solo accesibles con los scripts).
- C) Las dos en carpetas de Windows.
- **Recomendada: A.** La evidencia queda visible y copiable sin Docker (útil ante un
  problema y para entregar un ZIP a mano). Mongo sobre NTFS montado en WSL2 es lento y tiene
  problemas conocidos con WiredTiger; en volumen es estable y se respalda con `mongodump`.
  Advertencia en la guía: la carpeta de evidencia no se edita a mano.

**D9. Backup: alcance y frecuencia.**
- A) `backup.ps1` = `mongodump` + copia de `evidencia\` + `config\` (incluye `.env` con el
  JWT y `appsettings.Local.json`/branding) en `backups\AAAA-MM-DD_HHMM\` o en un destino
  elegido, con `manifiesto-sha256.txt`; acceso directo en el escritorio y **opción** de
  tarea programada diaria; retención de los últimos N. `restaurar.ps1` verifica el manifiesto
  antes de tocar nada.
- B) Solo manual, sin manifiesto.
- **Recomendada: A.** Sin la base no hay contraseñas de los ZIP, y sin el manifiesto no se
  puede demostrar que la evidencia restaurada es la original. Sub-preguntas: ¿el estudio
  tiene disco externo o NAS? ¿cada cuánto (diario al apagar, semanal)? ¿cuántos backups
  conservar? El backup no se cifra aparte (los ZIP ya están cifrados), pero contiene la base
  con las contraseñas en claro: **el destino tiene que estar resguardado**.

**D10. Red y puertos.**
- A) Frontend y backend publicados solo en `127.0.0.1`; Mongo **sin publicar** (solo red
  interna de Docker), sin usuario/contraseña de Mongo; sin HTTPS (todo es localhost).
- B) Igual que A pero con autenticación en Mongo.
- **Recomendada: A.** Con Mongo sin puerto publicado, solo el backend lo ve; auth de Mongo
  agrega otra credencial que guardar sin ganar protección real en una PC única. Para
  depurar, `diagnostico.ps1` usa `docker compose exec mongo mongosh`.

**D11. El agente Tatana escucha en `0.0.0.0:8765` sin auth y con CORS `*`.**
- A) Cambiar el bind a `127.0.0.1` (una línea en `server/src/Factum.Agent/Program.cs`, quizá
  configurable con `Agent:BindAddress`) dentro de esta HU.
- B) Dejarlo y bloquear con el firewall de Windows (la primera ejecución muestra el aviso;
  la guía dice "No permitir" en redes públicas y privadas).
- C) Dejarlo así.
- **Recomendada: A.** Hoy cualquiera en la Wi-Fi del estudio puede listar el celular
  conectado y disparar capturas. Con una sola PC no se necesita acceso remoto al agente.
  Ojo: hay que verificar que la grabación AirPlay (uxplay) no dependa de ese bind; es un
  servicio aparte.

**D12. Arranque automático.**
- A) Docker Desktop con "Start when you sign in" + `restart: unless-stopped`; Tatana con el
  acceso directo en `Startup` del portátil. Todo arranca **al iniciar sesión** del usuario.
- B) Docker Engine dentro de WSL2 sin Docker Desktop, como servicio que arranca sin login
  (no tiene licencia comercial que considerar, pero es más difícil de instalar y mantener).
- **Recomendada: A.** En una PC de escritorio el perito siempre inicia sesión; B es más
  frágil para alguien que no administra Linux. La guía avisa que tras reiniciar Docker tarda
  1-2 minutos y que `instalar.ps1` deja un acceso "Factum" que espera a que el backend
  responda antes de abrir el navegador.

**D13. LibreOffice en la imagen del backend.**
- A) Sacar `libreoffice-writer` del `Dockerfile` en esta HU (queda `tzdata`) y corregir el
  comentario; el código muerto de `ReportService` queda para otra HU.
- B) Dejarlo.
- **Recomendada: A.** El propio `ReportService.cs` marca la conversión a PDF como "Sin
  uso"; LibreOffice suma cientos de MB al `.tar` que se lleva al estudio y a cada
  actualización. No hacen falta fuentes en la imagen: el DOCX se arma con OpenXML y se abre
  en Word en Windows. Pregunta asociada: ¿el estudio abre los informes con **Microsoft Word**?
  Si usa LibreOffice/WPS, Century Gothic se sustituye (es estético, no cambia el contenido).

**D14. Imagen del frontend.**
- A) Agregar `client/.dockerignore` (excluye `node_modules`, `.next`, `.env*`), pasar
  `NEXT_PUBLIC_*` como build args y usar `output: "standalone"` en `next.config.ts` para no
  copiar todo `node_modules` a la imagen final.
- B) Solo el `.dockerignore`.
- C) Dejarlo como está.
- **Recomendada: A.** Hoy el `.env.local` del desarrollador entra al build y define URLs que
  quedan horneadas; el `environment:` del compose no tiene efecto. `standalone` reduce la
  imagen a una fracción. El `implementer-frontend` tiene que verificar `standalone` contra la
  doc de Next 16 que trae el repo (`client/AGENTS.md`).

**D15. Actualizaciones.**
- A) `actualizar.ps1 <paquete vY>`: backup automático → `docker load` → cambia
  `FACTUM_VERSION` en `.env` → `docker compose up -d` → espera el healthcheck del backend →
  si falla, vuelve al tag anterior y avisa. Tatana se actualiza reemplazando el portátil
  (`UPDATE_URL` vacío en `tatana-portable.ini`). Sin migraciones explícitas (no hay
  framework; los índices se crean al arrancar).
- B) Actualización manual siguiendo la guía.
- **Recomendada: A.** Hace imposible "actualizar sin backup" y permite volver atrás. Si una
  HU futura cambia el esquema de forma incompatible, su SDD agrega el paso de migración al
  script. Nota: volver de versión **no** deshace cambios que la versión nueva haya escrito en
  Mongo; para eso está el backup previo.

**D16. Healthcheck del backend.**
- A) Agregar `healthcheck` al servicio `backend` (y `frontend` con `depends_on:
  condition: service_healthy`) usando un endpoint existente (p. ej. `GET
  /api/config/public`), para que los scripts sepan cuándo el sistema está listo.
- B) Los scripts solo esperan N segundos.
- **Recomendada: A.** Lo necesitan `instalar.ps1`, `actualizar.ps1` (rollback) y el acceso
  "Factum". La imagen `aspnet` no trae `curl`: la SDD decide si se agrega o se usa otra
  forma de chequeo.

**D17. ¿Qué hacer con `server/src/Factum.Agent/Dockerfile`?**
- A) Borrarlo (no lo usa nada y sugiere que el agente se puede dockerizar).
- B) Dejarlo con un comentario "solo modo mock, para demos".
- **Recomendada: A.** Evita que alguien lo use creyendo que sirve en producción.

**D18. Licencia y requisitos de Docker Desktop.**
- Docker Desktop es gratuito para organizaciones de **menos de 250 empleados y menos de USD
  10 M de facturación anual**; un estudio jurídico chico entra. Requiere Windows 10/11 de 64
  bits con WSL2 y virtualización habilitada en BIOS/UEFI; recomendable 16 GB de RAM (8 GB es
  el mínimo práctico con WSL2 + Mongo + Next + el navegador).
- **Recomendada:** dejarlo explícito en la guía y que **vos confirmes** el tamaño del estudio
  y que la PC cumple (¿qué PC es: RAM, Windows Home o Pro, admite virtualización?).

**D19. Entregables y dónde viven en el repo.**
- A) `deploy/windows/` con `docker-compose.yml` de producción, `.env.example`,
  `appsettings.Local.example.json`, `scripts/*.ps1` y un script de empaquetado (en tu Mac)
  que construye las imágenes `linux/amd64`, hace `docker save` y arma el ZIP del paquete;
  guía en `docs/instalacion-windows.md`. El `docker-compose.yml` de la raíz queda para
  desarrollo (solo se le corrige lo que confunde).
- B) Reemplazar el `docker-compose.yml` de la raíz por el de producción.
- **Recomendada: A.** Separa desarrollo de instalación y no rompe `docker compose up --build`
  del README. Nota: `docs/` hoy guarda las HU del arnés; si preferís, la guía puede ir en
  `deploy/windows/INSTALACION.md`.

**D20. `Agent:Mock` en la entrega.**
- A) Asegurar que el `appsettings.json` del agente quede con `"Mock": false` en el commit (y
  que el script de empaquetado lo verifique), y usar `--mock` por línea de comandos para
  desarrollo.
- B) Solo cuidarlo a mano antes de cada release.
- **Recomendada: A.** En la copia de trabajo actual está en `true`; un portátil armado así
  "funciona" pero simula los celulares, que en un contexto forense es grave.

## Validación (2026-10-02)

El usuario validó las 20 dudas en la opción **A** (recomendada). D7 confirmada explícitamente: instalar con `Auth:Mode=dev` por ahora (HU posterior `usuarios-locales`). D18: el estudio tiene < 250 empleados y < USD 10 M (licencia gratuita de Docker Desktop OK); la PC es Windows 10 (versión exacta, RAM y virtualización desconocidas) → `instalar.ps1` debe chequear requisitos (Windows 10 22H2+ 64 bits, RAM, virtualización habilitada, WSL2) y explicar claramente qué falta antes de instalar.
