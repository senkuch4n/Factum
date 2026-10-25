# SDD — fiscalia-operacion (#95): el servidor de la fiscalía como canal de actualización de Tatana en Docker + `actualizar.sh`

**HU:** `docs/hu-fiscalia-operacion.md` (validada el 2026-10-20 con **alcance reducido**, ver su sección final).
**Rama:** `feat/fiscalia-operacion`, apilada sobre `feat/instalacion-fiscalia` (PR #98 sin mergear: rebasar sobre
`develop` cuando entre).
**Implementa:** `implementer-backend` (todo es `deploy/`, `ops/` y CI; no hay código de producto).
**Base que se reusa:** `deploy/fiscalia/` (#94), `deploy/tatana-docker/` (#94 §7), `ops/tatana/` (sobre firmado
Ed25519), `.github/workflows/tatana-release.yml` y `tatana-linux.yml`, SDD `tatana-instalador-autoupdate.md`
(reglas del actualizador: sin downgrade, verificar antes de instalar, nunca durante una captura) e
`instalacion-fiscalia.md`.

---

## 1. Resumen funcional

El servidor de la fiscalía (plan A, `https://factum.internal`) pasa a ser **el único lugar que se actualiza**. Un
paquete de actualización chico (solo lo que cambió) se lleva en persona o por AnyDesk y se aplica con
`sudo bash actualizar.sh`, que verifica el paquete, hace un **backup previo obligatorio**, carga las imágenes
nuevas, levanta la versión nueva y **vuelve sola a la anterior** si no queda sana, y **publica la imagen de Tatana
en Docker** en un canal estático servido por Caddy (`https://factum.internal/tatana/docker/`). En cada PC de perito
un **timer de systemd** (cada 30 min con desfase aleatorio) consulta ese canal, verifica el manifiesto firmado con
la **misma clave Ed25519** de Tatana (producto nuevo `tatana-docker`) usando el Python de la imagen ya instalada,
baja y verifica la imagen, y **solo con Tatana libre** cambia el contenedor conservando datos, evidencia, pareos y
configuración; si la versión nueva no responde en 2 minutos vuelve a la anterior y no la reintenta. Cada consulta
se identifica (equipo, versión, resultado) y el servidor arma con el registro de Caddy la lista "qué PC tiene qué
versión". Las PCs instaladas el 2026-10-20 se suman con **un comando por PC** que baja todo del servidor.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API, `server/src/Factum.Backend`) | **no** | — |
| backend (Tatana, `server/src/Factum.Agent`) | **no** | Se usan tal cual `GET /health`, `GET /agent/state`, `POST`/`DELETE /agent/maintenance`. |
| client | **no** | — |
| agent-ui | **no** | Solo se **lee** `agent-ui/src/main/updater/trusted-keys.ts` (fuente única de las claves públicas). |
| deploy / ops / CI | **sí** | `deploy/tatana-docker/`, `deploy/fiscalia/`, `ops/tatana/`, `.github/workflows/`, `ci/`, docs. |

Campo `Toca` del Project: `backend` (lo implementa `implementer-backend`). No hay `implementer-frontend`.

## 3. Alcance validado y mapeo con la HU

Dentro (decisiones de la HU: D1=A, D2=A, D3=A, D4=B, D5=B, D6=A, D8=A, D9=A, D10 presencial/AnyDesk, D16=B):

| Escenario de la HU | Sección |
|---|---|
| Actualizar Factum en el servidor (correcta / no levanta / paquete dañado o versión menor) | §8.3–§8.5 |
| Tatana en Docker: versión nueva y libre / capturando / firma o hash inválidos / no arranca / servidor caído / nunca baja / poco disco | §7.4–§7.7 |
| Saber qué versión tiene cada PC (D5=B) | §8.6 |
| Sumar el actualizador a una PC ya instalada (D6=A) | §9.2 |
| Estado local / desinstalar | §7.8, §7.9 |
| Backup previo de `actualizar.sh` y "nunca corren dos operaciones a la vez" | §8.4 (paso 3), §8.7 |

**Fuera** (por la validación del 2026-10-20): backups programados, retención/USB/cifrado (D11–D15), plan B
(D16), AppImage en el canal del servidor (D7), pantalla en la web (D5=C, D15=B), descargas diferenciales por capas
(ver DU-2), rotación de la clave, actualizar Tatana en Windows desde el servidor.

## 4. Vista general

```
 Mac / CI                                  Servidor de la fiscalía (plan A)                  PC de perito (×4+)
 ─────────                                 ───────────────────────────────                   ──────────────────
 tatana-docker.yml (tag tatana-vX.Y.Z)     sudo bash actualizar.sh                           factum-tatana-actualizador.timer
   arma imagen amd64 + kit                   [1] verifica SHA256SUMS                           (5 min tras arrancar, cada 30 min,
   autopruebas                               [2] requisitos + imágenes requeridas               desfase aleatorio ≤ 10 min)
   firma tatana-update-docker.json  ──┐      [3] backup previo (backup.sh)                     ▼
   (environment tatana-release)       │      [4] carga imágenes nuevas                       actualizador-tatana-docker.sh (root)
   GitHub Release tatana-vX.Y.Z       │      [5] copia archivos (guarda los anteriores)        GET manifiesto ?equipo=…  ──────┐
                                      │      [6] levanta y espera salud ─ falla → vuelve       verifica firma (Python de la     │
 armar-paquete-fiscalia.sh            │      [7] publica canal-tatana/docker/ ◄──────┐         imagen INSTALADA + claves de     │
   --actualizacion                    │      [8] limpieza + resumen                  │         /opt, no del servidor)           │
   → Factum-Fiscalia-Actualizacion-   │                                              │         baja imagen + kit, SHA-256       │
     vA.B.C/ (solo lo que cambia) ◄───┘    Caddy: handle_path /tatana/* → /srv/canal-tatana   carga + autoprueba (si libre)      │
        │ pendrive / AnyDesk                       log JSON → datos/caddy-logs/tatana.log ◄──  /agent/maintenance → cambio de     │
        └──────────────────────────────►  estado-peritos.sh lee ese log (D5=B)                 contenedor → /health vN+1 ≤ 2 min  │
                                                                                               si no: vuelve a vN, marca fallida ◄┘
```

## 5. Producto `tatana-docker`: manifiesto, firma y CI

### 5.1 Sobre y payload

Mismo sobre que Windows/Linux (`ops/tatana/_lib.mjs`, `signEnvelope`): archivo **`tatana-update-docker.json`**,
`{"schema":1,"key_id":"…","payload":"<base64 de los bytes exactos>","signature":"<base64, 64 bytes>"}\n`, firmado
con `TATANA_FIRMA_CLAVE_PRIVADA` (environment `tatana-release`). Payload (JSON, snake_case, bytes exactos firmados):

| Campo | Tipo | Regla |
|---|---|---|
| `schema` | int | `1` |
| `product` | string | **`"tatana-docker"`** (un manifiesto de `tatana-windows`/`tatana-linux` no sirve y al revés) |
| `version` | string | `X.Y.Z` (sin prerelease, `RELEASE_VERSION` de `_lib.mjs`) |
| `released_at` | string | ISO-8601 |
| `commit` | string | 40 hex |
| `file` | string | **exactamente** `imagen-tatana-docker-v<version>.tar.gz` (`docker save` + gzip) |
| `size` | int | > 0, bytes de `file` |
| `sha256` | string | 64 hex minúsculas de `file` |
| `sha512` | string | base64 de 64 bytes de `file` (se conserva por uniformidad con los otros productos) |
| `image` | string | **exactamente** `factum-tatana-docker:<version>` (nombre:tag que tiene que quedar cargado) |
| `kit` | object | `{ "file": "kit-tatana-docker-v<version>.tar.gz", "size": int > 0, "sha256": "64 hex" }` |
| `notes` | string? | opcional, ≤ 2000 |

### 5.2 Cambios en `ops/tatana/`

- `_lib.mjs`: `PRODUCTS = ['tatana-windows','tatana-linux','tatana-docker']`; `expectedFile('tatana-docker', v)` =
  `imagen-tatana-docker-v${v}.tar.gz`; `payloadError` suma, **solo para `tatana-docker`**, la validación de `image`
  y `kit` (para los otros productos, si aparecen esos campos, es error: no se mezclan formatos).
- `armar-payload.mjs`: con `--producto tatana-docker` **no** pide `--latest`; pide `--archivo <imagen .tar.gz>`,
  `--kit <kit .tar.gz>` e `--imagen factum-tatana-docker:X.Y.Z`; calcula `size/sha256/sha512` de la imagen y
  `size/sha256` del kit.
- `verificar-manifiesto.mjs`: `--producto tatana-docker`, `--archivo` (imagen) y `--kit` (compara tamaño y SHA-256).
- `firmar-manifiesto.mjs`: sin cambios de interfaz (el producto sale del payload).
- `probar-firma.mjs`: casos nuevos (ida y vuelta docker; docker rechazado como linux/windows y al revés; `kit`
  alterado; `image` con otro tag; `file` con otro nombre).
- **Nada** cambia en `agent-ui/src/main/updater/manifest.ts`: Electron nunca pide `tatana-update-docker.json` y su
  chequeo de `product` ya rechazaría uno.

### 5.3 Workflow nuevo `.github/workflows/tatana-docker.yml` (D8=A)

Separado de `tatana-release.yml` (D-T2): una falla de Docker no frena la release de Windows/Linux ni al revés; misma
versión (`tatana-vX.Y.Z`) y mismo GitHub Release.

- **Disparadores:** `push` de tags `tatana-v*`; `workflow_dispatch` (`version`, `publicar`); `pull_request` a
  `develop`/`main` con paths `deploy/tatana-docker/**`, `deploy/linux/**`, `packaging/linux/**`,
  `server/src/Factum.Agent/**`, `ops/tatana/**`, `ci/tatana-docker/**`, el propio workflow.
- **Modos** (idénticos a `tatana-release.yml`, D-T23 de esa SDD): `release` solo desde un commit que está en `main`
  (P3), `ensayo` en PRs y en dispatch sin `publicar` (clave **efímera**, sin secrets).
- **Jobs:**
  1. `preparar` — versión y modo (copiar la lógica de `tatana-release.yml`, incluida la verificación de que
     `TATANA_FIRMA_KEY_ID` está en `trusted-keys.ts`).
  2. `armar` (ubuntu-24.04, nativo amd64) — `generar-lock.py --check`; `armar-tatana-linux.sh` desde `git archive`;
     `armar-tatana-docker.sh --version X --agente-tar … --salida … --canal <dir>` (§6). Sube artifacts
     `tatana-docker-paquete` (el `Tatana-Docker-vX.tar.gz` completo) y `tatana-docker-canal` (imagen, kit y
     `payload-docker.json` sin firmar). En **ensayo** también firma con la clave efímera y deja la clave pública
     efímera como artifact `clave-ensayo-publica` (nunca la privada).
  3. `firmar` (release, `environment: tatana-release`) — `armar-payload.mjs --producto tatana-docker` →
     `firmar-manifiesto.mjs` → `verificar-manifiesto.mjs --producto tatana-docker --archivo --kit --payload`.
     Artifact `tatana-docker-canal-firmado`.
  4. `prueba` (ubuntu-24.04) — suite funcional del actualizador (§14.2, nivel 2).
  5. `prueba-2004` (ubuntu-24.04 + contenedor `ubuntu:20.04` con systemd) — §14.2, nivel 3.
  6. `publicar` (release, needs `prueba` y `prueba-2004`, `permissions: contents: write`) — sube al GitHub Release
     `tatana-vX.Y.Z` (crearlo si no existe, `--clobber` si existe: el otro workflow puede haberlo creado antes)
     exactamente: `tatana-update-docker.json`, `imagen-tatana-docker-vX.Y.Z.tar.gz`,
     `kit-tatana-docker-vX.Y.Z.tar.gz`, `Tatana-Docker-vX.Y.Z.tar.gz`, `SHA256SUMS-docker`. **No** publica nada en el
     VPS de la nube (la fiscalía no usa la nube).

## 6. Kit y paquete de Tatana en Docker

`armar-tatana-docker.sh` (existente) cambia así:

- Imagen con nombre **oficial** `factum-tatana-docker` (ya es el default; `--imagen` queda solo para pruebas y el
  manifiesto exige el oficial).
- **Kit** (`kit-tatana-docker-vX.Y.Z.tar.gz`, decenas de KB) = carpeta `Tatana-Docker-vX.Y.Z/` **sin** la imagen:

```
Tatana-Docker-vX.Y.Z/
  LEEME.txt  VERSION  IMAGEN.txt  SHA256SUMS          (SHA256SUMS lista TODO el paquete, incluida la imagen)
  comun.sh                                            (+ armar_run, cambiar_contenedor, version_cmp, candado; §7.3)
  instalar-tatana-docker.sh  estado-tatana-docker.sh  desinstalar-tatana-docker.sh  diagnostico-distro.sh
  actualizador-tatana-docker.sh                       NUEVO (§7.4)
  sumar-actualizador.sh                               NUEVO (§9.2; el servidor lo publica suelto)
  estado-agente.py  verificar-manifiesto-docker.py    NUEVOS (§7.5, §10.3, §10.4)
  claves-confiables.txt                               NUEVO: "key_id spkiDerBase64" por línea, generado de trusted-keys.ts
  systemd/factum-tatana-actualizador.service  systemd/factum-tatana-actualizador.timer   NUEVOS (§7.2)
```

- **Paquete completo** `Tatana-Docker-vX.Y.Z.tar.gz` = kit + `imagen-tatana-docker-vX.Y.Z.tar.gz` (como hoy: sirve
  para instalar con pendrive sin servidor).
- `--canal <dir>`: además deja en `<dir>` la imagen, el kit y `VERSION` (lo que firma CI y publica el servidor).
- `--clave-extra key_id:spki` (solo ensayo/pruebas): agrega esa línea a `claves-confiables.txt`. En release
  `tatana-docker.yml` verifica que el kit publicado tenga **exactamente** las claves de `trusted-keys.ts`.
- La lectura de `trusted-keys.ts` se hace con el mismo regex que `readTrustedKeys` de `_lib.mjs` (un
  `node ops/tatana/…` chico que imprime `key_id spki`, o `node -e` importando `_lib.mjs`): una sola fuente.
- Las autopruebas existentes (`--autoprueba-linux`, `--autoprueba-ios`) siguen; se suma que
  `verificar-manifiesto-docker.py` y `estado-agente.py` corren con el Python de la imagen
  (`/opt/tatana/tools/python-embed/bin/python3 -I … --autoprueba`).

## 7. PC del perito

### 7.1 Archivos (todos de root salvo lo que ya era del perito)

| Ruta | Dueño / modo | Qué |
|---|---|---|
| `/opt/factum-tatana-docker/kits/<versión>/` | root 755 | Kit extraído de cada versión instalada (se conservan la actual y la anterior). |
| `/opt/factum-tatana-docker/actual` | symlink | → `kits/<versión instalada>`; las units llaman a `actual/actualizador-tatana-docker.sh`. |
| `/etc/factum-tatana-docker/actualizador.env` | root 644 | `CANAL_URL`, `CA`, `HABILITADO` (§10.6). |
| `/etc/factum-tatana-docker/instalacion.env` | root 644 | **Copia canónica** de la instalación para recrear el contenedor sin el perito delante (§10.5). |
| `/etc/factum-tatana-docker/ca-factum-fiscalia.crt` | root 644 | Copia de la CA (de `/usr/local/share/ca-certificates/factum-fiscalia.crt`). |
| `/var/lib/factum-tatana-docker/estado.env` | root 644 | Estado del actualizador (§10.7); lo lee `estado-tatana-docker.sh` sin sudo. |
| `/var/lib/factum-tatana-docker/descargas/` | root 700 | Descargas en curso (`.parcial`, se reanudan) y verificadas. |
| `/var/lib/factum-tatana-docker/imagenes.txt` | root 644 | Historial de imágenes que cargó Factum (para no borrar nunca una imagen ajena). |
| `/var/lib/factum-tatana-docker/candado` | root 644 | `flock` común a actualizador, instalador y desinstalador. |
| `/var/log/factum-tatana-docker/actualizador.log` | root 644 | Una línea por evento (además del journal). Rotación propia: > 1 MiB → `.1`. |
| `/etc/systemd/system/factum-tatana-actualizador.{service,timer}` | root 644 | §7.2. |
| `~/.local/share/tatana-docker/instalacion.env` | perito | Se sigue escribiendo (compatibilidad con los scripts del perito). |

Los datos del agente (`~/.local/share/tatana-docker/{agent-data,home,lockdown,appsettings.Local.json}`) y la
evidencia (`~/Factum/Evidencia`) **no cambian de lugar ni de dueño**.

### 7.2 Units de systemd (D1=A, D2=A)

```ini
# factum-tatana-actualizador.timer
[Unit]
Description=Factum: buscar actualizaciones de Tatana en Docker en el servidor de la fiscalía
[Timer]
OnBootSec=5min
OnUnitActiveSec=30min
RandomizedDelaySec=10min
AccuracySec=1min
[Install]
WantedBy=timers.target

# factum-tatana-actualizador.service
[Unit]
Description=Factum: actualizador de Tatana en Docker
After=docker.service network-online.target
Wants=network-online.target
ConditionPathExists=/etc/factum-tatana-docker/actualizador.env
[Service]
Type=oneshot
ExecStart=/opt/factum-tatana-docker/actual/actualizador-tatana-docker.sh --una-vez
Nice=10
IOSchedulingClass=best-effort
IOSchedulingPriority=7
TimeoutStartSec=60min
PrivateTmp=yes
NoNewPrivileges=yes
```

Compatibles con systemd 237 (Ubuntu 18.04) y 245 (20.04). `systemd-analyze verify` en CI (§14.2).

### 7.3 Funciones nuevas en `comun.sh` (una sola fuente para instalador, actualizador y desinstalador)

- `leer_instalacion_root` — lee `/etc/factum-tatana-docker/instalacion.env` con lista blanca de claves y valida
  cada valor con regex (§10.5); nunca `source`.
- `armar_run <imagen>` — arma el arreglo `RUN` + `AGENTE` que hoy está **en línea** en `instalar-tatana-docker.sh`
  (pasos [6/7]), parametrizado con `UID/GID/GIDS/DATOS/EVIDENCIA/PUERTO/MOCK/ZONA/RED_CON_PUERTOS` en vez de
  `id -u`/`$HOME`, para que root lo pueda reproducir **idéntico**. El instalador pasa a usarla (mismo resultado).
- `cambiar_contenedor <imagen nueva> <versión esperada> <segundos>` — el cambio con vuelta atrás (§7.6). Lo usan el
  actualizador, `--volver-a-anterior` **y el instalador** cuando ya hay un contenedor (hoy el instalador lo borra
  sin red de seguridad y sin mirar si Tatana está capturando).
- `version_cmp A B` → `-1|0|1` con precedencia semver (prerelease < release; identificadores numéricos vs
  alfanuméricos) — tabla de casos en §10.8. Compatible con bash 4.4.
- `tomar_candado` — `flock -n` sobre `/var/lib/factum-tatana-docker/candado` (fd de solo lectura: el perito también
  puede tomarlo). Si otro lo tiene: el actualizador sale en silencio (`RESULTADO=ocupado_otro_proceso`), el
  instalador avisa y corta. Si el archivo todavía no existe (primera instalación, o PC del 2026-10-20) no hay nada
  con qué competir: se sigue sin candado.
- `estado_agente <accion>` — corre `estado-agente.py` (§10.4) con el Python de la **imagen instalada**:
  `docker run --rm --network host --entrypoint /opt/tatana/tools/python-embed/bin/python3 -v <kit>:/k:ro <imagen
  instalada> -I /k/estado-agente.py --puerto $PUERTO --accion …`. (Con `RED_CON_PUERTOS=1`, solo pruebas: `docker
  exec` en el contenedor.) Así el host no necesita curl, wget ni Python.

### 7.4 `actualizador-tatana-docker.sh --una-vez` (root; lo corre el timer)

Paso a paso (cada salida deja `estado.env` y una línea en el log; nunca imprime en la sesión del perito):

0. `tomar_candado`; leer `actualizador.env` (`HABILITADO=0` → `deshabilitado`, fin) e `instalacion.env` root.
   **Recuperación de un cambio a medias** (corte de luz durante §7.6): si no existe el contenedor `tatana-docker` y
   sí `tatana-docker-anterior` → renombrarlo y arrancarlo (`RESULTADO=recuperado`); si existen los dos y
   `tatana-docker` responde `/health` → borrar el `-anterior`.
1. **Versión instalada** = etiqueta `org.opencontainers.image.version` de la imagen del contenedor `tatana-docker`
   (fallback: `version` de `/health`). Imagen instalada = `.Config.Image` del contenedor.
2. **Consulta**: resolver `factum.internal` en el host (`getent ahostsv4`); bajar el manifiesto con el `curl` de
   la imagen instalada: `docker run --rm --network host -v /etc/factum-tatana-docker/ca-factum-fiscalia.crt:/ca.crt:ro
   -v …/descargas:/d --entrypoint curl <imagen instalada> -fsS --max-time 30 --cacert /ca.crt --resolve
   factum.internal:443:<ip> -o /d/tatana-update-docker.json "<CANAL_URL>tatana-update-docker.json?<query §10.2>"`.
   Sin respuesta → `sin_servidor` (sin ningún otro efecto, escenario "Servidor caído").
3. **Verificación del manifiesto** con `verificar-manifiesto-docker.py` **del kit instalado** y
   `claves-confiables.txt` **del kit instalado**, corriendo con el Python de la imagen instalada y `--network none`
   (D-T3). Inválido → `manifiesto_invalido|clave_desconocida|firma_invalida` y fin.
4. **Decisión de versión**: `COMPARACION` ≠ `mayor` → `al_dia` (nunca baja). Versión en `VERSIONES_FALLIDAS` →
   `version_fallida` (hasta que se publique una mayor).
5. **Espacio**: libre en `DockerRootDir` (`docker info`) ≥ 4 × `size` + 1 GiB y en `/var/lib/factum-tatana-docker` ≥
   `size` + `kit.size` + 100 MiB; si no → `sin_espacio` con lo que falta y lo que hay.
6. **Descarga** de `file` y `kit.file` (mismo `curl` de la imagen, `-C -` para reanudar, `.parcial` → nombre final
   solo si tamaño y SHA-256 coinciden con el payload firmado). No coincide → borrar y `hash_invalido`.
7. **Carga y autoprueba — solo si Tatana está libre en ese momento** (`estado_agente estado`; D-T5): `docker load -i`;
   comprobar que existe `image` y que su etiqueta de versión es `version` (si no → `imagen_invalida`, se borra lo
   cargado); `docker run --rm --network none --cpus 1 <image> --autoprueba-linux /tmp/autoprueba` (falla →
   `autoprueba_fallida`, versión a `VERSIONES_FALLIDAS`, se borra la imagen). Si no está libre →
   `esperando_libre` (la descarga ya hecha se conserva).
8. **Kit nuevo**: verificar SHA-256 contra el payload, extraer en `kits/<version>.parcial` y renombrar.
9. **Cambio** (`cambiar_contenedor`, §7.6) usando `armar_run` **del kit nuevo** (D-T6) con `instalacion.env` root.
   Si no quedó libre al pedir mantenimiento → `esperando_libre`.
10. Éxito → `actual` → `kits/<version>`; reinstalar las units si cambiaron (`daemon-reload`); `VERSION_ANTERIOR`/
    `IMAGEN_ANTERIOR` = la que había; agregar `image` a `imagenes.txt`; **limpieza**: borrar imágenes de
    `imagenes.txt` que no sean la actual ni la anterior (nunca una que no esté en ese archivo, nunca una que use un
    contenedor) y los kits que no sean actual/anterior; borrar descargas. `RESULTADO=actualizada`.
11. **Reporte**: si la consulta del paso 2 no llevaba el resultado final, una segunda petición liviana (`HEAD` del
    manifiesto con la query actualizada) para que el servidor vea `actualizada`/`no_arranca` sin esperar 30 min.

### 7.5 "Libre" (D3=A)

`estado-agente.py --accion estado` devuelve `LIBRE=1` solo si en `GET /agent/state`: `busy == false`,
`maintenance == false`, `operations == []`, `recordings == []`, **sin** `recording_session`,
`background_operations == []` (posproceso de video: entrar en mantenimiento lo cancela) y
`unclaimed_recordings == []` (D-T7: una grabación detenida que la web todavía no importó; la lista vive en memoria
y se perdería al reiniciar). Cualquier otra cosa → `LIBRE=0` + `MOTIVOS=` con los `kind` (para el log y el
estado: "esperando a que termine la captura").

El cambio se hace con `POST /agent/maintenance` `{"ttl_seconds":300}`: `200` → seguir; `409 agent_busy` →
`esperando_libre` (resuelve la carrera "empezó una captura justo ahora"); otro → `error_agente`, no se toca nada.
La request sale del host sin `Origin` ni `Sec-Fetch-Site` (el endpoint lo exige).

### 7.6 `cambiar_contenedor` (vuelta atrás instantánea, D4)

1. Pedir mantenimiento (§7.5). No libre → salir sin tocar nada.
2. `docker stop -t 30 tatana-docker` → `docker rename tatana-docker tatana-docker-anterior` (el viejo **se
   conserva** detenido, con su configuración exacta).
3. `docker run` con `armar_run <imagen nueva>` (nombre `tatana-docker`, etiqueta `factum.tatana-docker=1`).
4. Esperar hasta **120 s** a que `/health` responda con `version == <versión esperada>`.
5. OK → `docker rm tatana-docker-anterior`. Falla (no arranca, versión distinta o `docker run` con error) →
   `docker logs --tail 60` al log, `docker rm -f tatana-docker`, `docker rename tatana-docker-anterior
   tatana-docker`, `docker start tatana-docker`, esperar `/health` de la versión vieja, versión nueva a
   `VERSIONES_FALLIDAS`, `RESULTADO=no_arranca`.
6. Un contenedor `tatana-docker` sin la etiqueta `factum.tatana-docker=1` **nunca** se toca (como hoy).

### 7.7 Volver a la anterior a mano (D4=B)

`./estado-tatana-docker.sh --volver-a-anterior` (perito o usuario; pide sudo) →
`sudo /opt/factum-tatana-docker/actual/actualizador-tatana-docker.sh --volver-a-anterior`: requiere
`IMAGEN_ANTERIOR` todavía cargada y su kit; respeta "libre" (si está ocupado: "Tatana está capturando: probá
cuando termine", sin forzar); usa `cambiar_contenedor` con el `armar_run` del kit **anterior**; marca la versión
de la que se vuelve como fallida (no se reinstala sola hasta que haya una mayor); `actual` → kit anterior.
Además `--buscar-ahora` (= `sudo systemctl start factum-tatana-actualizador.service` y muestra el resultado).

### 7.8 `estado-tatana-docker.sh` (bloque nuevo "Actualizaciones")

Sin sudo: servidor (`CANAL_URL`), habilitado sí/no, timer activo y próxima consulta
(`systemctl show … -p NextElapseUSecRealtime`), versión instalada / disponible / anterior, última consulta y
resultado en castellano (tabla §10.7), versiones fallidas, últimas 10 líneas de `actualizador.log`. Si el
actualizador no está instalado: "Esta PC no tiene el actualizador: ver docs/tatana-docker.md §4 (sumar-actualizador)".

### 7.9 `instalar-tatana-docker.sh` y `desinstalar-tatana-docker.sh`

Instalador (sigue corriendo con el usuario del perito; pide sudo solo donde hace falta):
- **Sin `--origen` y con una instalación previa → conserva los `ORIGENES` de antes** (hoy vuelve a solo
  localhost: reinstalar sin el flag le quitaba `https://factum.internal` a una PC del plan A).
- Si ya hay un contenedor andando: `tomar_candado` y `cambiar_contenedor` (respeta "libre"; si está ocupado corta
  con "Tatana está capturando…"; `--forzar` lo saltea con confirmación escrita). Si la versión nueva no arranca,
  queda la anterior.
- Paso nuevo **"[8/8] Actualizador automático"**: por defecto **sí** si los orígenes incluyen
  `https://factum.internal` (`CANAL_URL=https://factum.internal/tatana/docker/`); `--actualizador <url>` para otra,
  `--sin-actualizador` para no instalarlo. Con sudo: copiar el kit a `/opt/factum-tatana-docker/kits/<v>`, symlink
  `actual`, escribir `/etc/factum-tatana-docker/{instalacion.env,actualizador.env,ca-factum-fiscalia.crt}`
  (la CA del almacén del sistema; si no está: "corré preparar-pc-perito.sh primero" y se instala igual con
  `HABILITADO=0`), units, `daemon-reload`, `enable --now` del timer, registrar la imagen en `imagenes.txt` (y la
  imagen que había antes, como anterior).
- Persistir en los dos `instalacion.env` las claves nuevas (§10.5).

Desinstalador: además de lo de hoy, `disable --now` del timer, borrar units, `/opt/factum-tatana-docker`,
`/etc/factum-tatana-docker`, `/var/lib/factum-tatana-docker` y `/var/log/factum-tatana-docker`, y el contenedor
`tatana-docker-anterior` si existiera. **Nunca** la evidencia ni `~/.local/share/tatana-docker`. `--borrar-imagen`
borra también las de `imagenes.txt`.

## 8. Servidor de la fiscalía (plan A)

### 8.1 Canal en Caddy (`deploy/fiscalia/servidor/Caddyfile`)

Dentro del bloque `factum.internal`, antes del `handle` final:

```caddy
	# Canal de Tatana en Docker (SDD fiscalia-operacion §8.1): archivos estáticos que publica actualizar.sh.
	# La integridad no depende de este servidor: cada PC verifica la firma Ed25519 con sus propias claves.
	handle_path /tatana/* {
		root * /srv/canal-tatana
		@manifiesto path /docker/tatana-update-docker.json
		header @manifiesto Cache-Control "no-cache"
		file_server
	}
```

Registro solo del canal (D5=B), a nivel de sitio, con `log_skip` en los otros `handle` (no se registra el uso de la
web):

```caddy
	log {
		output file /var/log/caddy/tatana.log {
			roll_size 5MiB
			roll_keep 5
		}
		format json
	}
```

El implementador valida la sintaxis con la misma imagen (`docker run --rm -v …:/etc/caddy/Caddyfile:ro caddy:2.11.7
caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile`) y comprueba en la prueba (§14.3) que la línea
registrada trae la URI **original** con la query.

`compose.servidor.yml`, servicio `caddy`, volúmenes nuevos:

```yaml
      - ${FACTUM_HOME}/canal-tatana:/srv/canal-tatana:ro
      - ${FACTUM_DATOS}/caddy-logs:/var/log/caddy
```

`instalar.sh` crea `canal-tatana/docker` (root 755) y `datos/caddy-logs` (root 700). Sin `browse`: no se listan
carpetas. El canal no es secreto (mismo criterio que `/tatana/updates` de la nube).

Layout publicado:

```
/srv/factum-fiscalia/canal-tatana/docker/
  tatana-update-docker.json          (manifiesto firmado; se reemplaza ÚLTIMO, con mv atómico)
  imagen-tatana-docker-vX.Y.Z.tar.gz
  kit-tatana-docker-vX.Y.Z.tar.gz
  sumar-actualizador.sh              (extraído del kit publicado; §9.2)
  VERSION                            (versión publicada, texto)
  (+ imagen y kit de la versión anterior; las más viejas se borran)
```

### 8.2 Paquete de actualización (`armar-paquete-fiscalia.sh --actualizacion`)

Mismo script (una sola fuente de los pasos de build, D-T8) con un modo nuevo:

```
deploy/fiscalia/armar-paquete-fiscalia.sh --actualizacion --version A.B.C [--ref <git ref>]
    [--imagenes-de <versión instalada>]                       # solo scripts/compose/canal: reusa las imágenes de esa versión
    [--tatana-docker-release X.Y.Z | --tatana-docker-canal <dir>]
    [--incluir-base]                                           # suma mongo/caddy (solo si cambió su tag)
    [--deploy-de-trabajo] [--salida <dir>]
→ <salida>/Factum-Fiscalia-Actualizacion-vA.B.C/  + .tar.gz + .tar.gz.sha256
```

- Sin plan B: no arma `frontend-una-pc`, no baja Docker estático, no baja mongo/caddy (salvo `--incluir-base`).
- Imágenes comprimidas (`docker save | gzip -6` → `imagenes/<nombre>.tar.gz`; `docker load` acepta gzip en
  cualquier versión).
- `--imagenes-de V`: no construye backend/frontend; escribe `imagenes-de.txt` = `V` (D-T9).
- `--tatana-docker-release X.Y.Z`: `gh release download tatana-vX.Y.Z` de los 3 archivos del canal (§5.3) y los
  verifica con `node ops/tatana/verificar-manifiesto.mjs --producto tatana-docker --archivo --kit --version` antes
  de incluirlos. `--tatana-docker-canal <dir>` hace lo mismo con archivos locales (también verificados: un canal sin
  firma válida **no** entra al paquete).
- Tamaño esperado (anotarlo en el progress): backend ≈ 45 MB + frontend-servidor ≈ 25 MB + Tatana ≈ 290 MB; con
  `--imagenes-de` y sin Tatana nuevo: < 1 MB.

Layout (contrato con `actualizar.sh`, §10.9):

```
Factum-Fiscalia-Actualizacion-vA.B.C/
  actualizar.sh  LEEME.txt  tipo.txt ("actualizacion")  version.txt  origen.txt  requiere.txt  SHA256SUMS
  [imagenes-de.txt]
  comun/      (_comun.sh, backup.sh, restaurar.sh, diagnostico.sh, borrar-caso-prueba.sh, estado-peritos.sh,
               docker-compose.yml, compose.superadmin.yml, compose.sin-avx.yml, mongo-init/, mongo-init-sin-avx/,
               appsettings.Local.example.json)
  servidor/   (compose.servidor.yml, Caddyfile)
  [imagenes/backend.tar.gz  imagenes/frontend-servidor.tar.gz  (imagenes/mongo*.tar.gz caddy.tar.gz con --incluir-base)]
  [tatana-docker/  tatana-update-docker.json  imagen-tatana-docker-vX.Y.Z.tar.gz  kit-tatana-docker-vX.Y.Z.tar.gz  VERSION]
  instalacion-fiscalia.md
```

`requiere.txt`: una imagen por línea (`mongo:7.0.43`, `mongo:4.4.29`, `caddy:2.11.7`, …, sacadas de los compose del
paquete, como hoy) que tienen que estar **cargadas** en el servidor si no vienen en `imagenes/`.

El paquete completo de instalación también acepta `--tatana-docker-release|--tatana-docker-canal` (misma carpeta
`tatana-docker/`) y `instalar.sh` del plan A publica el canal si la trae (función común `publicar_canal_tatana`).

### 8.3 `actualizar.sh` — interfaz

```
sudo bash actualizar.sh [--carpeta /srv/factum-fiscalia] [--si]
                        [--sin-backup-previo] [--permitir-version-menor] [--solo-tatana]
```

- Interactivo por defecto (una confirmación antes de pausar la web); `--si` no pregunta (AnyDesk con la terminal
  minimizada). `--sin-backup-previo` pide escribir `SIN BACKUP` (con `--si` además hace falta
  `FACTUM_CONFIRMO_SIN_BACKUP=1` en el entorno: nunca se saltea por accidente).
- Todo lo que muestra va también a `/srv/factum-fiscalia/registro/actualizar-AAAAMMDD-HHMMSS.log` (root 600).
- Mensajes `OK / AVISO / FALLA` y pasos `[n/8]` con las funciones de `_comun.sh`. Solo plan A: con `FACTUM_PLAN`
  ≠ `servidor` corta ("el plan B no se actualiza con este script en esta versión").
- Por AnyDesk la terminal vive en el escritorio del servidor: cortar AnyDesk **no** corta el script (decirlo en la
  guía; no hace falta `nohup`).

### 8.4 `actualizar.sh` — pasos

1. **Paquete**: `tipo.txt == actualizacion`; `verificar_paquete` (SHA256SUMS de todo, antes de tocar nada);
   `cargar_instalacion`; `tomar_candado` (§8.7). Comparar `version.txt` con `FACTUM_VERSION` (`version_cmp`):
   menor → corta salvo `--permitir-version-menor` (confirmación escrita); igual → si trae Tatana más nuevo que el
   publicado, pasa a **solo Tatana** (pasos 7–8, sin backup ni reinicio); si no, "ya está en vX, nada que hacer".
   `--solo-tatana` fuerza ese modo.
2. **Requisitos**: Docker y compose; cada imagen de `requiere.txt` cargada o incluida; con `imagenes-de.txt`, que
   existan `factum-backend:<V>` y `factum-frontend-servidor:<V>`; espacio para las imágenes (3 × lo comprimido) y
   para el backup (misma cuenta que `backup.sh`: evidencia + 1 GB). **Cualquier faltante corta acá, sin haber
   tocado nada**, con el motivo y qué paquete hace falta (p. ej. "este paquete necesita caddy:2.12.0: usá
   `--incluir-base` al armarlo").
3. **Backup previo** (D9=A): `comun/backup.sh` **del paquete** con `--carpeta` y el candado heredado; guardar la
   ruta del backup. Si falla, corta (la web vuelve sola por el `trap` de `backup.sh`).
4. **Imágenes**: `docker load` de `imagenes/*.tar.gz`; con `imagenes-de.txt`, `docker tag factum-backend:<V>
   factum-backend:<nueva>` (ídem frontend-servidor). Comprobar que existen los tags nuevos. Las de la versión
   instalada **no se tocan** (tags distintos).
5. **Archivos**: copiar a `<carpeta>/actualizacion-anterior/` (reemplazando la anterior) `docker-compose.yml`,
   `compose.*.yml`, `Caddyfile`, `mongo-init*/`, `version.txt`, `scripts/` y `config/.env`; copiar los nuevos (como
   el paso 6 de `instalar.sh`, que se extrae a una función común `copiar_archivos_instalacion` para no duplicar);
   `env_poner FACTUM_VERSION <nueva>`. Conserva `config/.env` (salvo la versión), `appsettings.Local.json`,
   `branding/`, el modo sin AVX (`FACTUM_MONGO_SIN_AVX` se respeta, como en `--reparar`) y `datos/`.
6. **Arranque**: `fc up -d --remove-orphans` + `fc up -d --force-recreate --no-deps caddy` (el Caddyfile es un bind
   de un solo archivo: recrear asegura que lea el nuevo); `esperar_sano 300`; `curl --resolve
   factum.internal:<443>:127.0.0.1 --cacert <CA> https://factum.internal/health` y `/health/ready`.
   **Falla** → vuelta atrás automática: restaurar los archivos de `actualizacion-anterior/` (incluido
   `FACTUM_VERSION`), `fc up -d --remove-orphans --force-recreate`, `esperar_sano 300`, y terminar con código 1
   diciendo qué paso falló, "se volvió a vX", la ruta del backup previo y el comando exacto
   `sudo /srv/factum-fiscalia/scripts/restaurar.sh --desde <backup previo>` ("solo si la versión nueva llegó a
   modificar datos; la base **no** se restaura sola"). **No** publica Tatana. Si ni la anterior queda sana:
   `mostrar_estado` + `mostrar_log_backend` + guía §9.7.
7. **Tatana en Docker** (si el paquete trae `tatana-docker/`): leer la versión del payload del manifiesto
   (`base64 -d` + extraer `"version"`) y exigir que sea igual a `tatana-docker/VERSION` (coherencia; la firma la
   verifica cada PC). Si es mayor que `canal-tatana/docker/VERSION` (o no hay canal) → `publicar_canal_tatana`:
   copiar imagen y kit con nombre `.parcial` + `mv`, extraer `sumar-actualizador.sh` del kit, escribir `VERSION`,
   **mover el manifiesto al final**, borrar los archivos de versiones anteriores a la previa. Si es igual o menor →
   "Tatana vX ya publicado" (nunca se publica una menor: las PCs igual la rechazarían).
8. **Limpieza y resumen**: borrar imágenes `factum-backend:*` / `factum-frontend-servidor:*` / `factum-frontend:*`
   que no sean la versión nueva ni la anterior (nunca mongo/caddy ni imágenes ajenas). Mostrar: "Factum vX.Y.Z
   funcionando. Tatana en Docker vA.B.C publicado: las PCs se actualizan solas en los próximos ~40 minutos mientras
   no estén capturando. Ver: sudo /srv/factum-fiscalia/scripts/estado-peritos.sh", ruta del backup previo y del
   registro.

### 8.5 Paquete dañado / otro plan / versión menor

Todo eso corta en los pasos 1–2, **antes** del backup y sin tocar contenedores ni archivos (escenario "Paquete
dañado o de otro plan"). Un paquete de instalación completo (`tipo.txt` ausente) → "este es un paquete de
instalación: para actualizar usá el de actualización (o `instalar-servidor.sh --reparar`, sin backup previo)".

### 8.6 `estado-peritos.sh` (D5=B) y `diagnostico.sh`

`sudo /srv/factum-fiscalia/scripts/estado-peritos.sh [--horas 72]`: lee `datos/caddy-logs/tatana.log*` (JSON por
línea; parseo con `awk`/`sed` sobre `"ts"`, `"remote_ip"`, `"uri"`: sin jq ni Python en el host), filtra las URIs
del manifiesto con `equipo=`, se queda con la **última** por `equipo`+`remote_ip`, y muestra:

```
Tatana en Docker publicado: 1.5.0
EQUIPO           IP              INSTALADA  DISPONIBLE  ÚLTIMA CONSULTA     RESULTADO
pc-perito-1      192.168.1.21    1.5.0      1.5.0       hace 12 min         al día
pc-perito-3      192.168.1.23    1.4.0      1.5.0       hace 3 min          esperando a que termine la captura
pc-perito-4      192.168.1.24    1.4.0      1.5.0       hace 2 días (!)     sin consultas recientes
```

`(!)` si la última consulta tiene más de 2 h. `diagnostico.sh` suma una sección "Tatana en Docker (canal)": versión
publicada, cantidad de PCs vistas en 24 h y cuántas no están en la versión publicada (con el comando de arriba).

### 8.7 Una operación a la vez

`_comun.sh` suma `tomar_candado` (`flock -n` sobre `<carpeta>/.operacion.lock`). Lo toman `actualizar.sh`,
`backup.sh`, `restaurar.sh` e `instalar.sh --reparar`. Si lo tiene otro: "hay otra operación de Factum en curso
(actualizar/backup/restaurar): esperá a que termine". Los scripts llamados por otro que ya tiene el candado lo
heredan con `FACTUM_CANDADO_HEREDADO=1` (backup previo de `actualizar.sh` y de `restaurar.sh`). Sin `flock` (Mac de
pruebas): aviso y sigue.

## 9. Migración de lo instalado el 2026-10-20 (sin perder datos ni evidencia)

Estado real (memoria del proyecto y bitácora): servidor plan A en `/srv/factum-fiscalia`, Factum
`0.0.0-fiscalia-prueba` (imágenes `factum-backend|factum-frontend-servidor:0.0.0-fiscalia-prueba`, Caddy sin canal);
≥ 4 PCs con Tatana en Docker **`tatana-docker-prueba:0.0.0-docker-prueba`**, contenedor `tatana-docker` (etiqueta
`factum.tatana-docker=1`), datos en `~<perito>/.local/share/tatana-docker`, instalado con
`--origen https://factum.internal`, **sin actualizador**. Las versiones de prueba son prerelease: cualquier `X.Y.Z`
real es mayor (§10.8), así que no hace falta ningún truco de versiones.

### 9.1 Servidor (una vez, por AnyDesk o en persona)

1. Precondición: una release firmada `tatana-vX.Y.Z` con los archivos de Docker (ver **DU-1**).
2. En la Mac: `armar-paquete-fiscalia.sh --actualizacion --version 0.1.0 --tatana-docker-release X.Y.Z` y, según
   **DU-3**, `--imagenes-de 0.0.0-fiscalia-prueba` (paquete ≈ 290 MB, casi todo Tatana) o imágenes nuevas (≈ 360 MB).
3. Copiarlo al servidor (AnyDesk, transferencia de archivos) con su `.sha256`; `sha256sum -c`; descomprimir en el
   disco (no en el pendrive).
4. `sudo bash Factum-Fiscalia-Actualizacion-v0.1.0/actualizar.sh`. Lo que hace sobre la instalación vieja:
   backup previo completo en `/srv/factum-fiscalia/backups/` (base + evidencia + config + CA); reemplaza
   compose/Caddyfile/scripts (guardando los viejos en `actualizacion-anterior/`); `config/.env`, `datos/`,
   `appsettings.Local.json`, `branding/`, la CA de Caddy (`datos/caddy-data`, **no cambia**: los certificados de las
   PCs siguen valiendo) y el modo sin AVX quedan como estaban; crea `canal-tatana/` y `datos/caddy-logs/`; publica
   Tatana. Nada se borra de `datos/` ni de `Storage`.
5. Comprobar: `https://factum.internal` abre igual desde una PC de perito; `curl -I` del manifiesto responde 200.

### 9.2 Cada PC de perito (una vez; un comando, lo corre el usuario — D6=A)

En una terminal con la sesión del perito (sin sudo; el script la pide una vez):

```bash
wget -qO /tmp/sumar-actualizador.sh https://factum.internal/tatana/docker/sumar-actualizador.sh \
  && bash /tmp/sumar-actualizador.sh
```

(Ubuntu de escritorio trae `wget`; si no, `curl -fsSo …`. La PC ya confía en la CA y resuelve `factum.internal`
por `preparar-pc-perito.sh`; si no, el script lo dice.)

`sumar-actualizador.sh` (en el kit; el servidor lo publica suelto):
1. Chequeos: Linux x86_64, Docker accesible (`docker` o `sudo docker`), que **no** corra como root.
2. Baja el manifiesto, el kit y la imagen a `~/Tatana-Docker-vX.Y.Z/` (con `wget`, `curl` o, si no hay ninguno,
   el `curl` de la imagen instalada), con reanudación.
3. **Verificación**: extrae el kit en una carpeta temporal y corre su `verificar-manifiesto-docker.py` con el Python
   de la **imagen instalada** (la vieja ya trae `cryptography`); compara tamaño y SHA-256 de kit e imagen con el
   payload. Modelo de confianza de **este único paso**: el script y el kit llegan por TLS con la CA de la fiscalía
   (el mismo nivel de confianza que un pendrive del usuario); desde ahí en adelante cada actualización se verifica
   con las claves **ya instaladas** en `/opt`, no con lo que mande el servidor (D-T3). Si no hay Tatana instalado
   (PC nueva), se verifica con el Python de la imagen recién descargada y se avisa que es una instalación nueva.
4. Arma la carpeta del paquete (kit + imagen; el `SHA256SUMS` del kit la cubre) y corre
   `./instalar-tatana-docker.sh --si` **sin** `--origen` → conserva `ORIGENES` (§7.9). El instalador: espera a
   que Tatana esté libre (si está capturando, corta y pide repetir después: no se perdió nada), carga la imagen,
   hace el cambio con vuelta atrás, instala el actualizador (`sudo`), registra la imagen vieja
   (`tatana-docker-prueba:0.0.0-docker-prueba`) como anterior.
5. Lanza una primera consulta (`systemctl start factum-tatana-actualizador.service`) para que la PC aparezca ya en
   `estado-peritos.sh`, y muestra el bloque "Actualizaciones" de `estado-tatana-docker.sh`.

Qué **no** cambia en la PC: `~/.local/share/tatana-docker/{agent-data,home,lockdown,appsettings.Local.json}`,
`~/Factum/Evidencia`, la regla udev, el usbmuxd del host enmascarado, `/etc/hosts`, la CA. El contenedor se recrea
con los **mismos** montajes, uid/gid, grupos, zona horaria y puerto (los toma de `instalacion.env` + `id` del
perito, que es quien corre el script).

### 9.3 Vuelta atrás de la migración

- Servidor: `actualizacion-anterior/` + el backup previo; o `instalar-servidor.sh --reparar` desde el paquete viejo
  (sigue en el disco del servidor).
- PC: `./estado-tatana-docker.sh --volver-a-anterior` vuelve a `0.0.0-docker-prueba` (su imagen queda cargada como
  anterior hasta la próxima actualización); `desinstalar-tatana-docker.sh` saca el actualizador sin tocar datos.

## 10. Contrato compartido

No cruza `client/` ↔ `server/`: los "lados" son **piezas de despliegue** que tienen que coincidir byte a byte. Cada
fila dice quién escribe y quién lee.

### 10.1 Manifiesto `tatana-update-docker.json`

Formato en §5.1. Escriben: `ops/tatana/armar-payload.mjs` + `firmar-manifiesto.mjs` (CI). Leen:
`ops/tatana/verificar-manifiesto.mjs` (CI y Mac), `deploy/tatana-docker/paquete/verificar-manifiesto-docker.py`
(PC), `deploy/fiscalia/comun/actualizar.sh` (solo `version`, sin verificar firma). Campos JSON exactos:
`schema`, `key_id`, `payload`, `signature` (sobre); `schema`, `product`, `version`, `released_at`, `commit`, `file`,
`size`, `sha256`, `sha512`, `image`, `kit.file`, `kit.size`, `kit.sha256`, `notes` (payload). Reglas de base64
estricto, firma de 64 bytes, `key_id` en la lista y Ed25519 sobre los bytes exactos: las de `verifyEnvelopeText`
de `_lib.mjs`, replicadas en Python. **Prueba cruzada obligatoria** (§14.1): un manifiesto firmado con Node verifica
en Python y uno alterado falla en los dos con el mismo motivo.

### 10.2 Consulta al canal (PC → Caddy → `estado-peritos.sh`)

`GET <CANAL_URL>tatana-update-docker.json?v=1&equipo=<hostname>&instalada=<versión>&disponible=<versión o vacío>&resultado=<código §10.7>`

Valores saneados a `[A-Za-z0-9._-]` y ≤ 64 caracteres (lo que no cumple se reemplaza por `_`). Escribe:
`actualizador-tatana-docker.sh`. Lee: `estado-peritos.sh` desde `request.uri` del log JSON de Caddy (más `ts` y
`request.remote_ip`). El reporte del paso 11 de §7.4 es un `HEAD` con la misma query.

### 10.3 `verificar-manifiesto-docker.py` (Python 3.11 de la imagen, `-I`, solo stdlib + `cryptography`)

```
python3 -I verificar-manifiesto-docker.py --manifiesto <ruta> --claves <claves-confiables.txt>
        [--version-instalada <v>] [--autoprueba]
```

Salida `stdout`, una `CLAVE=valor` por línea (bash la lee con lista blanca, nunca `source`):

| Código de salida | Claves |
|---|---|
| `0` (válido) | `VERSION`, `IMAGEN`, `ARCHIVO`, `TAMANO`, `SHA256`, `KIT_ARCHIVO`, `KIT_TAMANO`, `KIT_SHA256`, `KEY_ID`, `COMPARACION` (`mayor`/`igual`/`menor`, solo con `--version-instalada`) |
| `1` (inválido) | `MOTIVO` ∈ `manifiesto_invalido` / `clave_desconocida` / `firma_invalida`, `DETALLE` (una línea) |
| `2` | uso incorrecto |

`claves-confiables.txt`: líneas `key_id<espacio>spkiDerBase64`; `#` comentario. Generado de `trusted-keys.ts`.

### 10.4 `estado-agente.py` (Python de la imagen; habla con el agente en `127.0.0.1:<puerto>`)

```
python3 -I estado-agente.py --puerto N --accion estado|mantenimiento|salir-mantenimiento|version
        [--ttl 300] [--fixture <state.json>] [--autoprueba]
```

| Acción | HTTP | Salida |
|---|---|---|
| `estado` | `GET /agent/state` | `LIBRE=1|0`, `MOTIVOS=<kinds separados por coma>`, `VERSION=` · exit 3 + `INALCANZABLE=1` si no responde |
| `mantenimiento` | `POST /agent/maintenance` body `{"ttl_seconds":N}` | `MANTENIMIENTO=ok|ocupado|error`, `HTTP=<status>` |
| `salir-mantenimiento` | `DELETE /agent/maintenance` | `MANTENIMIENTO=fuera` |
| `version` | `GET /health` | `VERSION=<version>` |

Campos que lee de `/agent/state` (snake_case, tal como los serializa `AgentStateController`): `busy`, `maintenance`,
`operations[].kind`, `background_operations[].kind`, `recordings[]`, `recording_session` (ausente = sin sesión),
`unclaimed_recordings[]`, `version`. Sin encabezados `Origin`/`Sec-Fetch-Site`. `--fixture` (solo pruebas) lee el
JSON de un archivo en vez de la red.

### 10.5 `instalacion.env` (perito y copia root)

Claves (lista blanca; regex de validación en `leer_instalacion_root`):
`PUERTO` (1–65535), `IMAGEN` (`[a-z0-9._/-]+:[A-Za-z0-9._-]+`), `ORIGENES` (URLs separadas por coma, ya validadas por
`hornear-origenes-tatana.py`), `MOCK` (0/1), **nuevas:** `USUARIO` (`[a-z_][a-z0-9_-]*`), `UID`/`GID` (enteros
> 0), `GIDS` (`[0-9,]*`), `DATOS` y `EVIDENCIA` (rutas absolutas sin `..` ni espacios raros: `^/[A-Za-z0-9._/@+-]+$`),
`ZONA` (`[A-Za-z0-9_+/-]*`), `RED_CON_PUERTOS` (0/1). Escribe: `instalar-tatana-docker.sh`. Leen: `comun.sh`
(`leer_instalacion`, `leer_instalacion_root`), actualizador, desinstalador, estado. Una copia vieja sin las claves
nuevas (las PCs del 2026-10-20) se completa al correr el instalador nuevo (§9.2), nunca la inventa el actualizador:
sin `UID`/`DATOS` el actualizador queda `RESULTADO=sin_configuracion`.

### 10.6 `actualizador.env`

`CANAL_URL` (`https://…/`, termina en `/`), `CA` (ruta absoluta), `HABILITADO` (0/1). Escribe el instalador; lee el
actualizador.

### 10.7 `estado.env` y códigos de resultado

Claves: `ULTIMA_CONSULTA` (ISO-8601 local), `RESULTADO`, `DETALLE`, `VERSION_INSTALADA`, `IMAGEN_INSTALADA`,
`VERSION_DISPONIBLE`, `VERSION_ANTERIOR`, `IMAGEN_ANTERIOR`, `VERSIONES_FALLIDAS` (coma), `ULTIMA_ACTUALIZACION`.
Escribe el actualizador (y el instalador al instalar). Leen `estado-tatana-docker.sh` y la query de §10.2.

| `RESULTADO` | Texto en `estado-tatana-docker.sh` / `estado-peritos.sh` |
|---|---|
| `al_dia` | al día |
| `actualizada` | actualizada a vX |
| `esperando_libre` | esperando a que termine la captura |
| `sin_servidor` | no se pudo llegar al servidor |
| `manifiesto_invalido` / `clave_desconocida` / `firma_invalida` | manifiesto rechazado (motivo) |
| `hash_invalido` / `imagen_invalida` | descarga dañada (se descartó) |
| `sin_espacio` | sin espacio en disco (falta X, hay Y) |
| `autoprueba_fallida` | la versión nueva no pasó la autoprueba (no se instaló) |
| `no_arranca` | la versión nueva no arrancó: se volvió a la anterior |
| `version_fallida` | la versión publicada ya falló en esta PC (se espera una mayor) |
| `vuelta_manual` | se volvió a la versión anterior a mano |
| `recuperado` | se recuperó un cambio interrumpido |
| `sin_configuracion` / `deshabilitado` / `ocupado_otro_proceso` / `error_docker` / `error_agente` | (literal, con `DETALLE`) |

### 10.8 Comparación de versiones (bash `version_cmp` ↔ Python `--version-instalada`)

Precedencia semver 2.0. Casos que **las dos** implementaciones tienen que pasar (tabla de prueba compartida en
`ci/tatana-docker/versiones.txt`, `A B esperado`): `0.0.0-docker-prueba 0.1.0 -1`; `0.0.0-fiscalia-prueba 0.0.1 -1`;
`1.10.0 1.9.0 1`; `1.0.0-rc.1 1.0.0 -1`; `1.0.0-alpha 1.0.0-alpha.1 -1`; `1.0.0-alpha.2 1.0.0-alpha.10 -1`;
`1.0.0-beta 1.0.0-alpha 1`; `2.0.0 2.0.0 0`.

### 10.9 Paquete de actualización ↔ `actualizar.sh`

Layout y archivos de §8.2 (`tipo.txt`, `version.txt`, `requiere.txt`, `imagenes-de.txt`, `imagenes/*.tar.gz`,
`tatana-docker/*`, `SHA256SUMS` con rutas relativas, mismo formato que hoy `verificar_paquete`). Escribe
`armar-paquete-fiscalia.sh --actualizacion`; lee `actualizar.sh`.

### 10.10 Rutas del canal

`https://factum.internal/tatana/docker/{tatana-update-docker.json, imagen-tatana-docker-vX.Y.Z.tar.gz,
kit-tatana-docker-vX.Y.Z.tar.gz, sumar-actualizador.sh}` ↔ `<carpeta>/canal-tatana/docker/` en el host ↔
`/srv/canal-tatana/docker/` en el contenedor de Caddy. `CANAL_URL` por defecto en la PC:
`https://factum.internal/tatana/docker/`.

## 11. Decisiones técnicas

- **D-T1 — Todo en `deploy/`/`ops/`/CI, cero cambios en `Factum.Agent`.** `/agent/state` y `/agent/maintenance` ya
  alcanzan; con `--network host` el host llega a `127.0.0.1:8765` sin `Origin`.
- **D-T2 — Workflow propio `tatana-docker.yml`** en vez de sumar jobs a `tatana-release.yml`: canales y consumidores
  distintos (la fiscalía, transporte manual), y así una falla de uno no bloquea al otro. Misma versión y mismo
  GitHub Release.
- **D-T3 — Raíz de confianza en la PC = archivos de root en `/opt`, no el servidor.** El verificador y las claves
  que deciden si un manifiesto vale son los del **kit instalado**; el Python que hace la cuenta es el de la imagen
  **instalada** (OpenSSL 1.1.1 de Ubuntu 20.04 no verifica Ed25519 sobre datos crudos; el host puede no tener
  Python). El kit nuevo recién se usa cuando su SHA-256 coincide con el payload firmado. Un servidor adulterado
  puede, a lo sumo, no publicar o publicar algo que se rechaza.
- **D-T4 — Descargas y llamadas HTTP con `curl`/Python de la imagen instalada**, no del host (Ubuntu de escritorio
  20.04 no trae curl; el comentario de `comun.sh` ya lo asume). `factum.internal` se resuelve en el host y se pasa
  con `--resolve`.
- **D-T5 — Descargar siempre; cargar y autoprobar solo con Tatana libre; cambiar solo con mantenimiento.**
  `docker load` y la autoprueba usan CPU y disco unos minutos: en una PC vieja podrían afectar una grabación de
  pantalla en curso. La descarga por la red local no.
- **D-T6 — El contenedor nuevo se crea con `armar_run` del kit nuevo** (verificado): si una versión futura necesita
  otro flag de `docker run`, viaja con ella. El rollback usa el contenedor viejo **renombrado**, no una
  reconstrucción: vuelve exactamente como estaba.
- **D-T7 — `unclaimed_recordings` no vacío también posterga** (más estricto que Electron): esa lista vive en
  memoria y un reinicio dejaría una grabación detenida sin que la web la ofrezca para importar. Como mucho posterga
  24 h (vencen solas).
- **D-T8 — Paquete de actualización = modo `--actualizacion` del mismo `armar-paquete-fiscalia.sh`**, no un script
  nuevo: una sola definición de cómo se construyen las imágenes del plan A.
- **D-T9 — `--imagenes-de`**: actualización "solo scripts + canal" re-etiquetando en el servidor las imágenes ya
  cargadas. Permite un paquete de KB cuando no cambió la web ni la API (caso de la migración, DU-3).
- **D-T10 — `actualizar.sh` no restaura la base sola** (D9=A); vuelve imágenes **y** archivos (`compose`,
  `Caddyfile`, `.env`) a los de antes.
- **D-T11 — Identificación por query string** (no `User-Agent`): queda en `request.uri` del log JSON de Caddy sin
  configurar el registro de encabezados, y se parsea con `awk`. Se lista por `equipo` + IP (PCs clonadas pueden
  repetir el nombre).
- **D-T12 — Registro de Caddy solo del canal** (`log_skip` en web/API): no se guarda el uso de la web por
  privacidad y espacio. Rotación 5 × 5 MiB (≈ 4 PCs × 48 consultas/día × ~600 B: meses de historia).
- **D-T13 — Se conservan solo la versión actual y la anterior** (imágenes y kits, en PC y servidor). Las imágenes
  que se borran son solo las registradas por Factum (`imagenes.txt` en la PC; repositorios `factum-*` en el
  servidor).

## 12. Dudas que necesitan al usuario (no bloquean la implementación; sí el despliegue)

**DU-1 — De dónde sale la primera release firmada de Tatana en Docker.** La clave privada solo se usa desde `main`
(P3: environment `tatana-release`, el workflow verifica que el commit esté en `main`), y la memoria del proyecto dice
que `develop` → `main` se promueve **solo cuando lo pidas**. Además, empujar a `main` **despliega la nube**
(`desplegar-produccion.yml`).
- **A) (recomendada)** Cuando esta HU (y #94) estén en `develop` y quieras llevarla a la fiscalía, promover
  `develop` → `main` y pushear el tag `tatana-vX.Y.Z`: CI arma, prueba, firma y publica el Release con los archivos
  de Docker. Una sola cadena de confianza.
- B) Permitir `develop` en el environment `tatana-release` solo para `tatana-docker.yml`. Rápido, pero afloja P3
  (cualquier merge a `develop` podría firmar).
- C) Primera migración con un kit armado en la Mac **sin firma** (el bootstrap aceptaría `--sin-firma`). Las PCs
  quedarían con el actualizador igual, pero el primer paso no tendría ninguna verificación más allá de TLS.

**DU-2 — Tamaño del paquete cuando cambia Tatana (~290 MB).** La imagen de Tatana es casi toda `tools/` (adb,
scrcpy, ffmpeg, Python), que cambia poco.
- **A) (recomendada)** Imagen completa comprimida, **solo cuando Tatana cambió** (si no, el paquete no la trae). Es
  lo que soporta `docker load` en cualquier versión de Docker de las PCs.
- B) HU aparte: separar `tools/` y el agente en capas distintas del `Dockerfile` y armar paquetes "delta" con solo
  las capas nuevas (~30–70 MB). Riesgo: `docker load` de un archivo sin todas sus capas depende del almacén de
  imágenes de cada Docker (el de containerd, default en Docker 29, puede rechazarlo); hay que probarlo en el Docker
  real de cada PC.

**DU-3 — Primera actualización del servidor: ¿con imágenes nuevas de la web/API o solo scripts + canal?**
- **A) (recomendada)** `--imagenes-de 0.0.0-fiscalia-prueba`: la web y la API quedan exactamente como hoy (cero
  riesgo para lo que ya usan los peritos), el paquete pesa lo que pesa Tatana, y `actualizar.sh` se estrena igual
  (backup previo, archivos nuevos, Caddy con el canal, vuelta atrás si algo falla).
- B) Construir backend y frontend desde `develop` (lleva todo lo que se haya mergeado desde el 2026-10-20). Suma
  ~70 MB y cambios de la web en la misma visita.

## 13. Checklist atómico (`implementer-backend`)

Regla dura de datos (AGENTS.md): no se toca la MongoDB de desarrollo ni `Storage`; las pruebas usan sus propias
instalaciones en carpetas temporales y borran solo lo que crearon. **No mover tarjetas ni tocar `progress/sesiones/`.**

**Firma (`ops/tatana/`)**
- [ ] `_lib.mjs`: producto `tatana-docker`, `expectedFile`, validación de `image` y `kit` (§5.1–5.2).
- [ ] `armar-payload.mjs`: modo docker (`--archivo`, `--kit`, `--imagen`; sin `--latest`).
- [ ] `verificar-manifiesto.mjs`: `--producto tatana-docker`, `--kit`.
- [ ] `probar-firma.mjs`: casos docker y cruzados (§5.2). `node ops/tatana/probar-firma.mjs` verde.

**Kit de Tatana en Docker (`deploy/tatana-docker/`)**
- [ ] `paquete/comun.sh`: `leer_instalacion_root`, `armar_run`, `cambiar_contenedor`, `version_cmp`, `tomar_candado`,
      `estado_agente` (§7.3); el `docker run` sale de `instalar-tatana-docker.sh` sin cambiar su resultado.
- [ ] `paquete/estado-agente.py` (§10.4) con `--fixture` y `--autoprueba`.
- [ ] `paquete/verificar-manifiesto-docker.py` (§10.3) con `--autoprueba` (firma/verifica con una clave efímera).
- [ ] `paquete/actualizador-tatana-docker.sh` (`--una-vez`, `--volver-a-anterior`, `--buscar-ahora`; §7.4–7.7).
- [ ] `paquete/systemd/factum-tatana-actualizador.{service,timer}` (§7.2).
- [ ] `paquete/sumar-actualizador.sh` (§9.2).
- [ ] `instalar-tatana-docker.sh`: conserva orígenes, candado, `cambiar_contenedor` con "libre", `--forzar`, paso
      [8/8] actualizador, `--actualizador`/`--sin-actualizador`, claves nuevas de `instalacion.env` (§7.9, §10.5).
- [ ] `estado-tatana-docker.sh`: bloque "Actualizaciones", `--volver-a-anterior`, `--buscar-ahora` (§7.7–7.8).
- [ ] `desinstalar-tatana-docker.sh`: quita actualizador, units, `/opt|/etc|/var/lib|/var/log` propios y
      `tatana-docker-anterior` (§7.9).
- [ ] `armar-tatana-docker.sh`: kit, `--canal`, `claves-confiables.txt` desde `trusted-keys.ts`, `--clave-extra`,
      autopruebas de los dos `.py` dentro de la imagen (§6).
- [ ] `paquete/LEEME.txt` actualizado.

**CI de Tatana en Docker**
- [ ] `.github/workflows/tatana-docker.yml` (§5.3), con actions fijadas por SHA como los otros workflows y valores
      por `env:` (nunca interpolados en `run:`).
- [ ] `ci/tatana-docker/prueba-actualizador.sh` (nivel 2, §14.2) y `ci/tatana-docker/prueba-2004.sh` (nivel 3).
- [ ] `ci/tatana-docker/versiones.txt` + prueba de `version_cmp` (bash) y del Python contra la misma tabla.

**Servidor (`deploy/fiscalia/`)**
- [ ] `servidor/Caddyfile`: `handle_path /tatana/*`, `log` + `log_skip` (§8.1); `caddy validate` con `caddy:2.11.7`.
- [ ] `servidor/compose.servidor.yml`: volúmenes `canal-tatana` y `caddy-logs`.
- [ ] `comun/_comun.sh`: `tomar_candado`, `version_cmp`, `copiar_archivos_instalacion`, `publicar_canal_tatana`.
- [ ] `comun/instalar.sh`: usa `copiar_archivos_instalacion`; crea `canal-tatana/docker` y `datos/caddy-logs`;
      publica el canal si el paquete trae `tatana-docker/`; candado en `--reparar`.
- [ ] `comun/actualizar.sh` (va en la raíz del paquete de actualización; §8.3–8.5).
- [ ] `comun/backup.sh` y `comun/restaurar.sh`: candado (con herencia, §8.7). Sin otros cambios de comportamiento.
- [ ] `comun/estado-peritos.sh` (§8.6) y sección nueva en `comun/diagnostico.sh`.
- [ ] `armar-paquete-fiscalia.sh`: `--actualizacion`, `--imagenes-de`, `--tatana-docker-release`,
      `--tatana-docker-canal`, `--incluir-base`; imágenes `.tar.gz` en ese modo (§8.2).
- [ ] `ci/fiscalia/prueba-actualizar.sh` (§14.3) + workflow `.github/workflows/fiscalia.yml` (PR a `develop`/`main`
      con paths `deploy/fiscalia/**`, `ci/fiscalia/**`).
- [ ] `LEEME.txt` del paquete de actualización (3 líneas: verificar, descomprimir, `sudo bash actualizar.sh`).

**Docs**
- [ ] `docs/instalacion-fiscalia.md`: §9.6 reescrita (Tatana en Docker se actualiza desde el servidor; el AppImage
      no se usa en esta fiscalía), sección nueva **"Actualizar Factum y Tatana"** (armar en la Mac, copiar por
      AnyDesk, `actualizar.sh`, qué hacer si vuelve atrás, `estado-peritos.sh`) y **checklist de la migración del
      2026-10-20** (§9), §8 menciona el backup previo de `actualizar.sh` y el candado.
- [ ] `docs/tatana-docker.md`: §4 "Sin actualizaciones automáticas" → cómo funciona el actualizador, estado,
      volver a la anterior, deshabilitarlo (`sudo systemctl disable --now factum-tatana-actualizador.timer`);
      §3 suma `sumar-actualizador.sh`; §6 la verificación nueva.
- [ ] `docs/despliegue-nube.md` §19/§20 (releases de Tatana): mencionar que el tag también dispara
      `tatana-docker.yml` y qué archivos suma al Release.

**Cierre**
- [ ] `progress/impl_backend_fiscalia-operacion.md`: archivos, comandos corridos con su salida resumida, tamaños de
      paquete, qué quedó para CI y qué para la fiscalía.

## 14. Verificación

### 14.0 Regla para la Mac (nota del implementador previo)

En la Mac **no** se corren contenedores privilegiados, ni con systemd como PID 1, ni `tonistiigi/binfmt`, ni nada
que toque binfmt: en Docker Desktop un `systemd-binfmt` dentro de un contenedor privilegiado desregistra los
manejadores del kernel compartido (Rosetta/qemu) y rompe todas las imágenes amd64 de la Mac (pasó el 2026-10-20).
Todo lo que necesita systemd, `--network host` real o USB va a **CI Linux** (runners `ubuntu-24.04`, VM efímera
propia: ahí un contenedor privilegiado no afecta a nadie) o a la **PC Ubuntu 24.04 del usuario** (nativa, x86_64).

### 14.1 Nivel 1 — en la Mac, antes de `done` (sin privilegios)

```bash
node ops/tatana/probar-firma.mjs
bash -n deploy/tatana-docker/paquete/*.sh deploy/fiscalia/comun/*.sh deploy/fiscalia/*.sh ci/tatana-docker/*.sh ci/fiscalia/*.sh
shellcheck -S warning <los mismos>            # si está instalado; si no, decirlo (CI lo corre)
# Python con la imagen real (amd64 emulada, SIN --privileged, --network none):
docker run --rm --network none -v "$PWD/deploy/tatana-docker/paquete:/k:ro" -v "$PWD/ci/tatana-docker:/c:ro" \
  --entrypoint /opt/tatana/tools/python-embed/bin/python3 <factum-tatana-docker:prueba> -I /k/verificar-manifiesto-docker.py --autoprueba
#   ídem estado-agente.py --autoprueba (fixtures libre/ocupado/posproceso/sesión/no reclamada/mantenimiento)
# Prueba cruzada §10.1: manifiesto firmado con Node (clave efímera) → verifica en Python; alterado → mismo MOTIVO.
bash ci/tatana-docker/probar-versiones.sh     # tabla §10.8 contra bash y Python
docker run --rm -v "$PWD/deploy/fiscalia/servidor/Caddyfile:/etc/caddy/Caddyfile:ro" caddy:2.11.7 \
  caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
deploy/tatana-docker/armar-tatana-docker.sh --version 0.0.2 --imagen factum-tatana-docker --salida <tmp> --canal <tmp>/canal --clave-extra <efímera>
deploy/fiscalia/armar-paquete-fiscalia.sh --actualizacion --version 0.0.2-prueba --imagenes-de 0.0.0-fiscalia-prueba \
  --tatana-docker-canal <tmp>/canal --deploy-de-trabajo --salida <tmp>   # anotar tamaño
```

Además, en la Mac se puede correr el **actualizador sin systemd ni red host** contra un Tatana `--mock` con
`--red-con-puertos` (no privilegiado) y un canal servido por un `caddy:2.11.7` no privilegiado en un puerto alto:
camino feliz y rollback. Es opcional (la prueba que cuenta es la de CI). Limpiar solo contenedores, imágenes y
carpetas propias.

### 14.2 Nivel 2 y 3 — CI de Tatana en Docker (`tatana-docker.yml`, PR en borrador)

**Nivel 2 — `prueba` (ubuntu-24.04 nativo, Docker real, systemd del runner):** `ci/tatana-docker/prueba-actualizador.sh`
1. Canal real: `caddy:2.11.7` con el **Caddyfile de la fiscalía** (`tls internal`) en 443, `factum.internal` →
   127.0.0.1 en `/etc/hosts`, CA exportada e instalada con `preparar-pc-perito.sh` (con `--usuario` del runner).
2. Base: imagen `X` re-etiquetada con `org.opencontainers.image.version=0.0.1` (un `FROM` + `LABEL`), instalada con
   `instalar-tatana-docker.sh --mock --origen https://factum.internal --si` como usuario no root.
3. Casos (cada uno publica en el canal y corre `systemctl start factum-tatana-actualizador.service`, verificando
   `estado.env` y el contenedor):
   - versión `X` firmada (en release, la **real**; en ensayo, efímera) → `actualizada`, `/health` = X, los
     archivos sembrados en `agent-data`, `home`, `lockdown` y `Evidencia` siguen iguales (hash antes/después),
     `appsettings.Local.json` igual, el contenedor conserva montajes/uid/gid/puerto (`docker inspect` comparado);
   - firma inválida, `key_id` desconocido, producto `tatana-linux`, imagen con un byte cambiado, kit alterado →
     rechazado con su código, **nada cargado** en Docker (`docker images` igual);
   - versión menor/igual → `al_dia`;
   - versión `X+1` que es `X` re-etiquetada (su `/health` dice `X`) → `no_arranca`, vuelve a `X` en < 3 min,
     `VERSIONES_FALLIDAS` la tiene, la consulta siguiente da `version_fallida`;
   - ocupado: `FACTUM_TATANA_ESTADO_FIXTURE` (variable **solo de prueba**, la lee `estado_agente`) con
     `operations` → `esperando_libre` y el contenedor no cambió de ID; luego libre → se actualiza;
   - `--volver-a-anterior` → vuelve, marca fallida;
   - sin espacio (`FACTUM_TATANA_MARGEN_DISCO_MB` enorme, solo prueba) → `sin_espacio`, nada descargado;
   - servidor caído (Caddy parado) → `sin_servidor`, Tatana sigue respondiendo;
   - corte a mitad del cambio (matar el proceso entre `rename` y `run`) → la siguiente corrida da `recuperado`;
   - `estado-peritos.sh` (con el log del Caddy de la prueba) lista el equipo con la versión y el resultado;
   - `desinstalar-tatana-docker.sh --si` → no quedan units ni `/opt|/etc|/var/lib` propios; evidencia intacta.

**Nivel 3 — `prueba-2004`:** contenedor `ubuntu:20.04` con systemd como PID 1, **privilegiado (solo en CI)**,
`systemd-binfmt.service` enmascarado igual, el socket de Docker del runner montado y las rutas usadas montadas
**1:1** (`/opt/factum-tatana-docker`, `/etc/factum-tatana-docker`, `/var/lib/factum-tatana-docker`, el home de
prueba), con un `docker` CLI estático adentro. Ahí: `bash` 5.0 y coreutils/systemd 245 reales de 20.04;
`systemd-analyze verify` de las units; instalación con el instalador nuevo; `systemctl list-timers` muestra el
timer; una corrida feliz y una de rollback vía `systemctl start`. (Opcional `ubuntu:18.04` con
`continue-on-error`.)

### 14.3 CI del servidor (`fiscalia.yml`, `ci/fiscalia/prueba-actualizar.sh`, ubuntu-24.04)

Construye `factum-backend` y `factum-frontend-servidor` una vez (nativo), los etiqueta `0.0.1`, arma un paquete de
instalación y lo instala en plan A en `/srv/factum-fiscalia-prueba` (proyecto de compose propio). Siembra un caso
con un archivo de evidencia propio. Casos:
- **actualizar a 0.0.2** (mismas imágenes re-etiquetadas en un paquete con `imagenes/`, + canal falso de Tatana) →
  backup previo en `backups/`, `FACTUM_VERSION=0.0.2`, servicios sanos, el caso y la evidencia siguen (hash), la CA
  de Caddy no cambió (huella), `https://factum.internal/tatana/docker/tatana-update-docker.json` → 200 con
  `Cache-Control: no-cache`, un `curl` con la query aparece en `estado-peritos.sh`;
- **0.0.3 rota** (`factum-backend:0.0.3` = una imagen que no pasa el healthcheck) → vuelve a 0.0.2 sola, archivos
  restaurados, canal **sin** cambios, código ≠ 0 y el mensaje trae el comando de `restaurar.sh`;
- **paquete dañado** (un byte cambiado) → corta en [1/8], **no** hay backup nuevo ni contenedores reiniciados;
- **versión menor** → corta; con `--permitir-version-menor --si` sigue;
- **`--imagenes-de`** → re-etiqueta y queda sano;
- **solo Tatana** (misma versión de Factum, Tatana mayor) → publica sin backup ni reinicio;
- **candado**: `backup.sh` mientras corre `actualizar.sh` → se niega.

### 14.4 Cómo se corre CI antes de `done`

Los workflows nuevos no existen en la rama por defecto, así que no hay `workflow_dispatch`: el **orquestador** abre
un **PR en borrador** de `feat/fiscalia-operacion` contra `develop` (como en #91) para que corran
`tatana-docker.yml`, `fiscalia.yml` y `verificar.yml` (este último suma shellcheck de los scripts nuevos y las
autopruebas Python). El implementador declara `done` con el nivel 1 verde y deja escrito en su progress qué espera
de CI; los rechazos por CI vuelven como reintento.

### 14.5 Prueba manual que queda para el usuario

1. **PC Ubuntu 24.04 del usuario (antes de ir):** instalar Tatana en Docker con el paquete nuevo y un servidor de
   prueba (plan A en esa misma PC o en la Mac por la red), **con un celular real**: capturar, y durante la captura
   publicar una versión nueva → no se toca; al terminar, se actualiza sola; la captura y el ZIP quedan; el iPhone
   sigue pareado (sin volver a "Confiar"), adb sigue viendo el Android.
2. **Fiscalía (después de DU-1):** §9.1 en el servidor y §9.2 en cada PC; `estado-peritos.sh` muestra todas las PCs
   en la versión publicada; una captura de punta a punta desde una PC actualizada.

## 15. Riesgos y notas

- **Docker viejo en alguna PC** (si no se usó el estático del paquete): `docker load` de un `docker save` hecho con
  Docker 29 en CI. El formato incluye `manifest.json` (legacy) y lo cargó bien el Docker de las PCs el
  2026-10-20 (imagen armada en Docker Desktop reciente); la verificación §14.5.1 lo confirma con el Docker de la PC
  del usuario, y si una PC falla el actualizador deja `error_docker` y Tatana sigue en la versión anterior.
- **Relación con #96 (`tatana-docker-oficial`, Backlog):** esta HU ya deja la imagen oficial armada y firmada en CI;
  #96 puede quedar acotada a la guía/instalación general fuera de la fiscalía. Avisar al afinarla.
- **FACTUM_TATANA_VERSION_MINIMA** (`config/.env`): `actualizar.sh` no la cambia; si una web nueva exige un Tatana
  nuevo, se ajusta a mano después de ver en `estado-peritos.sh` que todas las PCs se actualizaron.
