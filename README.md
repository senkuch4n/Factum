# Factum

Sistema de adquisición forense de evidencia digital para dispositivos móviles.
Permite a un perito u operador crear un expediente, conectar un celular
(Android o iOS) por USB, capturar evidencia (fotos, video de pantalla,
capturas) y generar un informe forense (DOCX) + un paquete ZIP cifrado con
hash, todo sin que la evidencia original salga del dispositivo del usuario
hacia un servidor de terceros. El informe sale con la identidad de la
organización que lo emite (ver
[Identidad de la organización (Branding)](#identidad-de-la-organización-branding)).

Como **integración opcional de soporte** (apagada por defecto) se puede
conectar con **[Faro](https://gitlab.com/joelserrudo/faro-sistema-de-tokens)**,
una mesa de ayuda (sistema de tokens): el usuario reporta un problema técnico
de Factum y le hace seguimiento sin salir de la app, con la misma identidad
(DNI) — ver [Integración de soporte (Faro)](#integración-de-soporte-faro).

## Arquitectura

El proyecto tiene 4 partes que corren por separado:

```
┌─────────────────┐      HTTP       ┌──────────────────────┐      MongoDB
│  client/         │ ───────────────▶│  server/.../Backend  │ ───────────────▶ (mongo)
│  Next.js (web)   │◀─────────────── │  ASP.NET Core API    │
└─────────────────┘   JSON + JWT     └──────────┬───────────┘
        │                                        │
        │ WebSocket + HTTP (localhost:8765)      │ HTTP + X-Service-Key
        ▼                                        ▼
┌─────────────────┐                    ┌──────────────────────┐
│  server/.../Agent │  (proceso local) │  Faro (soporte,       │
│  "Tatana"          │ ◀── ADB/USB ──  │  gestión de tokens)   │
│  habla con el      │     dispositivo │  opcional)            │
│  celular por USB   │                 └──────────────────────┘
└─────────────────┘
        ▲
        │ empaqueta y da UI de bandeja
┌─────────────────┐
│  agent-ui/       │
│  Electron        │
└─────────────────┘
```

- **`client/`** — Frontend web en Next.js 16 (App Router) + React 19 +
  Tailwind. Login, dashboard, wizard de inspección (guía USB → conectar
  dispositivo → datos del expediente → captura → generar informe → resultado),
  historial de casos, y el modal de soporte (solo si la integración
  opcional de soporte está habilitada).
- **`server/src/Factum.Backend`** — API en ASP.NET Core (.NET 10) +
  MongoDB. Autenticación JWT, gestión de casos/expedientes, generación de
  informes (DOCX + ZIP cifrado), y el cliente HTTP de la integración
  opcional de soporte (Faro).
- **`server/src/Factum.Agent`** ("Tatana") — Un segundo servicio ASP.NET
  Core que **corre en la PC del oficial**, no en el servidor. Se comunica con
  el celular conectado por USB (Android vía ADB, iOS vía `pymobiledevice3`) y
  expone una API local + WebSocket que consume el `client`. Por eso **no**
  está en `docker-compose.yml`: necesita acceso físico al puerto USB de la
  máquina donde se ejecuta.
- **`agent-ui/`** — Envoltorio de escritorio en Electron para Tatana: permite
  instalar/actualizar las librerías que necesita el agente, ver su estado de
  conexión, configurar la URL del backend, e iniciarlo en segundo plano al
  prender la PC. Ver [`AGENTE_TATANA.md`](./AGENTE_TATANA.md) para el detalle
  de casos de uso.

## Stack

| Parte | Tecnología |
|---|---|
| Frontend web | Next.js 16, React 19, Tailwind CSS, Framer Motion |
| Backend API | ASP.NET Core (.NET 10), MongoDB.Driver |
| Agente local | ASP.NET Core (.NET 10), WebSockets, ADB / pymobiledevice3 |
| UI del agente | Electron + Vite |
| Base de datos | MongoDB 7 |
| Auth | JWT (access token corto) — modos `dev` (mock) y `external` (proveedor HTTP externo genérico) |

## Requisitos previos

- [Node.js](https://nodejs.org/) 20+ y npm
- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) + Docker Compose (para el modo rápido)
- Para capturar dispositivos reales: `adb` (Android) y/o `pymobiledevice3`
  (iOS) instalados y en el `PATH` — el agente los usa para hablar con el
  celular. Sin esto, el agente puede correr en modo `Mock` (ver más abajo).

## Puesta en marcha rápida (Docker)

Levanta MongoDB + Backend + Frontend con un solo comando:

```bash
docker compose up --build
```

- Frontend: http://localhost:3000
- Backend: http://localhost:8080
- MongoDB: localhost:27017

El agente **no** se levanta con Docker (ver arriba por qué) — para probar el
flujo de captura de un dispositivo real, corré el agente por separado como se
explica en la siguiente sección.

## Puesta en marcha en modo desarrollo (sin Docker)

Útil para desarrollar con hot-reload en las 4 partes.

### 1. MongoDB

```bash
docker run -d --name factum-mongo -p 27017:27017 mongo:7
```

### 2. Backend (`server/src/Factum.Backend`)

```bash
cd server/src/Factum.Backend
dotnet restore
dotnet run
```

Corre por defecto en `http://localhost:8080` (revisar `Properties/launchSettings.json`
si tu puerto difiere). Configuración en `appsettings.json` — ver
[Variables de entorno / configuración](#variables-de-entorno--configuración).

### 3. Frontend (`client`)

```bash
cd client
npm install
npm run dev
```

Corre en `http://localhost:3000`. Necesita un archivo `.env.local` (ver abajo)
apuntando al backend y al agente.

```bash
# client/.env.local
NEXT_PUBLIC_BACKEND_URL=http://localhost:8080
NEXT_PUBLIC_AGENT_URL=http://localhost:8765
```

### 4. Agente Tatana (`server/src/Factum.Agent`)

Corre en la máquina del oficial, no en el servidor:

```bash
cd server/src/Factum.Agent
dotnet restore
dotnet run
```

Por defecto expone `http://localhost:8765`. Si no tenés `adb`/`pymobiledevice3`
instalados y solo querés probar el flujo end-to-end sin un celular físico,
activá el modo mock en `appsettings.json`:

```json
{ "Agent": { "Port": 8765, "Mock": true, "DataDirectory": "./agent-data" } }
```

### 5. (Opcional) UI de escritorio del agente (`agent-ui`)

```bash
cd agent-ui
npm install
npm run dev       # ventana Electron en modo desarrollo
npm run package   # empaqueta la app instalable
```

## Variables de entorno / configuración

**`server/src/Factum.Backend/appsettings.json`**

| Clave | Qué es |
|---|---|
| `MongoDb:ConnectionString` / `DatabaseName` | Conexión a Mongo |
| `Jwt:Secret` / `ExpiryHours` | Firma y expiración del token de sesión |
| `Auth:Mode` | `dev` (identidad simulada, default) o `external` (login real contra un proveedor HTTP externo). Sin distinguir mayúsculas; cualquier otro valor impide arrancar |
| `Auth:External:BaseUrl` / `LoginPath` / `TimeoutSeconds` | Solo si `Auth:Mode=external`. Se hace `POST {BaseUrl}{LoginPath}` (default `/auth/login`); timeout entre 1 y 120 s (default 10) |
| `Auth:External:Request:DniField` / `UserField` / `PasswordField` | Nombres de los campos del body JSON que se manda al proveedor (default `dni` / `user` / `password`). Tienen que ser distintos |
| `Auth:External:Response:NameField` / `SiglaField` | Campos que se leen de la respuesta (default `name` / `sigla`). Admiten rutas con puntos (`data.user.fullName`). `SiglaField` vacío = no se lee sigla |
| `Integrations:Support:Enabled` | `true` activa la integración opcional de soporte (Faro). Default `false` |
| `Integrations:Support:BaseUrl` | URL del backend de Faro (`http://localhost:5038` en local). Obligatoria si está habilitada |
| `Integrations:Support:ServiceKey` | Clave compartida servicio-a-servicio con Faro (debe coincidir con `Integrations:ServiceKey` de Faro). Obligatoria si está habilitada. **No va en el repo**: `appsettings.Local.json` o variable de entorno |
| `Integrations:Support:TimeoutSeconds` | Timeout de las llamadas a Faro, entre 1 y 120 s (default 10) |
| `Integrations:Support:FrontendUrl` | URL del frontend de Faro, para armar el link de SSO (`http://localhost:3001` en local). Obligatoria si está habilitada |
| `Storage:DataDirectory` | Carpeta local donde se guardan los ZIP/PDF generados (dev) |
| `Audit:AdminDnis` | DNIs habilitados a leer `GET /api/agent-events` (auditoría de uso del agente) |
| `TatanaUpdates:ProjectId` / `ProjectRawBaseUrl` / `PrivateToken` | Proyecto de GitLab del que se espeja la última release de Tatana |
| `TatanaUpdates:PublicBaseUrl` | URL pública de este backend — a la que apuntan el instalador Electron y el `.bat` portátil para actualizarse |
| `Branding:OrganizationName` / `OrganizationLogo` / `ContactLines` | Identidad de la organización que emite los informes — ver [Branding](#identidad-de-la-organización-branding). Vacío en el repo |
| `Report:TimeZone` / `DomicilioConstituido` / `DefaultTexts:*` | Zona horaria, domicilio constituido y textos por defecto del informe pericial — ver [Informe pericial](#informe-pericial-configuración). Domicilio vacío en el repo |

**`client/.env.local`**

| Variable | Qué es |
|---|---|
| `NEXT_PUBLIC_BACKEND_URL` | URL del backend |
| `NEXT_PUBLIC_AGENT_URL` | URL del agente local (Tatana) |

**`server/src/Factum.Agent/appsettings.json`**

| Clave | Qué es |
|---|---|
| `Agent:Port` | Puerto donde escucha el agente (8765 por defecto) |
| `Agent:Mock` | `true` simula dispositivos sin USB real — útil para desarrollar sin celular a mano |
| `Agent:DataDirectory` | Carpeta temporal de capturas antes de subirlas al backend |

> `Jwt:Secret` sigue commiteado con un valor de desarrollo, pensado para correr
> todo en local: rotalo antes de cualquier despliegue real. La `ServiceKey` de
> soporte **ya no está en el repo**: va en `appsettings.Local.json` (ignorado por
> git) o en la variable de entorno `Integrations__Support__ServiceKey`. La clave
> que quedó expuesta en el historial de git hay que rotarla (en Faro y en tu
> config local).

## Autenticación

El login pide **DNI + usuario + contraseña**. El DNI identifica al usuario
(si la integración de soporte está activa, es la misma identidad que usa Faro).

- **Modo `dev`** (default): cualquier DNI de 7-8 dígitos y cualquier
  contraseña no vacía autentican. El nombre se arma a partir del usuario con
  la convención `nombre.apellido` (ej: usuario `carlos.mendoza` → "Carlos
  Mendoza"). No hace falta pre-registrar a nadie: el usuario se crea la
  primera vez que loguea.
  Si el backend corre con `Auth:Mode=dev` fuera de `Development`, avisa en el
  log de arranque que acepta cualquier contraseña.
- **Modo `external`**: valida contra un proveedor HTTP externo genérico
  (`POST {Auth:External:BaseUrl}{Auth:External:LoginPath}`). Los nombres de los
  campos del request y de la respuesta se configuran en
  `Auth:External:Request:*` / `Auth:External:Response:*` (la respuesta admite
  rutas con puntos). Un 401/403 del proveedor es "Credenciales inválidas"; un
  proveedor caído, lento o con 5xx da "No se pudo conectar al servicio de
  autenticación…", y cualquier otra respuesta rara, "El servicio de
  autenticación respondió de forma inesperada." El detalle técnico va al log,
  nunca a la pantalla.

Si `Auth:Mode` (o la config de `external`) es inválida, **el backend no
arranca** y dice por qué, en vez de caer en `dev` sin avisar. El log de
arranque muestra el modo y, en `external`, la URL de login efectiva.

> Compatibilidad: `Auth:Mode=mpf` y `Auth:MpfBaseUrl`/`MpfLoginPath`/`MpfTimeoutSeconds`
> se aceptan como legado, con un warning al arrancar.

## Identidad de la organización (Branding)

Factum es un producto: el **emisor** del informe es la organización cliente
(un estudio, un gabinete, un perito). Su nombre, su logo y sus datos de
contacto se configuran en el backend, en la sección `Branding`, y se usan en
el informe y en la web (login, menú de usuario y pie, vía
`GET /api/config/public`).

| Clave | Tipo | Qué es |
|---|---|---|
| `Branding:OrganizationName` | texto | Nombre del emisor. Máx. 150 caracteres (se trunca con un warning). Vacío = no configurado. |
| `Branding:OrganizationLogo` | ruta | Logo del emisor: ruta absoluta o relativa al directorio del backend (`/app` en Docker). |
| `Branding:ContactLines` | lista de textos | Domicilio, teléfonos, correo, matrícula… Máx. 6 líneas de 150 caracteres. No se expone a la web. |

**Logo:** PNG o JPEG (se valida por contenido, no por extensión; SVG no se
acepta), de hasta **1 MiB** y entre **16 y 4096 px** por lado. Para fondo
transparente, PNG. Si es inválido, el backend loguea
`Branding: logo ignorado (<motivo>)` y sigue sin logo: ni el arranque ni los
informes fallan. El logo se lee **una sola vez al arrancar**: para cambiarlo
(o cambiar el nombre o el contacto) hay que **reiniciar el backend**. Se sirve
desde memoria en `GET /api/config/branding/logo`.

**Los datos reales del cliente nunca van al repo.** En el
`appsettings.json` versionado la sección está vacía, y
`appsettings.Development.json` también está versionado, así que no sirve para
esto:

- **Desarrollo local:** `server/src/Factum.Backend/appsettings.Local.json`
  (ignorado por git, se carga después de `appsettings.{Environment}.json` y
  antes de las variables de entorno) y el logo en
  `server/src/Factum.Backend/branding/` (también ignorada). Ninguno de los dos
  se copia a `bin/`, a `publish/` ni a la imagen Docker.
- **Docker / producción:** variables de entorno, o el mismo
  `appsettings.Local.json` montado como volumen de solo lectura en
  `/app/appsettings.Local.json`. El logo, también como volumen de solo lectura.

Ejemplo de `appsettings.Local.json` (valores ficticios):

```json
{
  "Branding": {
    "OrganizationName": "Dr. Nombre Apellido · Dra. Nombre Apellido",
    "OrganizationLogo": "branding/logo.png",
    "ContactLines": [
      "Calle Ejemplo 123, Ciudad",
      "Cel. +54 9 000 000-0000 · +54 9 000 000-0000"
    ]
  }
}
```

Lo mismo con variables de entorno, en el servicio `backend` de un
`docker-compose.override.yml` (valores ficticios):

```yaml
services:
  backend:
    environment:
      Branding__OrganizationName: "Estudio Jurídico Ejemplo"
      Branding__OrganizationLogo: "/app/branding/logo.png"
      Branding__ContactLines__0: "Calle Ejemplo 123, Ciudad"
      Branding__ContactLines__1: "Cel. +54 9 000 000-0000"
    volumes:
      - ./branding/logo.png:/app/branding/logo.png:ro
```

**Atribución fija:** todo informe lleva en el pie de cada página el Sello de
Factum y la leyenda **"Realizado con Factum"**. La inyecta el código (no la
plantilla), así que ninguna plantilla la puede sacar.

### Placeholders de la plantilla del informe

La plantilla (`server/src/Factum.Backend/Templates/plantilla_informe_v4.docx`)
no se edita a mano: la genera `ops/plantilla/build_plantilla_v4.py` (ver
[Informe pericial: configuración](#informe-pericial-configuración)). Se
completa en el cuerpo y en los encabezados/pies:

| Placeholder | Reemplazo |
|---|---|
| `{nombreTribunal}`, `{organismoTribunal}`, `{tipoCausa}`, `{numeroCausa}`, `{caratula}`, `{parteDenunciante}`, `{parteDenunciada}`, `{objetoCausa}`, `{ambitoCausa}`, `{fechaIntervencion}` | Datos de la causa (los opcionales vacíos salen como "No informado") |
| `{tramiteAnte}`, `{fraseIntegracion}`, `{fraseDomicilio}`, `{datosProponente}`, `{elSuscripto}` | Frases armadas en código, para que un dato opcional vacío no deje una frase rota |
| `{nombrePerito}`, `{matriculaPerito}`, `{profesionPerito}`, `{caracterPerito}` | La copia del perfil del perito guardada en el caso |
| `{fechaInspeccion}`, `{horaInspeccion}` | Creación del caso, en la zona `Report:TimeZone` |
| `{tipoDispositivo}`, `{marcaModeloDispositivo}`, `{imeiDispositivo}`, `{lineaDispositivo}`, `{titularDispositivo}` | Datos del equipo |
| `{objetoInforme}`, `{descripcion…}` | Textos del paso Informe, un párrafo por línea (párrafo completo) |
| `{nombreArchivo}` / `{hashArchivo}` | Fila modelo de la tabla de hashes: una fila por archivo más la del ZIP |
| `{capturasImeiModelo}`, `{capturasNombreDispositivo}`, `{anexoCapturas}` | Capturas marcadas por rol y anexo con las capturas sin marca (párrafo completo) |
| `{#clave}` … `{/clave}` | Bloque condicional (párrafos propios): desaparece si el dato está vacío |
| `{ORGANIZACION}` | `Branding:OrganizationName` (vacío si no hay). |
| `{CONTACTO}` | `Branding:ContactLines`, una por línea (mismo formato del run) |
| `{CONTACTO_EN_LINEA}` | `Branding:ContactLines` unidas con " · " |
| `{LOGO_ORGANIZACION}` / `{LOGO_ORGANIZACION:4x1.2}` | Logo de la organización (párrafo completo), ajustado sin recortar a una caja de 5 × 1.5 cm o del tamaño indicado en cm. Sin logo, el párrafo queda vacío. |

Un placeholder que la plantilla traiga y Factum no conozca se borra y se
loguea un warning (`Plantilla: placeholder desconocido {x}`).

## Informe pericial: configuración

El informe es el **Informe Pericial Técnico Informático** del perito de parte.
Los datos del perito salen del perfil de cada usuario ("Mi perfil de perito",
colección `expert_profiles`) y se copian al caso al crearlo o editarlo. Lo que
es propio del estudio va en la sección `Report` del backend:

| Clave | Qué es |
|---|---|
| `Report:TimeZone` | Zona IANA de la fecha y hora de la inspección (default `America/Argentina/Buenos_Aires`). Si no existe en el sistema, se loguea un warning y se usa UTC-03:00 fijo. |
| `Report:DomicilioConstituido` | Domicilio constituido del perito, para la presentación ("…, con domicilio constituido en …"). Máx. 300 caracteres. Vacío = la frase se omite. |
| `Report:DefaultTexts:OperacionesRealizadas` / `AseguramientoEvidencia` / `NotasTecnicas` / `Reserva` | Textos por defecto propios del estudio para el paso Informe. Vacío = el texto neutro versionado en `Services/Reports/ReportDefaultTexts.cs`. Admiten los tokens `{fechaInspeccion}`, `{horaInspeccion}`, `{tipoDispositivo}`, `{marcaModeloDispositivo}`, `{imeiDispositivo}`, `{sistemaOperativo}`, `{zonaHoraria}`, `{elSuscripto}` y `{cantidadCapturas}`/`{cantidadGrabaciones}`/`{cantidadArchivosExtraidos}`/`{cantidadOtros}`. |

Como con `Branding`, **el domicilio real no va al repo**: va en
`appsettings.Local.json` (ignorado por git) o en `Report__DomicilioConstituido`.
La config se lee una sola vez al arrancar. Ejemplo (valores ficticios):

```json
{
  "Report": {
    "TimeZone": "America/Argentina/Buenos_Aires",
    "DomicilioConstituido": "Calle Ejemplo 123, Ciudad"
  }
}
```

**Hashes:** el ZIP de evidencia se cierra antes de generar el informe y no se
vuelve a abrir, así que el hash del ZIP que figura en la tabla del informe es
el del ZIP que se descarga. El DOCX va aparte (no dentro del ZIP) y su SHA-256
se guarda en el caso (`report_hash`); el informe no puede contener su propio
hash.

**Regenerar la plantilla v4** (por ejemplo, si cambia la plantilla de origen):
ver `ops/plantilla/README.md`.

## Integración de soporte (Faro)

Es **opcional y está apagada por defecto**. Apagada, el backend no llama a Faro,
`GET /api/config/public` devuelve `support_enabled: false`, el dashboard no
muestra el soporte y `/api/support/*` responde `404`.

Encendida, Factum y Faro comparten la misma identidad de usuario (DNI). Desde el
dashboard de Factum, un usuario puede:

1. **Reportar un problema** (botón del ícono del faro) — se crea un token de
   soporte real en Faro, con el DNI, nombre, número interno y teléfono del
   oficial.
2. **Ver el estado de sus reportes y calificarlos** sin salir de Factum.
3. **Abrir Faro ya logueado** ("Ver todo en el portal de soporte") — usa un código de
   intercambio de un solo uso (SSO) generado por Faro, así el oficial no
   vuelve a poner su contraseña ahí.

Para activarla en local, con el backend de Faro corriendo (ver su README),
agregá en `server/src/Factum.Backend/appsettings.Local.json` (ignorado por git):

```json
{
  "Integrations": {
    "Support": {
      "Enabled": true,
      "BaseUrl": "http://localhost:5038",
      "ServiceKey": "<clave-compartida-con-faro>",
      "FrontendUrl": "http://localhost:3001"
    }
  }
}
```

`ServiceKey` tiene que ser idéntica al `Integrations:ServiceKey` configurado en
Faro. En Docker/producción se usan las variables de entorno
`Integrations__Support__Enabled`, `Integrations__Support__BaseUrl`,
`Integrations__Support__ServiceKey` y `Integrations__Support__FrontendUrl`. Con
`Enabled=true`, si falta `BaseUrl`, `ServiceKey` o `FrontendUrl` el backend no
arranca y dice qué clave falta.

Una sección vieja `FaroIntegration` **no activa** el soporte: el backend arranca
con el soporte apagado y avisa en el log. Con `Enabled=true`, las claves que
falten en `Integrations:Support` se toman de `FaroIntegration` (también con un
warning); conviene renombrarlas.

## Estructura del proyecto

```
factum/
├── client/                        # Frontend Next.js
│   └── src/
│       ├── app/                   # Rutas (login, dashboard)
│       ├── components/            # Wizard, dashboard, soporte, UI
│       └── lib/                   # Cliente de API, tipos, agente
├── server/src/
│   ├── Factum.Backend/         # API principal (.NET)
│   │   ├── Controllers/
│   │   ├── Services/
│   │   │   ├── Auth/              # Proveedores de identidad (Dev/External)
│   │   │   ├── Branding/          # Identidad de la organización (nombre, logo, contacto)
│   │   │   ├── Reports/           # Informe DOCX + ZIP
│   │   │   └── Support/           # Integración de soporte opcional (Faro)
│   │   └── Models/
│   └── Factum.Agent/           # Agente local "Tatana" (.NET)
├── agent-ui/                      # UI de escritorio del agente (Electron)
├── docker-compose.yml             # mongo + backend + frontend
└── AGENTE_TATANA.md               # Casos de uso del agente
```

## Despliegue del agente en la PC del fiscal

Hay dos formas de correr Tatana, pensadas para distintos niveles de permisos
en la PC del fiscal — ambas hablan con el mismo `client` vía `localhost:8765`
y ambas se actualizan solas sin que el fiscal tenga que hacer nada:

- **Instalado** (`agent-ui`, Electron + NSIS): requiere poder correr un
  instalador. Se actualiza con `electron-updater` contra
  `/tatana/updates/` del backend (nunca contra GitLab directo).
- **Portátil** (`packaging/portable/`): sin instalador y sin permisos de
  administrador. Trae adb (platform-tools) y un Python embebido con
  `pymobiledevice3` ya copiados adentro — `AdbService`/`IosService` los
  resuelven por ruta relativa al ejecutable antes que por `PATH` del sistema.
  `install-portable.bat` copia todo a `%LOCALAPPDATA%\Programs\Tatana` y arma
  el autostart con un acceso directo en el `Startup` del usuario (sin admin).
  Se actualiza sola vía `update-portable.ps1` contra el mismo backend.

CI (`.gitlab-ci.yml`) arma ambos artifacts por tag (`v*`) y los publica como
GitLab Release. El backend expone `/tatana/updates/` (`TatanaUpdatesService`)
para espejar esa release sin que la PC del fiscal necesite salida directa a
internet.

**Auditoría de uso**: cada acción de captura (screenshot, grabación, webcam) y
el arranque del wizard reportan al backend quién (DNI), desde qué PC
(hostname, IP, modo instalado/portátil) y para qué caso —
`POST /api/agent-events`, colección Mongo `agent_events`. La lectura
(`GET /api/agent-events`) está limitada a los DNIs en `Audit:AdminDnis`
(`appsettings.json`) — no hay roles en el modelo de usuario todavía.

## Notas

- El agente (`Factum.Agent`) y su UI (`agent-ui`) están pensados para
  correr en la PC del oficial, con acceso físico al USB — por diseño no
  forman parte de `docker-compose.yml`.
- La evidencia capturada (fotos/video) no se sube a ningún servidor externo
  fuera de este sistema; el ZIP final queda cifrado con contraseña.
