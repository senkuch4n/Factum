# HU: Fiscalía — backups programados en el servidor y actualización de Tatana en Docker desde el servidor

**Slug:** `fiscalia-operacion` · **Issue:** #95
**Toca (preliminar, lo confirma la SDD):**
- `deploy/fiscalia/`: `actualizar.sh` del servidor, backup programado (timer), publicación del canal de Tatana en
  Caddy, `diagnostico.sh`, `restaurar.sh`, armado del paquete.
- `deploy/tatana-docker/`: actualizador de la PC del perito (chequeo, verificación, cambio de contenedor, vuelta
  atrás), integración en `instalar-`/`estado-`/`desinstalar-tatana-docker.sh`, script para sumar el actualizador a
  una PC ya instalada.
- `ops/tatana/` (+ CI de Tatana): producto nuevo `tatana-docker` en el sobre firmado y firma de la imagen.
- `server/src/Factum.Backend` + `client/`: **solo si** D5/D15 eligen mostrar el estado en la web.
- `agent-ui/`: **solo si** D7 incluye el AppImage en el canal del servidor.
- Docs: `docs/instalacion-fiscalia.md` (§8 backups, §9.6, sección nueva "Actualizar"), `docs/tatana-docker.md` (§4).

**Como** usuario que instaló y mantiene Factum en la fiscalía (sin estar ahí todos los días),
**quiero** que el servidor de la fiscalía haga backups solo, todos los días, y que sea el único lugar que tengo que
actualizar para que Factum y el Tatana en Docker de cada PC de perito queden en la misma versión,
**para que** no se pierda evidencia ni trabajo si falla un disco, y no tenga que ir PC por PC con un pendrive cada
vez que sale una corrección de Tatana — sin arriesgar nunca una captura en curso.

---

## Contexto

### Qué existe hoy (arqueología sobre `feat/fiscalia-operacion` ≈ `fac67a7`, 2026-10-20)

**La instalación real (2026-10-20).** Plan A en una fiscalía: servidor con `https://factum.internal` (Caddy
`tls internal`), instalado en `/srv/factum-fiscalia/{config,datos,backups,scripts}`. Las PCs de perito usan
**Tatana en Docker** (la mayoría Ubuntu ≤ 20.04, glibc < 2.35: el AppImage no corre); alguna PC sin AVX; internet
lento. Todo se llevó en pendrive (Docker estático incluido).

**Backups del servidor**
| # | Dónde | Qué hace hoy |
|---|---|---|
| B1 | `deploy/fiscalia/comun/backup.sh` (copiado a `/srv/factum-fiscalia/scripts/`) | A mano, con `sudo`. Pausa web + API (y Caddy en el plan A) mientras hace `mongodump` + `tar.gz` de `datos/backend` (evidencia **completa** cada vez) y vuelve a levantar. Agrega `config.tar.gz` (**incluye `config/.env` con todos los secretos**), `caddy-data.tar.gz` (la CA interna), `version.txt`, `plan.txt`, `SHA256SUMS`. Escribe en `.<nombre>.parcial` y renombra al final. |
| B2 | Mismo script | Antes de empezar chequea espacio (evidencia + 1 GB) y **corta con error** si no alcanza. Retención: en `<instalación>/backups` deja los `FACTUM_BACKUP_CONSERVAR` más nuevos (default 10); en `--destino` externo no borra nada. **Sin cifrar.** |
| B3 | `docs/instalacion-fiscalia.md` §8 | Backup automático "opcional" con una línea de `sudo crontab -e` a las 20 h. No se instaló. Si la PC está apagada a esa hora, ese día no hay backup; si falla, solo queda en `backup.log`. |
| B4 | `deploy/fiscalia/comun/restaurar.sh` | Verifica `SHA256SUMS`, pide escribir `RESTAURAR`, **hace un backup del estado actual**, `mongorestore --drop` de `factum.*`, extrae la evidencia encima, restaura `appsettings.Local.json` y `branding/`, **no** pisa el `.env`. Avisa si la versión del backup no es la instalada. |
| B5 | `deploy/fiscalia/comun/diagnostico.sh` | Muestra contenedores, salud, disco, backups y versión instalada (sin secretos). |

**Actualización de Factum en el servidor**
| # | Dónde | Qué hace hoy |
|---|---|---|
| S1 | `deploy/fiscalia/comun/instalar.sh --reparar` | Vuelve a copiar compose/scripts y cargar imágenes sin tocar `config/.env` ni datos. Con un paquete de otra versión solo **avisa** "se pasa a X" (comentario: "la actualización no es parte de esta entrega"). **Sin backup previo, sin vuelta atrás** si la versión nueva no levanta. |
| S2 | Backend | **No hay sistema de migraciones**: al arrancar crea índices (`EnsureIndexesAsync`). Los cambios de modelo hasta hoy son aditivos. |
| S3 | `deploy/fiscalia/servidor/Caddyfile` | Variante de la nube **sin `/tatana/updates` ni `/descargas`** (se sacaron a propósito). Solo 80/443. |

**Tatana en Docker en la PC del perito** (`deploy/tatana-docker/`, `docs/tatana-docker.md`)
| # | Dónde | Qué hay |
|---|---|---|
| T1 | `instalar-tatana-docker.sh` | Corre con el usuario del perito (no root; `sudo docker` si no está en el grupo). Verifica SHA-256 de la imagen contra `SHA256SUMS` del paquete, `docker load`, escribe `~/.local/share/tatana-docker/appsettings.Local.json` (orígenes) e `instalacion.env` (`PUERTO`, `IMAGEN`, `ORIGENES`, `MOCK`), y **recrea** el contenedor `tatana-docker` (etiqueta `factum.tatana-docker=1`, `--network host`, USB por cgroup, `--restart unless-stopped`, uid/gid del perito). Reinstalar = actualizar; no se pierden datos. |
| T2 | `docs/tatana-docker.md` §4 | "**Sin actualizaciones automáticas**: se actualiza instalando el paquete nuevo." No hay UI (sin Electron): el estado se ve en la web y con `estado-tatana-docker.sh`. |
| T3 | La imagen (`ubuntu:24.04`) | Trae Python embebido (`/opt/tatana/tools/python-embed`) con pymobiledevice3 (y por lo tanto la librería `cryptography`). El instalador ya lo usa para validar orígenes sin Python en el host. |

**Canal firmado de Tatana (Windows y AppImage)** — `Refactorizaciones/tatana-instalador-autoupdate.md`, `tatana-linux`
| # | Dónde | Qué hay |
|---|---|---|
| C1 | `ops/tatana/_lib.mjs`, `firmar-manifiesto.mjs`, `verificar-manifiesto.mjs`, `armar-payload.mjs` | Sobre `tatana-update.json` con payload firmado **Ed25519** (bytes exactos), `key_id`, productos `tatana-windows` y `tatana-linux` (un manifiesto por SO, misma clave). Un manifiesto de un producto no sirve para otro. |
| C2 | Clave | Privada **solo** en el secreto `TATANA_FIRMA_CLAVE_PRIVADA` (environment `tatana-release` de GitHub Actions); públicas horneadas en `agent-ui/src/main/updater/trusted-keys.ts`. |
| C3 | `agent-ui/src/main/updater/` | Reglas: sin downgrade, verificar antes de instalar, URLs de canal horneadas (`dist-config.update_urls`, solo `https`) probadas en orden; nunca durante una captura. |
| C4 | `Factum.Agent` `AgentStateController` | `GET /agent/state` (`busy`, `operations`, `background_operations`, `recording_session`, `version` real) y `POST /agent/maintenance` (409 `agent_busy` si hay operaciones; desde ahí las mutantes reciben 503 `agent_updating`; **solo sin `Origin`**, o sea, desde la misma PC y no desde un navegador). `POST /agent/prepare-shutdown` para cortar ordenado. Con `--network host`, un script del host llega a `localhost:8765` igual que Electron. |
| C5 | AppImage en la fiscalía (`docs/instalacion-fiscalia.md` §9.6) | Busca actualizaciones en **el canal de la nube**. Sin internet no encuentra nada; con internet se puede adelantar a una versión de la nube que el servidor de la fiscalía todavía no tiene. |

**Cómo se ve la versión de Tatana hoy:** la web manda `agent_version` (real desde `real_version_v1`) en la
auditoría de cada captura y en los datos del host del informe. No hay ningún lugar que liste "qué PC tiene qué
versión".

### Por qué hace falta
- Hoy **no hay ningún backup automático** en una fiscalía que ya está cargando evidencia real, y los que se hacen
  a mano quedan en el mismo disco del servidor.
- Cada corrección de Tatana obliga a ir a cada PC con un pendrive (el internet es lento y Tatana en Docker no se
  actualiza solo). Y una web nueva puede necesitar un Tatana nuevo.
- Actualizar el servidor hoy es `--reparar` con un paquete nuevo: sin backup previo ni forma de volver atrás.

### Qué es nuevo
1. **Backup programado** en el servidor (diario, hora configurable, retención, aviso de espacio, copia opcional a
   un disco USB), que se pone al día si la PC estuvo apagada y deja un estado consultable.
2. **`actualizar.sh`** en el paquete de la fiscalía: backup previo obligatorio, imágenes nuevas, verificación de
   salud, vuelta atrás automática de los contenedores, y publicación de la imagen de Tatana en Docker en el canal
   del servidor.
3. **Canal de actualización en el servidor** (`https://factum.internal/tatana/...`) con un producto nuevo
   `tatana-docker` firmado con la **misma clave Ed25519**.
4. **Actualizador en cada PC de perito**: consulta periódica por la red local, descarga, verifica firma y hash,
   cambia el contenedor solo si Tatana está libre y vuelve a la versión anterior si la nueva no levanta.
5. Forma de **saber qué versión tiene cada PC** (D5) y de **sumar el actualizador a las PCs ya instaladas** (D6).

---

## Criterios de aceptación

```gherkin
Feature: Backups programados en el servidor de la fiscalía

  Background:
    Given Factum instalado con el plan A en /srv/factum-fiscalia

  Scenario: Backup diario a la hora configurada
    Given el backup programado a las <hora> (D11)
    When llega esa hora
    Then se genera un backup en /srv/factum-fiscalia/backups con base, evidencia, configuración, CA de Caddy, versión y SHA256SUMS
    And queda registrado el resultado (fecha, duración, tamaño, ok/error) donde lo lee diagnostico.sh (y la web si D15 lo pide)

  Scenario: Cambiar la hora y la retención
    When el usuario cambia la hora o la cantidad de backups a conservar con el comando que define la SDD
    Then el próximo backup corre a la hora nueva sin reinstalar Factum
    And la retención nueva se aplica al terminar el próximo backup

  Scenario: La PC estaba apagada a la hora del backup
    Given el servidor apagado a la hora programada
    When se prende
    Then el backup se hace según D11 (al prender o en la próxima ventana), nunca se pierde el día en silencio

  Scenario: Retención
    Given <N> backups locales correctos y uno nuevo correcto (D12)
    Then se conservan los <N> más nuevos
    And nunca se borra un backup viejo si el nuevo falló

  Scenario: No alcanza el espacio
    Given el disco de backups sin lugar para el próximo backup
    When se va a hacer el backup
    Then no se empieza ni se pausa la web
    And queda un aviso visible (D15) que dice cuánto hace falta y cuánto queda
    And los backups existentes no se tocan

  Scenario: Poco espacio, todavía alcanza
    Given que después del backup quedaría menos del umbral (D12)
    Then el backup se hace y queda un aviso de "espacio bajo"

  Scenario: Copia a disco USB enchufado
    Given un disco USB reconocido como disco de backups de Factum (D14) enchufado
    When termina un backup local correcto
    Then se copia al disco, se verifica su SHA256SUMS en el disco y se desmonta de forma segura
    And en el disco se aplica la retención de D14

  Scenario: Sin disco USB
    Given ningún disco de backups enchufado
    Then el backup local se hace igual y el estado dice "sin copia externa desde <fecha>"

  Scenario: Falla el backup
    When el backup falla (base caída, error de disco, USB desenchufado a mitad)
    Then la web y la API vuelven a quedar levantadas
    And no queda una carpeta a medias con nombre de backup válido
    And el fallo queda visible según D15

  Scenario: No pisa una actualización
    Given actualizar.sh corriendo
    When llega la hora del backup
    Then el backup espera o se saltea y lo deja registrado; nunca corren los dos a la vez

  Scenario: Restaurar un backup programado
    Given un backup hecho por el programador (local o del disco USB, cifrado o no según D13)
    When se corre restaurar.sh --desde <backup>
    Then restaura igual que un backup manual de hoy (base, evidencia, appsettings.Local.json, branding)

Feature: Actualizar Factum en el servidor con un solo comando

  Scenario: Actualización correcta
    Given el servidor con Factum vN y el paquete Factum-Fiscalia-vN+1 copiado al servidor (D10)
    When el usuario corre sudo bash actualizar.sh desde el paquete
    Then verifica el SHA256SUMS de todo el paquete antes de tocar nada
    And hace un backup completo (no se puede saltear salvo con un flag explícito, D9)
    And carga las imágenes nuevas y conserva las de vN para volver atrás
    And levanta vN+1, espera salud de backend, frontend y Caddy, y prueba https://factum.internal/health
    And conserva config/.env, appsettings.Local.json, branding, modo sin AVX y datos
    And publica la imagen de Tatana en Docker del paquete en el canal del servidor (si es más nueva que la publicada)
    And muestra la versión de Factum y de Tatana publicadas

  Scenario: La versión nueva no levanta
    When vN+1 no queda sana en el tiempo límite
    Then vuelve a levantar las imágenes de vN con la misma configuración
    And no publica la versión nueva de Tatana
    And dice qué pasó, dónde está el backup previo y cómo restaurar la base si hiciera falta (D9)

  Scenario: Paquete dañado o de otro plan
    Given un paquete con SHA256SUMS que no coincide, o de una versión menor que la instalada
    Then actualizar.sh no toca nada y lo explica (una versión menor solo con un flag explícito, D9)

Feature: Tatana en Docker se actualiza desde el servidor de la fiscalía

  Background:
    Given una PC de perito con Tatana en Docker vN, el actualizador instalado y factum.internal resolviendo al servidor
    And el certificado de la CA de la fiscalía instalado en la PC (preparar-pc-perito.sh)

  Scenario: Hay versión nueva y Tatana está libre
    Given el servidor publica tatana-docker vN+1
    When el actualizador consulta (cada <intervalo>, D1)
    Then baja el manifiesto, verifica la firma Ed25519 con la clave pública de Tatana y que el producto sea tatana-docker
    And baja la imagen solo si vN+1 > vN, verifica tamaño y SHA-256 contra el manifiesto firmado, y recién ahí la carga
    And con Tatana libre (D3) pide modo mantenimiento, recrea el contenedor con la imagen nueva y los mismos datos, orígenes, puerto y USB
    And /health responde vN+1 en menos de 2 minutos
    And la evidencia, agent-data, los pareos del iPhone y appsettings.Local.json siguen iguales

  Scenario: Tatana está capturando
    Given una captura, grabación, armado de ZIP o sesión de grabación en curso (/agent/state)
    When hay una versión nueva descargada y verificada
    Then el contenedor no se toca
    And se reintenta en la próxima consulta hasta que esté libre

  Scenario: Firma o hash inválidos
    Given un manifiesto con firma inválida, de otro producto, o una imagen cuyo SHA-256 no coincide
    Then se descarta, no se carga nada en Docker y Tatana sigue en vN
    And el motivo queda en el registro del actualizador

  Scenario: La versión nueva no arranca
    Given la imagen vN+1 cargada y el contenedor recreado
    When /health no responde vN+1 en el tiempo límite
    Then se vuelve a crear el contenedor con la imagen vN y la misma configuración
    And esa versión queda marcada como fallida en esa PC para no reintentarla en cada consulta (D4)

  Scenario: Servidor caído o sin red
    When el actualizador no llega a https://factum.internal
    Then Tatana sigue funcionando en vN sin ningún efecto visible para el perito

  Scenario: Nunca baja de versión
    Given el servidor publica una versión menor o igual a la instalada
    Then no se instala

  Scenario: Poco disco en la PC
    Given menos espacio libre que el tamaño de la imagen más un margen
    Then no se descarga y queda el motivo en el registro

  Scenario: Saber qué versión tiene cada PC
    When el usuario consulta el estado según D5
    Then ve por cada PC (nombre del equipo) la versión de Tatana, la última consulta y el último error si lo hubo

  Scenario: Sumar el actualizador a una PC ya instalada
    Given una PC con Tatana en Docker instalado el 2026-10-20 (sin actualizador)
    When se aplica el procedimiento de D6
    Then queda con el actualizador, sin reinstalar desde cero ni perder datos, evidencia, pareos ni configuración

  Scenario: Estado local
    When el perito o el usuario corre estado-tatana-docker.sh
    Then además de lo de hoy muestra versión instalada, servidor de actualizaciones, última consulta, versión disponible y último error

  Scenario: Desinstalar
    When se corre desinstalar-tatana-docker.sh
    Then también se quita el actualizador, y la evidencia sigue intacta
```

---

## Datos que se registran

**Configuración del backup en el servidor** (`config/.env` o archivo propio, lo define la SDD)
| Dato | Obligatorio | Uso |
|---|---|---|
| Hora del backup diario (`HH:MM`) | Sí (default D11) | Programar el timer. |
| Backups locales a conservar | Sí (default D12) | Retención en `backups/`. |
| Umbral de espacio bajo | Sí (default D12) | Aviso antes de que se llene. |
| Disco USB: identificación (etiqueta) y retención en el disco | Solo si se usa USB (D14) | Reconocer el disco y no llenarlo. |
| Contraseña/clave de cifrado | Solo si D13 lo pide | Cifrar la copia; **no** se guarda junto al backup. |

**Estado del último backup** (archivo de estado en la instalación, sin secretos)
| Dato | Obligatorio | Uso |
|---|---|---|
| Inicio, fin, resultado (`ok`/`error`/`salteado`), motivo | Sí | `diagnostico.sh`, aviso (D15). |
| Nombre y tamaño del backup, espacio libre después | Sí | Diagnóstico y aviso de espacio. |
| Copia USB: hecha / no / error, fecha de la última copia externa correcta | Sí | "Sin copia externa desde…". |

**Manifiesto `tatana-docker`** (mismo sobre firmado de `ops/tatana/`, producto nuevo)
| Dato | Obligatorio | Uso |
|---|---|---|
| `product = "tatana-docker"`, versión semver | Sí | No mezclar con Windows/Linux; sin downgrade. |
| Archivo de imagen (`docker save` comprimido), tamaño, SHA-256 | Sí | Descarga e integridad. |
| Nombre:tag de la imagen | Sí | Comprobar que lo cargado es lo firmado. |
| Fecha, notas | No | Registro. |

**En cada PC de perito**
| Dato | Obligatorio | Uso |
|---|---|---|
| URL del canal (`https://factum.internal/tatana/...`) e intervalo | Sí | Configuración del actualizador. |
| Datos para recrear el contenedor (los de `instalacion.env` + uid/gid/rutas del perito) | Sí | Hoy los tiene solo el instalador en memoria; el actualizador corre sin el perito delante. |
| Versión instalada, imagen anterior (para volver atrás), versiones fallidas | Sí | Rollback y no reintentar en bucle. |
| Última consulta, resultado, motivo de descarte | Sí | `estado-tatana-docker.sh` y D5. |

**En el servidor, por PC** (según D5): nombre del equipo, versión de Tatana, fecha de la última consulta, último
error. **Sin cambios de modelo en Mongo** salvo que D5/D15 elijan la web.

---

## Diseño UX/UI

Es sobre todo **operación por terminal** (scripts en castellano, mismo estilo `OK / AVISO / ERROR` que los
instaladores actuales). La web (`client/`) solo cambia si D5 o D15 lo eligen. `agent-ui/` solo si D7.

### Servidor (terminal)
- **`actualizar.sh`**: pasos numerados como el instalador (`[1/7] Paquete`, `[2/7] Backup previo`, …). Al final:
  "Factum vX.Y.Z funcionando. Tatana en Docker vA.B.C publicado: las PCs se actualizan solas en las próximas
  <intervalo> mientras no estén capturando." En falla: qué paso falló, "se volvió a vN", ruta del backup previo y
  el comando exacto de `restaurar.sh`.
- **Backup programado**: comando para ver/cambiar hora y retención (p. ej. `backup-programado.sh --hora 21:30
  --conservar 7`, `--estado`, `--desactivar`); el nombre exacto lo define la SDD.
- **`diagnostico.sh`** suma: próximo backup, último resultado, espacio libre en `backups/` y en el USB, última
  copia externa, versión de Tatana publicada y (si D5 = B) la lista de PCs.

### PC del perito (terminal)
- El perito no ve nada: no hay ventana de Tatana en Docker. Durante el cambio de contenedor la web muestra
  unos segundos "Tatana no está disponible" y se reconecta sola (comportamiento actual).
- **`estado-tatana-docker.sh`** suma un bloque "Actualizaciones": servidor, versión instalada/disponible, última
  consulta, último error ("firma inválida", "sin espacio", "esperando a que termine la captura").

### Web (`client/`), solo si D5 = C o D15 = B
- Sección de **Administración** visible solo para superadmin, de solo lectura: "Último backup: hoy 21:00 · OK ·
  12 GB · copia USB: hace 3 días" con aviso destacado si falló, si hay poco espacio o si no hay copia externa hace
  más de N días; y, si D5 = C, tabla de PCs (equipo, versión de Tatana, última consulta, error).
- Estados: sin datos todavía ("Todavía no corrió ningún backup programado"), error, espacio bajo.

### Guías
- `docs/instalacion-fiscalia.md`: §8 reescrita (backup programado, USB, cifrado, restaurar), §9.6 reescrita (canal
  del servidor), sección nueva "Actualizar Factum y Tatana" y checklist para la visita de D6.
- `docs/tatana-docker.md`: §4 "Sin actualizaciones automáticas" pasa a describir el actualizador.

---

## Fuera de alcance
- Backups **fuera de la fiscalía** (nube, R2, otro servidor por red) y replicación/alta disponibilidad.
- Backups de las **PCs de perito** (la evidencia ya se sube al servidor; `~/Factum/Evidencia` local no se respalda).
- Avisos por **correo, WhatsApp o Telegram** (no hay SMTP ni internet confiable), salvo que D15 lo pida.
- Que el servidor de la fiscalía **baje solo** versiones nuevas de Factum de internet: el paquete se sigue llevando
  (o copiando) a mano (D10).
- Actualizar Docker Engine, el sistema operativo o la CA de Caddy de las PCs.
- Actualizar Tatana en **Windows** desde el servidor de la fiscalía.
- Un sistema formal de **migraciones de base**; si una versión futura necesita una, la trae su propia HU.
- Imágenes por capas o descargas diferenciales (se baja la imagen completa por la red local).
- Rotación de la clave Ed25519 de Tatana.

## Notas de implementación (mínimas; el detalle es de la SDD)
- **Firma:** reusar el sobre y las reglas de `ops/tatana/_lib.mjs` con un producto nuevo `tatana-docker`; la
  verificación en la PC no puede depender de Node/Python del host (Ubuntu ≤ 20.04 trae OpenSSL 1.1.1, que no
  verifica Ed25519 sobre datos crudos con `pkeyutl`). Opción natural: verificar con el Python de la **imagen ya
  instalada** (la vieja, la confiable), como hoy `instalar-tatana-docker.sh` usa `hornear-origenes-tatana.py`, y
  con la clave pública horneada en esa imagen/paquete.
- **Recrear el contenedor:** el `docker run` hoy vive solo en `instalar-tatana-docker.sh`. Hay que extraerlo a
  `comun.sh` (una sola fuente) y persistir lo que falte (uid, gid, grupos, rutas de datos/evidencia, zona horaria)
  para que el actualizador lo recree igual sin el perito delante. El contenedor sigue con la etiqueta
  `factum.tatana-docker=1`; uno ajeno no se toca.
- **Libre** = `GET /agent/state` sin `operations`, sin `recording_session` activa y `POST /agent/maintenance` → 200.
  El 409 `agent_busy` ya resuelve la carrera "empezó una captura justo ahora". Decidir en D3 si
  `background_operations` (posproceso de video, que el mantenimiento cancela) también posterga.
- **Canal en Caddy:** volver a habilitar una ruta estática solo para el canal de Tatana (archivos servidos desde
  `/srv/factum-fiscalia/...`), sin `/descargas` público si no hace falta. La PC lo pide por `https://factum.internal`
  con la CA ya instalada por `preparar-pc-perito.sh` (el actualizador corre como root: usa el almacén del sistema,
  o la copia del `.crt` si no está).
- **Concurrencia en el servidor:** backup programado, backup manual y `actualizar.sh` con un mismo candado
  (`flock`).
- `backup.sh` ya escribe en `.parcial` y renombra: mantenerlo; la retención tiene que correr **después** de un
  backup correcto.
- Versiones: la de Factum y la de Tatana son independientes; el paquete de la fiscalía lleva las dos (hoy la de
  Tatana en Docker sale de `armar-tatana-docker.sh --version`).
- Probar en una VM o contenedor Ubuntu 20.04 con systemd (`--autoprueba` del actualizador con `--mock`); con
  celular real queda para la próxima visita a la fiscalía.

---

## Dudas para validar con el usuario

**D1. Cada cuánto consultan las PCs al servidor.**
- A) Cada **30 minutos**, con un primer chequeo ~5 min después de prender la PC y un desfase aleatorio por PC para
  que no bajen todas la imagen al mismo tiempo.
- B) Cada 6 h, como el Tatana de Windows.
- C) Solo al prender la PC.
- **Recomendada: A.** Es red local: el manifiesto pesa unos KB y la imagen se baja una sola vez por versión. Con
  6 h, una corrección urgente puede tardar un día laboral; con "solo al prender", PCs que nunca se apagan no
  actualizan nunca. **Pregunta:** ¿las PCs de perito se apagan a la noche o quedan prendidas?

**D2. Dónde corre el chequeo en la PC del perito.**
- A) **Timer de systemd del host** (servicio + timer instalados por el script, como root), que usa el `docker` del
  host y `curl`.
- B) Contenedor "acompañante" (sidecar) con el socket de Docker montado.
- C) Dentro del propio contenedor de Tatana, con el socket de Docker montado.
- **Recomendada: A.** El Docker del paquete ya exige systemd, así que está en todas las PCs; no necesita imágenes
  extra ni internet. B y C le dan el socket de Docker (equivalente a root del host) a un contenedor que habla con la
  red y con celulares, y C además tiene que reemplazarse a sí mismo (si la nueva falla, no queda nadie para volver
  atrás). Costo de A: un script bash que tiene que andar en `bash`/`curl` viejos (Ubuntu 18.04/20.04) — se prueba
  en una VM 20.04.

**D3. Cuándo se cambia el contenedor.**
- A) **Apenas esté libre**: sin operaciones, sin sesión de grabación activa y sin posproceso de video en segundo
  plano. La web pierde a Tatana unos segundos y se reconecta sola.
- B) Solo en una franja (p. ej. fuera de horario o en los primeros minutos después de prender la PC).
- C) Libre **y** sin celular conectado.
- **Recomendada: A** (con la condición extra del posproceso, porque entrar en mantenimiento lo cancela y habría que
  rehacerlo). B deja PCs sin actualizar si se prenden y apagan dentro del horario. C es más prudente pero un perito
  puede dejar el celular enchufado horas sin capturar. **Pregunta:** ¿te parece aceptable que, entre una captura y
  otra, la web diga "Tatana no está disponible" unos 10–30 segundos?

**D4. Vuelta atrás en la PC.**
- A) **Automática solo si la nueva no arranca** (no responde `/health` con la versión nueva en 2 min): vuelve a la
  imagen anterior, marca esa versión como fallida en esa PC y no la reintenta hasta que se publique otra mayor. Se
  conserva solo la imagen anterior (las más viejas se borran para no llenar el disco).
- B) A + un comando manual en la PC (`estado-tatana-docker.sh --volver-a-anterior`) para cuando arranca pero anda mal.
- C) Volver atrás "en todas las PCs" desde el servidor publicando la versión vieja como si fuera nueva.
- **Recomendada: B.** A cubre lo que se puede detectar solo; un Tatana que arranca pero graba mal solo lo detecta
  una persona, y tiene que haber una salida sin pendrive. C rompe la regla "nunca baja de versión" (que protege
  contra un servidor adulterado); para un rollback general se publica una versión mayor con el código anterior.

**D5. Cómo se sabe qué versión tiene cada PC.**
- A) Solo en cada PC (`estado-tatana-docker.sh`) y por el `agent_version` que ya queda en cada captura/informe.
- B) **El actualizador se identifica al consultar** (nombre de equipo + versión + último error, en la URL o en un
  encabezado) y el servidor lo saca del registro de Caddy con un script (`diagnostico.sh` / `estado-peritos.sh`):
  "PC-PERITO-3 · Tatana 1.4.0 · consultó hace 12 min".
- C) B + **tabla en la web** (Administración, superadmin), lo que obliga a tocar backend y `client/`.
- **Recomendada: B.** Da la foto completa sin cambiar el backend ni la web, y como todo pasa por el servidor no hay
  que ir a cada PC. C es más cómoda, pero suma una API y una pantalla para un dato que se mira poco; queda como HU
  aparte si hace falta. **Pregunta:** ¿quién lo va a mirar: vos (por SSH/visita) o alguien de la fiscalía sin
  terminal? Si es lo segundo, conviene C.

**D6. Cómo se suma el actualizador a las PCs ya instaladas.**
Las PCs instaladas el 2026-10-20 no tienen ningún mecanismo de actualización: **una visita (o un comando) por PC
es inevitable una vez**.
- A) **Desde el servidor, sin pendrive**: en cada PC, un solo comando que baja el paquete de Tatana en Docker
  nuevo desde `https://factum.internal/tatana/...` (verificando firma/SHA-256) y corre el instalador nuevo, que deja
  el actualizador. Conserva datos, evidencia, pareos y orígenes (reinstalar ya es seguro, T1).
- B) Pendrive con el paquete nuevo de Tatana en Docker y correr `instalar-tatana-docker.sh` en cada PC.
- C) Un script chiquito "agregar-actualizador.sh" que no reinstala la imagen.
- **Recomendada: A.** Evita el pendrive y el internet lento; la PC ya confía en la CA y resuelve `factum.internal`.
  C ahorra una descarga, pero deja la PC con una imagen que no tiene la clave pública/verificador que espera el
  actualizador (hay que reinstalar igual). **Pregunta:** ¿cuántas PCs son y alguien de la fiscalía puede correr un
  comando con `sudo`, o lo hacés vos en la próxima visita?

**D7. ¿El AppImage (PCs con distro nueva) también toma el canal del servidor?**
- A) No en esta HU: sigue como hoy (canal de la nube; §9.6).
- B) **Sí**: el AppImage que va en el paquete de la fiscalía se arma con `update_urls = [https://factum.internal/
  tatana/updates/]` y el servidor publica también el manifiesto `tatana-linux` y el AppImage.
- C) No, y además se apaga la actualización del AppImage en la fiscalía (solo se actualiza con el paquete).
- **Recomendada: B si hay o va a haber PCs con AppImage; si no hay ninguna, C.** Con A, un AppImage con internet
  se puede adelantar a una versión de la nube que el servidor de la fiscalía todavía no tiene (web vieja + Tatana
  nuevo), y otro sin internet no se actualiza nunca: el peor de los dos mundos. B toca `agent-ui` (un armado
  "fiscalía" con otras `update_urls`), por eso queda condicionada. **Pregunta:** ¿hoy hay alguna PC con el
  AppImage en la fiscalía?

**D8. Dónde se firma la imagen de Tatana en Docker.**
La clave privada vive solo en GitHub Actions (C2); el paquete de la fiscalía y la imagen Docker hoy se arman en
la Mac.
- A) **Un workflow de GitHub Actions** arma la imagen `linux/amd64` (nativo, más rápido que la emulación en la Mac),
  corre las autopruebas, firma el manifiesto `tatana-docker` y lo deja como artifact; `armar-paquete-fiscalia.sh`
  lo toma de ahí (como hoy toma el AppImage del CI).
- B) Firmar en la Mac con una copia de la clave privada.
- C) Una clave aparte solo para la fiscalía, guardada en la Mac.
- **Recomendada: A.** Respeta la decisión de "misma clave" sin sacarla de GitHub. B la expone en una laptop; C es
  otra clave que custodiar y hornear. **Costo:** minutos de CI por versión (runner Linux, no Windows: barato).

**D9. `actualizar.sh` del servidor: backup previo, vuelta atrás y base.**
- A) **Backup completo previo obligatorio** (con pausa; saltearlo solo con `--sin-backup-previo` y confirmación
  escrita). Si la versión nueva no queda sana: vuelve **automáticamente** a las imágenes anteriores; la **base no se
  restaura sola** (se muestra el comando de `restaurar.sh` con el backup previo). Se niega a instalar una versión
  menor salvo `--permitir-version-menor`.
- B) Igual, pero ante una falla también restaura la base del backup previo automáticamente.
- C) Sin backup previo obligatorio (el diario alcanza).
- **Recomendada: A.** Hoy el backend no tiene migraciones (solo crea índices al arrancar) y los cambios son
  aditivos: volver a las imágenes viejas alcanza casi siempre. Restaurar la base solo podría borrar trabajo cargado
  después del backup previo si la versión nueva llegó a funcionar un rato; eso lo decide una persona. C arriesga
  hasta 24 h de evidencia.

**D10. Cómo llega el paquete nuevo al servidor y quién corre `actualizar.sh`.**
- A) Vos, en persona, con pendrive (como la instalación).
- B) **Remoto** (SSH/VPN/AnyDesk de la fiscalía) copiando el paquete por la red.
- C) Alguien de la fiscalía, siguiendo una guía de una página.
- **Recomendada: la que sea real hoy — no la puedo inferir.** Cambia el diseño: con C los mensajes y la guía tienen
  que ser a prueba de no técnicos y `actualizar.sh` no puede hacer preguntas ambiguas; con B conviene que todo sea
  no interactivo (`--si`) y que deje un registro. **Pregunta:** ¿tenés o vas a tener acceso remoto al servidor?
  ¿Cuánto pesa razonable bajar por su internet (el paquete completo ronda varios GB)?

**D11. Hora por defecto, pausa de la web y PC apagada.**
El backup pausa la web y la API mientras copia la evidencia (minutos, crece con la evidencia).
- A) **Default 21:00 con pausa**; si a esa hora el servidor estaba apagado, **se hace al prender pero sin pausa**
  (copia "en caliente": base primero, después evidencia; puede quedar una evidencia subida durante el backup sin su
  registro en la base, lo que no rompe una restauración) y lo deja anotado.
- B) Si estaba apagado, se hace al prender **con pausa** (los peritos no pueden usar Factum unos minutos al
  arrancar el día).
- C) Si estaba apagado, se saltea y se avisa.
- **Recomendada: A.** Nunca pasa un día sin backup y nunca se corta el trabajo de los peritos en horario. **Pregunta:**
  ¿el servidor queda prendido de noche? ¿Cuál es el horario de trabajo de la fiscalía (para elegir la hora)?

**D12. Retención, espacio y formato de la evidencia.**
Hoy cada backup lleva **toda** la evidencia comprimida: con 10 backups, la evidencia ocupa ~10 veces en el mismo
disco, y la evidencia (ZIP de capturas, videos) ya viene comprimida, así que el `tar.gz` casi no ahorra.
- A) **Evidencia como copia incremental con enlaces duros** (cada backup se ve completo pero los archivos que no
  cambiaron no ocupan de nuevo) + base y config completos cada día. Retención por defecto: **7 diarios**. Aviso de
  espacio bajo con menos de **15 %** o menos de lo que ocupó el último backup.
- B) Formato actual (todo en `tar.gz`) con retención **3** por defecto.
- C) Formato actual con 10 (como hoy).
- **Recomendada: A.** Con evidencia forense que crece todos los días, C llena el disco en semanas y B deja poca
  historia. A cambia el formato del backup, así que `restaurar.sh` tiene que aceptar los dos (los manuales viejos
  siguen sirviendo). **Pregunta:** ¿cuánto disco tiene el servidor y cuánta evidencia por mes calculás?

**D13. Cifrado de los backups.** Hoy no van cifrados y llevan `config/.env` (secretos de JWT, base, etc.).
- A) Local sin cifrar (carpeta de root con permisos 700, mismo disco que los datos: cifrar ahí no protege de quien
  ya tiene el servidor) y **la copia al USB cifrada** con una contraseña que se elige al activar el USB (AES-256 con
  `openssl`, que está en cualquier Ubuntu) y **no** se guarda en el disco USB. Sin la contraseña, el backup del USB
  no se puede restaurar: la guía dice dónde anotarla.
- B) Nada cifrado (como hoy), pero sin `.env` en la copia USB.
- C) Todo cifrado.
- **Recomendada: A.** El que se puede perder o robar es el disco USB, y lleva evidencia de causas y los secretos.
  C no agrega seguridad real (la clave tendría que estar en el mismo servidor para que el backup programado
  funcione). B deja la evidencia en claro en un disco que sale de la fiscalía. **Pregunta:** ¿la fiscalía tiene una
  política sobre medios extraíbles o cifrado que haya que respetar?

**D14. Disco USB: cómo se reconoce y retención.**
- A) **Por etiqueta del sistema de archivos** (p. ej. `FACTUM-BACKUP`): un comando lo prepara una vez (formatea en
  ext4 con esa etiqueta, previa confirmación escrita). El backup lo monta solo si está enchufado, copia, verifica,
  aplica su propia retención (default 14) y lo desmonta. Cualquier otro pendrive se ignora.
- B) Cualquier disco montado en una ruta fija (`/media/...`).
- C) Usar el disco como venga (NTFS/exFAT) para poder leerlo en Windows.
- **Recomendada: A.** Evita copiar evidencia a un pendrive cualquiera que alguien dejó enchufado y no depende de
  que el servidor tenga escritorio con montaje automático. ext4 no se lee en Windows sin herramientas, pero el
  backup solo se restaura en Linux (plan A o B); exFAT en Ubuntu ≤ 20.04 necesita un paquete extra. **Pregunta:**
  ¿el disco USB va a quedar siempre enchufado o se rota (uno en la fiscalía y otro afuera)?

**D15. Cómo se entera alguien de que el backup falló o falta espacio.**
- A) Solo `diagnostico.sh` y un registro.
- B) **Aviso en la web para el superadmin** (banner en Administración/inicio) leyendo el archivo de estado del
  backup, que el backend recibe montado de solo lectura. Toca backend y `client/`.
- C) Mensaje en la terminal de quien entre por SSH (`/etc/motd`) + A.
- **Recomendada: B.** Un backup que falla en silencio es el peor caso, y en la fiscalía nadie va a abrir una
  terminal para mirar un registro; el superadmin sí entra a la web. Es una pantalla de solo lectura y chica. Si se
  elige D5 = C, la misma sección muestra las PCs. **Pregunta:** ¿el superadmin de la fiscalía es alguien de ahí que
  entra seguido?

**D16. ¿Esto aplica también al plan B (una sola PC)?**
- A) **Backup programado: sí** (mismo script, `/srv/factum-una-pc`). Actualizador de Tatana: no hace falta canal;
  `actualizar.sh` del plan B actualiza Factum y también el Tatana en Docker de esa misma PC (respetando "nunca
  durante una captura").
- B) Solo plan A en esta HU.
- **Recomendada: A.** Los scripts ya son comunes a los dos planes y el plan B es el respaldo si el A falla: tiene que
  poder operar igual. **Pregunta:** ¿hay hoy alguna instalación del plan B en uso?

## Validación del usuario (2026-10-20)

Respuestas del usuario: no hay PCs con AppImage (todas Tatana en Docker); el servidor se actualiza en persona o
remoto por **AnyDesk**; no se sabe si las PCs quedan prendidas de noche; son **mínimo 4 PCs de perito** y el
comando con `sudo` lo corre el usuario; disco del servidor desconocido; el superadmin es el usuario (los de la
fiscalía tienen otro usuario); **no hay plan B en uso**. Y: **"los backups no me parecen tan necesarios"**.

Decisiones resultantes (orquestador, con las recomendadas donde aplican):
- **Alcance reducido a la actualización.** Los backups programados (D11–D15) **salen de esta HU** (quedan para una
  HU aparte si se piden). Se mantiene solo el **backup previo obligatorio de `actualizar.sh`** (D9 = A), porque
  protege la actualización.
- D1 = A (cada 30 min + desfase; sirve esté o no prendida de noche). D2 = A (timer systemd del host). D3 = A
  (cambia apenas esté libre; 10–30 s sin Tatana entre capturas). D4 = B. D5 = B (lo mira el usuario por AnyDesk o
  en persona; sin pantalla en la web). D6 = A (un comando por PC desde el servidor, lo corre el usuario). D8 = A.
- D7: **no aplica** (no hay AppImage en la fiscalía): el AppImage queda como está, fuera de alcance.
- D9 = A. D10: **presencial o AnyDesk** → `actualizar.sh` usable de forma interactiva y también no interactiva
  (`--si`), con registro; el paquete de actualización tiene que ser **lo más chico posible** (solo las imágenes que
  cambian: backend, frontend y Tatana en Docker; sin Mongo, Caddy ni Docker si no cambiaron) porque por AnyDesk y
  con internet lento varios GB no son viables.
- D16 = B (solo plan A; no hay plan B en uso).
