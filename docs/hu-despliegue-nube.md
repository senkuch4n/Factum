# HU: Factum en la nube, gratis, como SaaS (frontend + backend + MongoDB), con Tatana local

**Slug:** `despliegue-nube` · **Issue:** #14
**Toca (preliminar, lo confirma la SDD):** infraestructura nueva en el repo (compose de producción, proxy HTTPS,
scripts de backup/actualización, guía `docs/despliegue-nube.md`, workflow opcional), ajustes chicos en
`server/src/Factum.Backend` (health/config por entorno, quizá almacenamiento del DOCX según D3) y en el empaquetado de
Tatana (`deploy/windows/armar-tatana-portable.sh`, `packaging/portable/`, `agent-ui/package.json` → `publish.url`)
para que cada PC acepte el origen de la nube. `client/` probablemente sin cambios de código (solo build args), salvo
que D4 elija export estático. `agent-ui/` sin cambios de UI.

**Como** dueño de Factum (con mi socio Leo) que lo ofrece como SaaS a estudios jurídicos, policías y peritos
**quiero** tener el frontend, el backend y la base de datos publicados en internet con HTTPS, en planes gratuitos, y
que cada perito siga usando Tatana instalado en su PC
**para que** los clientes entren desde cualquier navegador con su cuenta, sin instalar Docker ni un servidor en su
estudio, y sin que la evidencia salga de la PC del perito.

---

## Contexto

### Qué existe hoy (arqueología sobre `develop` = `645c44c`, 2026-10-06)

**Imágenes y compose**
| # | Dónde | Qué hay |
|---|---|---|
| E1 | `client/Dockerfile` | Next 16 `output: "standalone"`, Node 22 alpine, puerto 3000, usuario `node`. `NEXT_PUBLIC_BACKEND_URL` y `NEXT_PUBLIC_AGENT_URL` son **build args** (se hornean en el bundle; defaults `http://localhost:8080` / `:8765`). |
| E2 | `client/` en general | **App 100 % del lado del navegador**: no hay `route.ts`, ni `middleware`/`proxy`, ni `"use server"`, ni `next/headers`, ni `next/image`, ni rutas dinámicas (páginas: `/`, `/dashboard`, `/cambiar-contrasena`, `/admin/cuentas`, `/design-system`). Técnicamente admite `output: "export"` (HTML estático) — relevante para D4. |
| E3 | `server/src/Factum.Backend/Dockerfile` | .NET 10, build multi-arquitectura (`--platform=$BUILDPLATFORM` + `TARGETARCH`), así que ya compila para `arm64`. Instala `tzdata` y `curl`. Puerto por env `PORT` (default 8080). |
| E4 | `docker-compose.yml` (raíz) | **De desarrollo**: Mongo 7 con puerto 27017 publicado y sin auth, backend con `Auth__Mode=dev` + `AllowDevOutsideDevelopment=true` y `Jwt__Secret` del repo, frontend con URLs `localhost`. Volúmenes `mongo-data` y `backend-data` (`/data`). No sirve tal cual para internet. |
| E5 | `deploy/windows/` | Instalación local en la PC de un estudio (compose propio, `.env`, `appsettings.Local.json` montado, perfil "poca RAM" con topes 512/768/384 MB, scripts de backup/restauración/diagnóstico en PowerShell). **Tiene que seguir funcionando** (ver D15). |
| E6 | `.gitlab-ci.yml` | CI de **GitLab** (el repo hoy vive en GitHub): solo arma Tatana (instalador NSIS, portátil, Linux) por tag `v*`. No hay `.github/workflows/`. |

**Backend: lo que ya está listo para la nube**
| # | Dónde | Qué hay |
|---|---|---|
| B1 | `Program.cs` L50-67 | Fail-fast de configuración: con `Auth:Mode=local` fuera de Development exige `Jwt:Secret` propio (≥ 32, distinto del del repo) y al menos un superadmin; `dev` fuera de Development no arranca salvo `Auth:AllowDevOutsideDevelopment=true` (#6). |
| B2 | `Infrastructure/CorsOrigins.cs` | `Cors:AllowedOrigins` (lista, sin `*`, validada al arrancar; default `http://localhost:3000`). Sin credenciales: el JWT va en `Authorization`. |
| B3 | `Program.cs` L110-115, L252 | `UseForwardedHeaders` con `X-Forwarded-For`/`Proto` y `KnownProxies` vacíos: preparado para correr detrás de un reverse proxy. No hay `UseHttpsRedirection` ni HSTS (los pondría el proxy). |
| B4 | `Program.cs` L265 | `GET /health` → `{ status, version: "2.0.0", auth_mode }`. **No chequea Mongo** (liveness pura). |
| B5 | `Infrastructure/StorageService.cs` + `CaseService.DownloadAsync` | El DOCX se escribe y se sirve desde **disco**: `<Storage:DataDirectory>/cases/<id>/informe_pericial_<ref>.docx`. Además usa `.upload-tmp/` y `.generate-tmp/` como temporales. El caso en Mongo guarda `pdf_filename` (apunta al DOCX) y `report_hash`. |
| B6 | `ReportOptions.MaxGenerateUploadBytes` | `POST /api/cases/{id}/generate/finish` acepta hasta **256 MiB** (capturas que embebe el informe). Relevante si hay un proxy con tope de request (D5). |
| B7 | `CaseService` L395 | Un caso con `evidence_storage = "agent"` (todos los casos nuevos) **rechaza** subir evidencia al servidor. Una base nueva en la nube no tiene casos "server", así que el flujo viejo de `POST /files` (hasta 4 GiB) no se usa. |
| B8 | marca-por-cliente (#13) | Logos e isotipos de cada cuenta en Mongo (`account_brandings`, tope 1 MiB por imagen). La marca **global** de la instalación (`Branding:*`, la del login) sigue siendo config con **ruta a archivo**. |
| B9 | `TatanaUpdates:*` | Espejo de releases de **GitLab** para auto-actualizar Tatana (`/tatana/updates/`). Vacío en el repo. |
| B10 | `client/src/lib/api.ts` L1064 | Una descarga manda el JWT por **query string** (`?token=`): quedaría en cualquier log de acceso del proxy (D12). |

**Tatana (sigue local) y su relación con la web en la nube**
| # | Dónde | Qué hay |
|---|---|---|
| T1 | `Factum.Agent/Common/OriginPolicy.cs`, `Program.cs` L76-176 | `Agent:AllowedOrigins` (default **solo** `http://localhost:3000` y `http://127.0.0.1:3000`). Cualquier `Origin` fuera de la lista → **403 `origin_not_allowed`**, incluido el WebSocket. Responde el preflight de Private Network Access. ⇒ **Ninguna PC va a funcionar con la web en la nube hasta que su Tatana tenga el origen del dominio real.** |
| T2 | `client/src/lib/agent.ts` L225-250, `agent-messages.ts` | La web ya detecta el permiso de **Local Network Access** de Chrome/Edge y traduce `origin_not_allowed`. Nunca se probó contra un origen HTTPS público real. |
| T3 | `agent-ui/package.json` `publish.url` y `.gitlab-ci.yml` | El instalador Electron hornea la URL de actualización (`http://localhost:8080/tatana/updates/` por defecto; en CI, `TATANA_UPDATES_URL`). |
| T4 | `packaging/portable/tatana-portable.ini` y `armar-tatana-portable.sh` L172 | El portátil tiene `CLIENT_URL` (qué abre al iniciar sesión) y `UPDATE_URL`; el paquete local los deja vacíos. No configura `Agent:AllowedOrigins`. |

### Qué decidieron las HU anteriores
- #9 `zip-local-informe-servidor`: la evidencia y el ZIP quedan en la PC; **el servidor guarda solo el DOCX** (+ metadatos
  y hashes en Mongo). Su D11 dejó explícitamente para esta HU **dónde vive el DOCX si el disco del plan gratis es
  efímero**.
- #6 `usuarios-locales`: SaaS = `Auth:Mode=local`, superadmins iniciales por config (el usuario y Leo), contraseña
  temporal que se cambia en el primer ingreso, sin email.
- #12 `abm-clientes` y #13 `marca-por-cliente`: panel de cuentas y marca por cuenta, todo en Mongo.

### Por qué hace falta
Todo lo anterior preparó el código para la nube, pero no existe ningún artefacto para **publicarlo**: no hay compose
de producción, ni HTTPS, ni gestión de secretos, ni backups fuera de Windows, ni forma de que las PCs con Tatana
acepten el dominio nuevo. Sin esta HU, Factum solo corre en `localhost`.

### Qué es nuevo
1. Una **topología de producción gratuita** elegida (D1-D4) con su infraestructura versionada en el repo (sin secretos).
2. **HTTPS** con un dominio definitivo (D5) y headers de seguridad en el borde (D13).
3. **Persistencia y backups** de Mongo y del DOCX que sobrevivan reinicios y la pérdida de la VM/servicio (D3, D11).
4. **Configuración de producción** por variables de entorno, fuera del repo (D7).
5. **Tatana configurado para el dominio real** en cada PC (D8) y una prueba de punta a punta HTTPS → `localhost` (D10).
6. **Guía operativa** `docs/despliegue-nube.md`: qué hace el usuario a mano, primer despliegue, actualización,
   restauración, rotación de secretos y plan de salida a un plan pago (D14).

---

## Criterios de aceptación

```gherkin
Feature: Factum desplegado en la nube con Tatana local

  Background:
    Given la topología elegida en D1-D4 creada siguiendo docs/despliegue-nube.md
    And el dominio definitivo de D5 apuntando a ella

  Scenario: Acceso por HTTPS
    When un usuario abre https://<dominio-del-front>
    Then ve el login de Factum con un certificado válido emitido automáticamente
    And http:// redirige a https://
    And las respuestas llevan los headers de seguridad acordados en D13

  Scenario: Superficie expuesta mínima
    When se escanean los puertos públicos del host o servicio
    Then solo responden 80 y 443 (o el equivalente del proveedor)
    And MongoDB y el puerto interno del backend (8080) no son alcanzables desde internet
    And Swagger no está publicado

  Scenario: Configuración de producción segura
    Given el backend arrancando en producción
    Then corre con Auth:Mode=local y Auth:AllowDevOutsideDevelopment=false
    And usa un Jwt:Secret propio generado para este despliegue
    And Cors:AllowedOrigins contiene solo el origen real del frontend
    And ningún secreto (Jwt:Secret, conexión a Mongo, contraseñas temporales, credenciales de backup) está en el repo ni en una imagen publicada
    And si falta un secreto obligatorio el backend no arranca y lo dice en el log (fail-fast existente)

  Scenario: Primer ingreso de los superadmins
    Given la base de producción vacía
    When el backend arranca por primera vez con los superadmins iniciales configurados (el usuario y Leo)
    Then cada uno entra con su DNI y su contraseña temporal
    And se le exige cambiarla antes de usar Factum
    And desde el panel de cuentas puede dar de alta clientes

  Scenario: Flujo completo de un perito desde la nube
    Given un cliente con cuenta, en una PC Windows con Tatana instalado y configurado para el dominio real (D8)
    And Chrome o Edge actualizados (D10)
    When entra a https://<dominio-del-front>, crea un caso, captura evidencia con Tatana y genera el informe
    Then el navegador habla con http://localhost:8765 (HTTP y WebSocket) sin errores de mixed content ni de origen
    And si el navegador pide el permiso de acceso a la red local, la web explica qué aceptar
    And el ZIP queda en la PC y el DOCX queda guardado en el almacenamiento elegido en D3
    And el DOCX se puede descargar desde otra PC con la misma cuenta

  Scenario: Tatana sin el origen de la nube
    Given una PC con Tatana que no tiene el dominio real en Agent:AllowedOrigins
    When el perito usa la web en la nube
    Then ve el mensaje existente de "origen no permitido" con la indicación de cómo actualizar Tatana (D8)

  Scenario: Persistencia ante reinicios
    Given casos, cuentas, marcas e informes DOCX cargados
    When se reinicia el contenedor, el servicio o la VM (y cuando el plan apaga el servicio por inactividad, si aplica)
    Then todos siguen disponibles y los DOCX se descargan con el mismo SHA-256 que report_hash

  Scenario: Backup y restauración
    Given el backup automático configurado según D11
    When pasa el período acordado
    Then existe una copia de Mongo y de los DOCX fuera del host de producción
    And siguiendo docs/despliegue-nube.md se restaura en un host nuevo y vacío
    And tras restaurar, los DOCX conservan su SHA-256 y los usuarios entran con sus contraseñas actuales

  Scenario: Actualizar la versión publicada
    Given un cambio mergeado a main
    When se ejecuta el procedimiento de actualización de D9 (manual o automático)
    Then la nueva versión queda publicada sin perder datos
    And si la nueva versión no arranca (fail-fast), la anterior se puede restituir siguiendo la guía

  Scenario: Monitoreo
    Given el monitoreo de D12
    When el backend o Mongo dejan de responder
    Then el usuario recibe un aviso (mail o similar) sin tener que entrar a mirar
    And los logs de los contenedores tienen rotación y no llenan el disco
    And ningún log guarda tokens JWT ni contraseñas

  Scenario: La instalación local de Windows no se rompe
    Given el paquete deploy/windows existente
    When se arma con los cambios de esta HU
    Then se instala y funciona igual que antes (localhost, sin HTTPS)
```

---

## Datos y configuración que se registran

No hay datos de negocio nuevos. Esto es **configuración de producción**; ningún valor real va al repo (en el repo solo
un `.env.example` con nombres y placeholders).

| Dato | Dónde vive | Obligatorio | Uso |
|---|---|---|---|
| `Jwt__Secret` (≥ 32 caracteres, generado con `openssl rand`) | Variables de entorno del host/servicio (D7) | Sí | Firma de sesiones. Rotarlo cierra todas las sesiones. |
| `MongoDb__ConnectionString` / `DatabaseName` | Ídem | Sí | Mongo de la VM (con usuario y contraseña) o Atlas (`mongodb+srv://…`), según D2. |
| `Auth__Mode=local`, `Auth__AllowDevOutsideDevelopment=false`, `ASPNETCORE_ENVIRONMENT=Production` | Compose de producción (no son secretos) | Sí | B1. |
| `Auth__Local__BootstrapSuperadmins__{0,1}__Dni` / `Name` / `TemporaryPassword` | Variables de entorno | Sí (primer arranque) | El usuario y Leo. Se crean solo si no existen; después de su primer ingreso se pueden quitar del entorno. |
| `Cors__AllowedOrigins__0` | Compose/entorno | Sí | `https://<dominio-del-front>`. |
| `Agent:AllowedOrigins` de cada Tatana | `appsettings.json` del paquete de Tatana o entorno de la PC (D8) | Sí | `https://<dominio-del-front>` (+ los `localhost` de siempre). |
| `NEXT_PUBLIC_BACKEND_URL` / `NEXT_PUBLIC_AGENT_URL` | Build args del frontend | Sí | `https://<dominio-de-la-api>` y `http://localhost:8765`. Cambiarlos implica volver a compilar el front. |
| `Storage__DataDirectory` (+ volumen persistente) o el almacenamiento de D3 | Compose | Sí | DOCX generados. |
| `Branding__*` global (nombre/logo de Factum en el login) | Entorno + archivo montado | No | Identidad del producto en la pantalla de login. |
| `TatanaUpdates__PublicBaseUrl` y demás | Entorno | No (D8b) | Auto-actualización de Tatana. |
| Credenciales del destino de backup | Solo en el host, archivo con permisos 600 | Sí si hay backup remoto (D11) | Subir los dumps fuera del host. |
| Correo de alertas del monitoreo | Cuenta del servicio de monitoreo | Sí | D12. |

---

## UX / operación

No hay pantallas nuevas en `client/` ni en `agent-ui/`. La "UX" de esta HU es la del **operador** (el usuario y Leo) y
la del **perito** la primera vez que usa la web en la nube.

**Perito (`client/`, web).**
- Entra por el dominio definitivo, con candado válido. La primera vez que la web llama a Tatana, Chrome/Edge pueden
  pedir permiso de "acceso a otros dispositivos/apps de la red local": la web ya tiene la detección (T2); hay que
  confirmar que el texto que muestra le dice qué aceptar y cómo revertir un "Bloquear".
- Si su Tatana es viejo (sin el origen nuevo), ve "origen no permitido" con instrucción concreta: "Actualizá Tatana
  desde <link de descarga>" (D8).
- Navegadores soportados documentados (D10).

**Operador (guía `docs/despliegue-nube.md`).** La guía separa con claridad:

| Lo hace el usuario a mano (fuera del repo) | Va en el repo |
|---|---|
| Crear las cuentas del proveedor (D1), de Mongo si es Atlas (D2), del dominio/DNS (D5), del monitoreo (D12) y del destino de backup (D11). Algunos proveedores piden tarjeta para verificar identidad aunque el plan sea gratis. | Compose de producción (`deploy/nube/docker-compose.yml` o similar) sin puertos internos publicados, con log rotation y healthchecks. |
| Crear la VM/servicio, abrir 80/443 (en la consola del proveedor **y** en el firewall del sistema operativo si corresponde). | Config del proxy HTTPS (p. ej. `deploy/nube/Caddyfile`) con redirección, HSTS y headers. |
| Registros DNS (A/AAAA o CNAME). | `deploy/nube/.env.example` con todas las claves y placeholders, nunca valores. |
| Generar los secretos y escribir el `.env` real en el host. | Scripts de primer despliegue, actualización, backup y restauración (bash, el host es Linux). |
| Elegir DNI, nombre y contraseña temporal de los superadmins. | Guía `docs/despliegue-nube.md` paso a paso, con capturas de lo que hay que tocar en cada consola. |
| Distribuir el Tatana configurado a las PCs de los clientes (D8). | Cambios de empaquetado de Tatana para hornear el origen de la nube (D8). |
| Probar la restauración una vez (simulacro). | Workflow de GitHub Actions si D9 lo pide; ajustes de backend que pidan D3/D12. |

**Estados de error del operador que la guía tiene que cubrir:** el backend no arranca por config inválida (cómo leer
el log), el certificado no se emite (DNS mal, puertos cerrados), disco lleno, Mongo no responde, el proveedor
apagó/reclamó la VM (D14), y cómo rotar `Jwt:Secret` o la contraseña de Mongo.

---

## Fuera de alcance

- Cambiar dónde vive la evidencia o el ZIP (resuelto en #9: siempre en la PC).
- Cambios funcionales de la web, del informe, de cuentas o de marca.
- Emparejamiento/autenticación entre la web y Tatana (sigue solo CORS + guarda de origen).
- Pasarela de pagos, planes, facturación, email transaccional o recuperación de contraseña por mail.
- Multi-región, alta disponibilidad, réplicas de Mongo, autoescalado.
- Migrar datos de las instalaciones locales existentes a la nube (la nube arranca con base vacía; si hace falta, HU
  aparte).
- Reescribir el CI de GitLab de Tatana o firmar el instalador; solo lo mínimo para que el paquete traiga el origen
  nuevo (D8).
- Auto-actualización de Tatana desde la nube, salvo que D8b la incluya.
- Ocultar `/design-system` en producción (anotarlo como tarea chica si molesta).
- Hacer per-cuenta los parámetros globales que quedan (`Report:DomicilioConstituido`, `Branding` del login).
- Asesoramiento legal sobre protección de datos (D6 solo deja la decisión de región registrada).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- **Nada de precios ni límites inventados.** Todo límite de plan gratuito citado acá lleva "(a confirmar)": el
  architect los verifica en la documentación oficial vigente y los anota con fecha en la guía.
- Docker publica puertos saltándose firewalls como UFW: el compose de producción **no** debe publicar 27017 ni 8080;
  solo el proxy expone 80/443.
- Si el host es ARM (Oracle Ampere A1), las imágenes tienen que ser `arm64`: el Dockerfile del backend ya lo soporta;
  Node alpine y `mongo:7` tienen variante arm64 (a confirmar los requisitos de CPU de Mongo 7 en Ampere).
- `NEXT_PUBLIC_*` se hornean en el build: el front se compila con las URLs de producción (en el host o en CI).
- El proxy no debería registrar query strings (B10, `?token=`), o la SDD propone sacar el token de la URL.
- La regla dura de datos de `AGENTS.md` aplica a la Mongo de desarrollo: ninguna prueba de esta HU toca la base de
  desarrollo ni `dev-data`; las pruebas de despliegue se hacen contra la base de producción vacía o una de ensayo.
- Si D3 = GridFS: hay que contemplar los DOCX de instalaciones locales existentes que siguen en disco (lectura desde
  disco como respaldo, o migración explícita), sin borrar nada.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué. Lo marcado "(a confirmar)" es un límite o condición de un
> proveedor que el architect tiene que verificar en la fuente oficial antes de cerrar la SDD.

**D1. Proveedor y topología.**
- A) **Todo en una VM Oracle Cloud Always Free (Ampere A1, ARM)**: proxy HTTPS (Caddy) + frontend + backend + Mongo en
  un compose de producción, con disco persistente. Gratis (a confirmar: hasta 4 OCPU / 24 GB RAM / 200 GB de
  disco en bloque en total).
- B) Repartido en PaaS gratuitos: frontend en Cloudflare Pages/Workers, backend en Cloud Run / Render / Koyeb / Azure
  F1, Mongo en Atlas M0.
- C) Híbrido: frontend en Cloudflare Pages, backend + Mongo en la VM Oracle.
- **Recomendada: A.** Ahora que el servidor solo guarda DOCX y metadatos, el disco necesario es chico, pero los PaaS
  gratis tienen disco efímero, se duermen sin tráfico (cold starts de decenas de segundos en algunos, a confirmar) y
  con varios servicios cada uno con su límite. A corre el mismo compose que ya conocen, no obliga a cambiar código
  de almacenamiento, no tiene cold starts y es portable: si mañana hay que pagar, el mismo compose va a cualquier VPS.
  **Riesgos de A a aceptar explícitamente:** Oracle pide tarjeta para verificar la cuenta; la capacidad A1 suele
  faltar en regiones populares (a veces hay que reintentar la creación); Oracle puede **reclamar instancias Always
  Free ociosas** (a confirmar política vigente y si pasar la cuenta a "Pay As You Go" sin consumo lo evita); es un
  solo host sin SLA. Por eso D11 (backup fuera del host) es parte obligatoria de A.

**D2. Dónde vive MongoDB.**
- A) **En la misma VM, en un contenedor `mongo:7` con usuario/contraseña, sin puerto publicado.**
- B) Atlas M0 (gratis, gestionado).
- **Recomendada: A si D1 = A.** Sin el límite de 512 MB de M0 (a confirmar), sin latencia de red y sin abrir Atlas a
  `0.0.0.0/0`. Lo que se pierde (gestión) se cubre con D11. Hay que tener en cuenta que M0 no incluye backups
  automáticos (a confirmar), así que tampoco ahorra ese trabajo. **B solo si D1 = B** (con PaaS no hay dónde correr
  Mongo persistente gratis).

**D3. Dónde se guarda el DOCX.**
- A) **En disco persistente, como hoy (`Storage:DataDirectory` en un volumen de la VM)**, incluido en el backup.
- B) En Mongo (GridFS), dentro de esta HU: el backend deja de depender de disco persistente.
- C) En un bucket compatible con S3 (Cloudflare R2, a confirmar capa gratis).
- **Recomendada: A si D1 = A** (cero cambios de código; un DOCX con capturas embebidas puede pesar varios MB y en
  disco no compite con la base). **Si D1 = B, es obligatorio B o C**: B es más simple (una sola cosa que respaldar),
  pero con Atlas M0 los 512 MB (a confirmar) se llenan con unas decenas o pocos cientos de informes, según cuántas
  capturas tengan; C escala mejor pero suma otra cuenta, credenciales y código. **Pregunta:** ¿tienen una idea de
  cuántos informes por mes esperan en el primer año?

**D4. Cómo se sirve el frontend.**
- A) **El contenedor `standalone` actual (Node) detrás de Caddy, en la misma VM.**
- B) Export estático (`output: "export"`, posible porque la app es 100 % cliente, E2) servido por Caddy o por
  Cloudflare Pages, sin proceso Node.
- C) Cloudflare Workers con OpenNext (uso comercial permitido en el plan gratis, a confirmar).
- **Recomendada: A.** No toca `client/` ni rompe la instalación local de Windows (que usa la misma imagen); con la RAM
  de A1 el proceso Node no es un problema. B es una mejora válida si algún día el host queda justo de RAM, pero
  implica dos modos de build. C agrega un adaptador y otro proveedor sin beneficio para una app sin SSR. (Vercel Hobby
  queda descartado: prohíbe uso comercial.)

**D5. Dominio, subdominios y HTTPS.**
- A) **Dominio propio** (p. ej. `factum.com.ar` o similar; tiene costo anual, no es gratis) con `app.<dominio>` para
  la web y `api.<dominio>` para el backend; HTTPS automático con Let's Encrypt vía Caddy. DNS en Cloudflare (gratis)
  pero **sin proxy (nube gris)** en `api.`.
- B) Subdominio gratis (DuckDNS u otro) con Caddy + Let's Encrypt.
- C) Mismo origen: la web en `<dominio>/` y la API en `<dominio>/api` (evita CORS del backend).
- **Recomendada: A.** El origen del frontend **queda escrito en el Tatana de cada PC** (D8): cambiar de dominio más
  adelante obliga a reconfigurar todas las PCs de todos los clientes, así que conviene elegir el definitivo ahora. Un
  dominio propio también da confianza a estudios y fuerzas de seguridad. Si se usa el proxy de Cloudflare (nube
  naranja) delante de la API, ojo con el tope por request del plan gratis (100 MB, a confirmar) frente a los 256 MiB
  de `generate/finish` (B6). C es posible pero el backend también sirve `/health` y `/tatana/updates/` fuera de
  `/api`, y no aporta mucho porque el CORS ya está resuelto. **Si no quieren pagar dominio: B**, sabiendo que migrar
  después es costoso. **Pregunta:** ¿tienen o quieren comprar un dominio? ¿Cuál?

**D6. Región del servidor y datos personales.**
Los expedientes tienen DNI, nombres e informes periciales.
- A) **La región más cercana a los clientes con capacidad gratis** (p. ej. São Paulo o Santiago en Oracle, a confirmar
  disponibilidad de A1).
- B) Cualquier región con capacidad.
- **Recomendada: A** por latencia. La decisión sobre transferencia internacional de datos personales (Ley 25.326 si
  los clientes son argentinos) es del usuario; esta HU solo la deja anotada en la guía. **¿Hay algún cliente
  (p. ej. una fuerza policial) que exija que los datos queden en el país?** Si sí, el plan gratuito probablemente no
  sirva para ese cliente.

**D7. Secretos y superadmins iniciales.**
- A) **Archivo `.env` solo en el host (permisos 600, fuera del repo), leído por el compose**; el repo trae
  `.env.example`. Si hay CI (D9), los mismos valores en GitHub Actions Secrets solo si el workflow los necesita.
- B) Gestor de secretos del proveedor (OCI Vault, etc.).
- **Recomendada: A.** Es simple, auditable y portable; B agrega dependencia del proveedor sin beneficio real a esta
  escala. **El usuario tiene que pasar fuera del chat** (no al repo): DNI y nombre de él y de Leo; las contraseñas
  temporales las genera él en el host. Se quitan del `.env` después del primer ingreso.

**D8. Cómo recibe cada Tatana el origen de la nube.**
Hoy Tatana solo acepta `localhost:3000` (T1).
- A) **El paquete de Tatana trae el origen horneado**: un parámetro de empaquetado (p. ej. `FACTUM_WEB_ORIGIN`) que
  escribe `Agent:AllowedOrigins` en el `appsettings.json` del portátil/instalador, conservando los `localhost`; el
  portátil además abre `CLIENT_URL=https://app.<dominio>` al iniciar sesión. Se distribuye un Tatana "para la nube"
  descargable desde la guía o la web.
- B) Edición manual en cada PC (variable `Agent__AllowedOrigins__0` o `appsettings.Local.json`).
- C) Tatana descarga la lista de orígenes del backend al arrancar.
- **Recomendada: A.** El perito no tiene que tocar archivos de configuración; B no escala y es fuente de soporte; C
  abre una vía para que alguien que controle la red cambie qué páginas pueden hablar con Tatana. **Pregunta:** ¿hoy
  hay PCs de clientes con Tatana instalado que vayan a pasar a la nube? ¿Usan el portátil o el instalador?

**D8b. Auto-actualización de Tatana desde la nube.**
El mecanismo actual espeja releases de **GitLab** (B9, `.gitlab-ci.yml`), pero el repo ya vive en GitHub.
- A) **Fuera de esta HU**: v1 se distribuye a mano (link de descarga); se arma una HU propia para releases de Tatana
  en GitHub + espejo en el backend de la nube.
- B) Incluirlo: portar el CI de Tatana a GitHub Actions y configurar `TatanaUpdates` contra GitHub.
- **Recomendada: A.** Es un trabajo de tamaño propio (CI de Windows/Electron, NSIS, uxplay) y no bloquea salir a
  producción. **Pregunta:** ¿el GitLab sigue existiendo y en uso, o quedó abandonado?

**D9. CI/CD.**
- A) **Despliegue manual con un script en el host** (`git pull` del tag/commit de `main` + `docker compose build` +
  `up -d`) y un workflow de GitHub Actions que **solo verifica** (build .NET y `tsc`) en los PR a `develop`/`main`.
- B) Deploy automático desde `main`: Actions arma imágenes `arm64`, las publica en GHCR (privado) y entra por SSH al
  host para actualizar.
- **Recomendada: A** para arrancar. B obliga a guardar una clave SSH del servidor en GitHub, y armar imágenes `arm64`
  en runners x64 con emulación es lento y consume minutos del plan gratis en repos privados (a confirmar cupo y
  disponibilidad de runners ARM gratis para repos privados). Con pocos despliegues por mes, el script manual es más
  seguro y suficiente. Se puede pasar a B en otra HU.

**D10. Navegadores soportados para hablar con Tatana desde HTTPS.**
Una página HTTPS que llama a `http://localhost` no es mixed content en Chrome/Edge/Firefox (localhost es "potentially
trustworthy"), pero Chrome/Edge recientes aplican **Local Network Access** y piden permiso (a confirmar el
comportamiento de la versión estable actual). En **Safari** el soporte de `http://localhost` (y `ws://localhost`)
desde HTTPS es dudoso (a confirmar en la versión actual).
- A) **Soporte oficial: Chrome y Edge actualizados en Windows** (donde corre Tatana con USB); Firefox "debería
  andar"; Safari no soportado hasta probarlo. La guía y el login lo dicen.
- B) Soportar también Safari en macOS, con lo que haga falta.
- **Recomendada: A.** Los peritos usan Windows (todo el empaquetado de Tatana es Windows) y la web ya maneja el
  permiso de Chrome/Edge (T2). **Pregunta:** ¿algún cliente usa Mac con Tatana?

**D11. Backups.**
- A) **Diario automático (cron en el host): `mongodump` comprimido + los DOCX, subido fuera del host** a un almacenamiento
  gratis (Oracle Object Storage o Cloudflare R2, a confirmar capa gratis), con retención de, p. ej., 7 diarios + 4
  semanales, y un simulacro de restauración documentado.
- B) Solo backups locales en el mismo host.
- C) Snapshots del volumen del proveedor.
- **Recomendada: A.** Si el proveedor reclama o pierde la VM (D1), un backup en el mismo host se pierde con ella. C
  depende del proveedor y no sirve para mudarse. Los backups contienen datos personales y hashes de usuarios: se cifran
  antes de subir (p. ej. `age`/`gpg`) y la clave la guarda el usuario fuera del servidor. **Pregunta:** ¿qué pérdida
  de datos toleran (un día, una hora)? Define la frecuencia.

**D12. Monitoreo y logs.**
- A) **Monitor externo gratuito** (UptimeRobot, Better Stack o similar, a confirmar límites) contra `/health` cada pocos
  minutos con alerta por mail; `/health` pasa a chequear también que Mongo responde (cambio chico en el backend, sin
  exponer detalles); logs de Docker con rotación (`max-size`/`max-file`); el proxy sin access log o sin query strings
  (B10).
- B) Solo logs de Docker, sin alertas.
- **Recomendada: A.** Con un solo host gratis, enterarse de una caída por un cliente es lo peor para un SaaS nuevo.
  Agregar el ping a Mongo en `/health` evita el falso "ok" con la base caída. **Pregunta:** ¿a qué mail(s) van las
  alertas?

**D13. Headers de seguridad.**
- A) **En el proxy: HSTS, `X-Content-Type-Options`, `Referrer-Policy`, `frame-ancestors 'none'`, y CSP en modo
  `Report-Only`** primero (tiene que permitir `connect-src` a la API, `http://localhost:8765` y `ws://localhost:8765`),
  pasando a CSP efectiva en una HU posterior tras revisar reportes.
- B) Sin CSP, solo los demás headers.
- C) CSP efectiva desde el día uno.
- **Recomendada: A.** HSTS y compañía no rompen nada; una CSP efectiva mal calibrada puede bloquear Tatana o los
  estilos de PrimeReact/Framer Motion sin que se note hasta producción.

**D14. Qué pasa si el plan gratis se queda corto o se pierde.**
- A) **La guía documenta un "plan de salida"**: señales (RAM/disco al 80 %, VM reclamada), y el procedimiento para
  levantar el mismo compose en un VPS pago y restaurar el último backup (objetivo: horas, no días). Sin trabajo
  adicional en esta HU.
- B) Además, dejar preparado un segundo proveedor en frío.
- **Recomendada: A.** Con compose + backup fuera del host, mudarse es restaurar; B duplica costos de mantenimiento
  antes de tener clientes. **Pregunta:** ¿hay presupuesto aprobado para pasar a un plan pago si hace falta, o tiene
  que ser gratis sí o sí?

**D15. Convivencia con la instalación local de Windows.**
- A) **Las dos siguen**: la nube es una opción más; `deploy/windows` no cambia de comportamiento (el criterio de
  aceptación lo verifica) y el compose de producción vive en su propia carpeta (`deploy/nube/`).
- B) La instalación local queda congelada/deprecada.
- **Recomendada: A.** Hay clientes que pueden necesitar todo dentro de su red (fuerzas de seguridad, D6) y el costo de
  mantener ambos es bajo porque comparten imágenes.

**D16. Un ambiente de prueba ("staging").**
- A) **Sin staging permanente**: antes de cada actualización se prueba en local con el compose de producción apuntando
  a una base de ensayo; producción es la única en la nube.
- B) Un segundo compose en la misma VM con otro subdominio y otra base.
- **Recomendada: A** para empezar (la VM es una sola y los recursos gratis son finitos). B si el número de clientes
  crece.

**D17. Cómo verificar esta HU.**
- A) **Despliegue real en la topología elegida + checklist manual**: HTTPS válido, escaneo de puertos (solo 80/443),
  login de superadmins con cambio de contraseña, alta de un cliente, flujo de un perito con Tatana real en una PC
  Windows (captura → generar → DOCX descargable desde otra PC con SHA-256 igual a `report_hash`), reinicio de la VM sin
  pérdida, backup + restauración en un host limpio, alerta del monitor al apagar el backend, y armado de
  `deploy/windows` sin cambios de comportamiento.
- B) Solo verificar que el compose de producción levanta en local.
- **Recomendada: A.** Lo que puede fallar (certificados, firewall del proveedor, LNA del navegador, CPU ARM, reclamo
  de la VM) solo aparece en el entorno real. El reviewer verifica lo del repo; el usuario corre el checklist en vivo.

## Validación del usuario (2026-10-06)

- **D1:** cambia. Se descarta Oracle Always Free: el usuario acepta invertir algo de plata. Va **un VPS "Cloud Server" de
  DonWeb (Argentina)** con el `docker-compose` de producción: Caddy + frontend + backend + Mongo, como en la opción A
  pero en DonWeb. Si el VPS es x86_64, no hacen falta imágenes ARM.
- **D2–D4:** A, todo en el mismo VPS (Mongo en un contenedor con autenticación y sin puerto publicado; DOCX en un volumen
  persistente; el frontend `standalone` detrás de Caddy).
- **D5:** cambia. **El nombre del producto todavía no está definido** (están por arrancar con una agencia de marketing y
  puede dejar de llamarse "Factum"), así que no se compra dominio por ahora.
  - El dominio queda **100% configurable** (`.env` del host, build args del frontend, parámetro de empaquetado de Tatana)
    y nunca hardcodeado.
  - El piloto arranca con un **dominio provisorio**: el hostname que asigne DonWeb, o DuckDNS si hace falta para el HTTPS
    con Let's Encrypt.
  - **Tatana acepta una lista de orígenes**, así puede tener cargados el provisorio y el definitivo y el cambio de
    dominio no obliga a reconfigurar las PCs.
  - La guía documenta el procedimiento de cambio de dominio.
  - Un posible renombre del producto en la UI y en el informe es una HU aparte.
- **D6:** se resuelve con DonWeb, que tiene los datos en Argentina.
- **D9:** cambia. El deploy es **automático cuando se mergea `develop` a `main`** (GitHub Actions → imágenes en GHCR → SSH
  restringido al VPS → `docker compose pull/up` → chequeo de `/health` con rollback). El script manual queda como plan B.
- **D7, D8, D8b, D10–D17:** las recomendadas.
- **Pedido explícito del usuario:** una **guía paso a paso de DonWeb** en `docs/despliegue-nube.md`:
  - qué plan de Cloud Server contratar (recursos mínimos);
  - la creación del servidor, el acceso SSH con clave y el firewall (solo 22/80/443);
  - Docker, el clonado del repo, el `.env` y el primer arranque;
  - el HTTPS, el bootstrap de los superadmins, los backups, el monitor externo, el deploy de actualizaciones y el
    cambio de dominio.
  Los pasos del panel de DonWeb que no se puedan verificar se marcan "a confirmar en el panel".
