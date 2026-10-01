# Factum

Sistema de adquisición forense de evidencia digital para dispositivos móviles.
Permite a un perito u operador crear un expediente, conectar un celular
(Android o iOS) por USB, capturar evidencia (fotos, video de pantalla,
capturas) y generar un informe forense (DOCX) + un paquete ZIP cifrado con
hash, todo sin que la evidencia original salga del dispositivo del usuario
hacia un servidor de terceros. El informe sale con la identidad de la
organización que lo emite (ver
[Identidad de la organización (Branding)](#identidad-de-la-organización-branding)).

También integra con **[Faro](https://gitlab.com/joelserrudo/faro-sistema-de-tokens)**,
la mesa de ayuda (sistema de tokens): un oficial puede reportar un problema
técnico de Factum y hacerle seguimiento sin salir de la app, y comparte la
misma identidad de usuario (DNI) que Faro — ver [Integración con Faro](#integración-con-faro).

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
│  server/.../Agent │  (proceso local) │  Faro (sistema de     │
│  "Tatana"          │ ◀── ADB/USB ──  │  gestión de tokens)   │
│  habla con el      │     dispositivo │  ver repo hermano     │
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
  historial de casos, y el modal de soporte (Faro).
- **`server/src/Factum.Backend`** — API en ASP.NET Core (.NET 10) +
  MongoDB. Autenticación JWT, gestión de casos/expedientes, generación de
  informes (DOCX + ZIP cifrado), y el cliente HTTP de Faro (mesa de ayuda).
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
| Auth | JWT (access token corto) — modos `dev` (mock) y `mpf` (proveedor HTTP externo) |

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
| `Auth:Mode` | `dev` (identidad simulada) o `mpf` (login real contra un proveedor HTTP externo) |
| `Auth:MpfBaseUrl` / `MpfLoginPath` / `MpfTimeoutSeconds` | Solo si `Auth:Mode=mpf` |
| `FaroIntegration:BaseUrl` | URL del backend de Faro (`http://localhost:5038` en local) |
| `FaroIntegration:ServiceKey` | Clave compartida servicio-a-servicio con Faro (debe coincidir con `Integrations:ServiceKey` de Faro) |
| `FaroIntegration:FrontendUrl` | URL del frontend de Faro, para armar el link de SSO (`http://localhost:3001` en local) |
| `Storage:DataDirectory` | Carpeta local donde se guardan los ZIP/PDF generados (dev) |
| `Audit:AdminDnis` | DNIs habilitados a leer `GET /api/agent-events` (auditoría de uso del agente) |
| `TatanaUpdates:ProjectId` / `ProjectRawBaseUrl` / `PrivateToken` | Proyecto de GitLab del que se espeja la última release de Tatana |
| `TatanaUpdates:PublicBaseUrl` | URL pública de este backend — a la que apuntan el instalador Electron y el `.bat` portátil para actualizarse |
| `Branding:OrganizationName` / `OrganizationLogo` / `ContactLines` | Identidad de la organización que emite los informes — ver [Branding](#identidad-de-la-organización-branding). Vacío en el repo |

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

> Los secretos que están commiteados (`Jwt:Secret`, `FaroIntegration:ServiceKey`)
> son valores de desarrollo, pensados para correr todo en local. Rotalos antes
> de cualquier despliegue real.

## Autenticación

El login pide **DNI + usuario + contraseña** — es la misma identidad (DNI)
que usa Faro, no un usuario propio de Factum.

- **Modo `dev`** (default): cualquier DNI de 7-8 dígitos y cualquier
  contraseña no vacía autentican. El nombre se arma a partir del usuario con
  la convención `nombre.apellido` (ej: usuario `carlos.mendoza` → "Carlos
  Mendoza"). No hace falta pre-registrar a nadie: el usuario se crea la
  primera vez que loguea.
- **Modo `mpf`**: valida contra un proveedor HTTP externo
  (`Auth:MpfBaseUrl` + `Auth:MpfLoginPath`).

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

La plantilla (`server/src/Factum.Backend/Templates/plantilla_informe_v3.docx`)
se completa reemplazando estos textos, en el cuerpo y en los
encabezados/pies (incluidas las cajas de texto):

| Placeholder | Reemplazo |
|---|---|
| `{NROREF}`, `{FECHA_HORA}`, `{fHora}`, `{DEPENDENCIA}` | Referencia del caso, fecha y hora (UTC), sigla del operador |
| `{NOMBRE_DENUNCIANTE}`, `{DNI_DENUNCIANTE}`, `{NOMBRE_FUNCIONARIO}`, `{DNI_LEGAJO}` | Datos de las personas del caso |
| `{MARCA}`, `{MODELO}`, `{IMEI}`, `{SO}`, `{NRO_SERIE}` | Datos del dispositivo |
| `{OBSERVACIONES}`, `{ARCHIVOS}`, `{ARCHIVO_GENERADO}`, `{HASH}` / `{HASH_ZIP_COMPLETO}`, `{CLAVE}` | Observaciones, lista de archivos con su SHA-256, nombre, hash y contraseña del ZIP |
| `{FOTO_FUNCIONARIO}`, `{FOTO_DENUNCIANTE}`, `{CAPTURAS}` | Imágenes (párrafo completo) |
| `{ORGANIZACION}` | `Branding:OrganizationName` (vacío si no hay). Ponelo en su propio run, sin separadores pegados. |
| `{CONTACTO}` | `Branding:ContactLines`, una por línea (mismo formato del run) |
| `{CONTACTO_EN_LINEA}` | `Branding:ContactLines` unidas con " · " |
| `{LOGO_ORGANIZACION}` / `{LOGO_ORGANIZACION:4x1.2}` | Logo de la organización (párrafo completo), ajustado sin recortar a una caja de 5 × 1.5 cm o del tamaño indicado en cm. Sin logo, el párrafo queda vacío. |

## Integración con Faro

Factum y Faro comparten la misma identidad de usuario (DNI). Desde el
dashboard de Factum, un oficial puede:

1. **Reportar un problema** (botón del ícono del faro) — se crea un token de
   soporte real en Faro, con el DNI, nombre, número interno y teléfono del
   oficial.
2. **Ver el estado de sus reportes y calificarlos** sin salir de Factum.
3. **Abrir Faro ya logueado** ("Ver todo en Faro") — usa un código de
   intercambio de un solo uso (SSO) generado por Faro, así el oficial no
   vuelve a poner su contraseña ahí.

Para que esto funcione en local, el backend de Faro tiene que estar corriendo
y `FaroIntegration:ServiceKey` tiene que ser idéntico al `Integrations:ServiceKey`
configurado en Faro. Ver el README de Faro para levantarlo.

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
│   │   │   ├── Auth/              # Proveedores de identidad (Dev/Mpf)
│   │   │   ├── Branding/          # Identidad de la organización (nombre, logo, contacto)
│   │   │   ├── Reports/           # Informe DOCX + ZIP
│   │   │   └── Support/           # Integración con Faro
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
