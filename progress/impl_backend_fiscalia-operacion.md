# impl_backend — fiscalia-operacion (#95)

**Estado:** done (nivel 1 de §14.1 verde en la Mac; niveles 2 y 3 y la prueba del servidor quedan escritos para CI).
**Rama:** `feat/fiscalia-operacion` (sin commits ni push: los hace el orquestador).
**SDD:** `Refactorizaciones/fiscalia-operacion.md` §13 · **HU:** `docs/hu-fiscalia-operacion.md`.
Dudas validadas aplicadas: DU-1=A (release firmada desde `main` con tag `tatana-vX.Y.Z`; no promoví nada ni empujé
tags), DU-2=A (imagen completa solo cuando cambia Tatana), DU-3=A (`--imagenes-de 0.0.0-fiscalia-prueba` en la guía
de migración).

No se tocó `server/src`, `client/` ni `agent-ui/` (solo se lee `agent-ui/src/main/updater/trusted-keys.ts`). No se
tocó la MongoDB de desarrollo ni `Storage:DataDirectory`. No se movieron tarjetas ni se tocó `progress/sesiones/`.

## Archivos

### Firma (`ops/tatana/`)
| Archivo | Cambio |
|---|---|
| `_lib.mjs` | Producto `tatana-docker` en `PRODUCTS`; `expectedFile` → `imagen-tatana-docker-v<v>.tar.gz`; `expectedKitFile`, `expectedImage`; `payloadError` valida `image` y `kit` solo para docker (en los otros productos esos campos son error); `notes` ≤ 2000. |
| `armar-payload.mjs` | Modo docker: `--archivo` (imagen), `--kit`, `--imagen`; sin `--latest` (y se niega si viene). Calcula size/sha256/sha512 de la imagen y size/sha256 del kit. Orden de errores de Windows/Linux sin cambios. |
| `verificar-manifiesto.mjs` | `--producto tatana-docker` y `--kit` (nombre, tamaño y SHA-256). |
| `claves-confiables.mjs` | **Nuevo.** Genera `claves-confiables.txt` desde `trusted-keys.ts` con `readTrustedKeys` (una sola fuente), `--clave-extra` para ensayo, `--verificar <archivo>` (exactamente las claves de trusted-keys; lo usa la release). |
| `probar-firma.mjs` | Casos §5.2: ida y vuelta docker, cruzados docker↔linux/windows, image con otro tag/nombre, file con otro nombre, kit alterado/raro/sin kit, linux con kit, imagen alterada, y `claves-confiables.mjs`. `firmar-manifiesto.mjs` sin cambios (el producto sale del payload). |

### Kit de Tatana en Docker (`deploy/tatana-docker/`)
| Archivo | Cambio |
|---|---|
| `paquete/comun.sh` | Rutas del actualizador (§7.1, sobreescribibles solo para pruebas con `FACTUM_TD_*`); `clave_de`, `leer_instalacion_root` (lista blanca + regex §10.5; `UID` → variable `T_UID` porque `UID` es de solo lectura en bash), `armar_run` (el `docker run` que estaba en línea en el instalador, parametrizado), `run_completo`, `run_completo_de_kit` (arma el run con el `comun.sh` de OTRO kit en un bash aparte, argumentos por NUL), `version_cmp` (semver, bash ≥ 3.2), `tomar_candado` (flock fd de solo lectura), `estado_agente`, `esperar_version`, `cambiar_contenedor` (§7.6, códigos 0/2/3/4/5/6), `texto_resultado`. |
| `paquete/estado-agente.py` | **Nuevo** (§10.4): estado/mantenimiento/salir-mantenimiento/version, sin Origin ni Sec-Fetch-Site, sin proxy, `--fixture`, `--autoprueba` (10 fixtures + agente HTTP falso en 127.0.0.1, funciona con `--network none`). |
| `paquete/verificar-manifiesto-docker.py` | **Nuevo** (§10.3): réplica de `verifyEnvelopeText`/`payloadError` (base64 estricto, Ed25519 con `cryptography`, BOM, `Number.isSafeInteger`), salida `CLAVE=valor`, códigos 0/1/2, `--version-instalada` → `COMPARACION`, `--comparar A B` (pruebas), `--autoprueba` (clave efímera, 42 chequeos). `cryptography` se importa recién al verificar. |
| `paquete/actualizador-tatana-docker.sh` | **Nuevo** (§7.4–7.7): `--una-vez` (candado, config, recuperación de cambio a medias, versión instalada, consulta con query §10.2 vía curl de la imagen + `--resolve`, verificación con kit/claves/Python INSTALADOS y `--network none`, nunca baja, fallidas, espacio, descarga reanudable con `.parcial` y SHA-256, carga + autoprueba solo con Tatana libre, kit nuevo, cambio con `armar_run` del kit nuevo, units si cambiaron, limpieza actual+anterior solo de `imagenes.txt`, HEAD con el resultado final), `--volver-a-anterior`, `--buscar-ahora`. Log propio con rotación > 1 MiB. |
| `paquete/systemd/factum-tatana-actualizador.{service,timer}` | **Nuevos**, tal cual §7.2. |
| `paquete/sumar-actualizador.sh` | **Nuevo** (§9.2), autocontenido (se publica suelto): baja con wget/curl/curl de la imagen, verifica con el Python de la imagen instalada (o la recién bajada si es PC nueva, avisando), arma la carpeta y corre `instalar-tatana-docker.sh --si` sin `--origen`, primera consulta y bloque "Actualizaciones". |
| `paquete/instalar-tatana-docker.sh` | Conserva `ORIGENES` sin `--origen`; candado; `[6/8]` con `cambiar_contenedor` (respeta "libre", `--forzar` con confirmación escrita o `TATANA_CONFIRMO_FORZAR=1` sin terminal); `[8/8]` actualizador (`--actualizador <url>`, `--sin-actualizador`; default si los orígenes tienen `https://factum.internal`); claves nuevas de `instalacion.env` (§10.5) en las dos copias; registra imagen nueva y previa en `imagenes.txt` y `estado.env`. |
| `paquete/estado-tatana-docker.sh` | Bloque "Actualizaciones" (§7.8, sin sudo), `--actualizaciones`, `--volver-a-anterior`, `--buscar-ahora`. |
| `paquete/desinstalar-tatana-docker.sh` | Quita timer/units/`/opt|/etc|/var/lib|/var/log` propios y `tatana-docker-anterior`; `--borrar-imagen` también las de `imagenes.txt`; candado. |
| `armar-tatana-docker.sh` | Kit `kit-tatana-docker-vX.tar.gz` (carpeta sin la imagen), `--canal <dir>`, `claves-confiables.txt` vía `claves-confiables.mjs`, `--clave-extra`, `systemd/` y `.py` en el paquete, `SHA256SUMS` recursivo, autopruebas de los dos `.py` con el Python de la imagen. |
| `paquete/LEEME.txt` | Actualizador, `sumar-actualizador.sh`, comandos nuevos. |

### Servidor (`deploy/fiscalia/`)
| Archivo | Cambio |
|---|---|
| `servidor/Caddyfile` | `handle_path /tatana/*` con `Cache-Control: no-cache` en el manifiesto, `log` JSON a `/var/log/caddy/tatana.log` (5 × 5 MiB) y `log_skip` en backend y frontend. |
| `servidor/compose.servidor.yml` | Volúmenes `canal-tatana` (ro) y `caddy-logs`. |
| `comun/_comun.sh` | `version_cmp` (bash 3.2), `tomar_candado` (`<carpeta>/.operacion.lock`, herencia con `FACTUM_CANDADO_HEREDADO`), `copiar_archivos_instalacion`, `version_de_manifiesto`, `publicar_canal_tatana` (`.parcial` + mv, manifiesto último, nunca igual/menor, quedan publicada + anterior). |
| `comun/instalar.sh` | Usa `copiar_archivos_instalacion`; crea `canal-tatana/docker` (755) y `datos/caddy-logs` (700); publica el canal si el paquete trae `tatana-docker/`; candado en `--reparar`. |
| `comun/actualizar.sh` | **Nuevo** (§8.3–8.5): `[1/8]`…`[8/8]`, registro en `registro/actualizar-*.log` (600), `--si`, `--sin-backup-previo` (SIN BACKUP / `FACTUM_CONFIRMO_SIN_BACKUP=1`), `--permitir-version-menor` (MENOR), `--solo-tatana`, modo solo-Tatana automático, requisitos antes de tocar nada, backup previo con candado heredado, `--imagenes-de`, `actualizacion-anterior/`, vuelta atrás automática (también con `trap` si se corta a mitad), limpieza de imágenes `factum-*`. `FACTUM_ESPERA_SANO` (solo pruebas) baja los 300 s. |
| `comun/backup.sh`, `comun/restaurar.sh` | Solo el candado (con herencia). |
| `comun/estado-peritos.sh` | **Nuevo** (§8.6): awk/sed sobre el log JSON de Caddy (también `.gz` rotados y `&`), última consulta por equipo+IP, `(!)` > 2 h, `--horas`, `--resumen`, `--registro` (pruebas). |
| `comun/diagnostico.sh` | Sección "Tatana en Docker (canal)" (publicada, PCs vistas en 24 h, desactualizadas). |
| `armar-paquete-fiscalia.sh` | `--actualizacion`, `--imagenes-de`, `--tatana-docker-release` (gh release download), `--tatana-docker-canal`, `--incluir-base`, `--clave-extra` (pruebas); imágenes `.tar.gz` (verificadas antes de comprimir); layout §8.2; el paquete completo también acepta Tatana en Docker; `--deploy-de-trabajo` también copia `ops/tatana/*.mjs`. El `rm -rf` del paso 6 quedó con `${VAR:?}`. |
| `LEEME-actualizacion.txt` | **Nuevo** (va como `LEEME.txt` del paquete de actualización). |

### CI
| Archivo | Qué |
|---|---|
| `.github/workflows/tatana-docker.yml` | **Nuevo** (§5.3): preparar (copia de la lógica de `tatana-release.yml`, incluida la verificación de `TATANA_FIRMA_KEY_ID` en trusted-keys) → armar (lock, `armar-tatana-linux.sh` desde `git archive`, `armar-tatana-docker.sh --canal`; ensayo: clave efímera, firma y artifact `clave-ensayo-publica`; release: `claves-confiables.mjs --verificar`) → firmar (environment `tatana-release`) → prueba (nivel 2) y prueba-2004 (nivel 3) → publicar (5 archivos al Release, `--clobber`). Actions fijadas por SHA, valores por `env:`. |
| `.github/workflows/fiscalia.yml` | **Nuevo**: `bash -n` + shellcheck de `deploy/fiscalia`, y `ci/fiscalia/prueba-actualizar.sh`. |
| `.github/workflows/verificar.yml` | shellcheck de los scripts nuevos; autopruebas de los `.py`, `probar-versiones.sh` y `prueba-cruzada.sh`. |
| `ci/tatana-docker/versiones.txt` | Tabla compartida §10.8 (los 8 casos de la SDD + 10 de semver.org/fiscalía). |
| `ci/tatana-docker/probar-versiones.sh` | La tabla contra `version_cmp` de la PC, `version_cmp` del servidor y el Python. |
| `ci/tatana-docker/prueba-cruzada.sh` | Prueba cruzada obligatoria §10.1 (Node firma → Python verifica; alterados, mismo motivo), con `--imagen` para el Python de la imagen. |
| `ci/tatana-docker/_prueba-lib.sh` | Funciones de los niveles 2 y 3 (Caddy real, CA con `preparar-pc-perito.sh`, publicar, firmar, imágenes re-etiquetadas, kits por versión). |
| `ci/tatana-docker/prueba-actualizador.sh` | Nivel 2 (§14.2), todos los casos de la SDD. |
| `ci/tatana-docker/prueba-2004.sh` | Nivel 3: `ubuntu:20.04` con systemd PID 1, privilegiado **solo en CI**, `systemd-binfmt` enmascarado, socket y rutas 1:1, Docker estático con el SHA de `armar-paquete-fiscalia.sh`. |
| `ci/fiscalia/prueba-actualizar.sh` | §14.3: los 7 casos (0.0.2, 0.0.3 rota, dañado, menor, `--imagenes-de`, solo Tatana, candado). |

### Docs
- `docs/tatana-docker.md`: §2 kit/canal/claves, §3 instalador nuevo + "Sumar el actualizador", §4 "Actualizaciones automáticas (plan A)" (cómo funciona, estado, volver, apagar, códigos), §5, §6 verificación nueva.
- `docs/instalacion-fiscalia.md`: §8 backup previo + candado, §9.6 reescrita, §11 nota, **§12 "Actualizar Factum y Tatana"** (armar, AnyDesk, `actualizar.sh`, vuelta atrás, `estado-peritos.sh`) con **12.5 checklist de la migración del 2026-10-20**.
- `docs/despliegue-nube.md` §19/§19.5/§20: el tag también dispara `tatana-docker.yml` y qué suma al Release.

## Verificación (nivel 1, Mac, sin privilegios)

| Comando | Resultado |
|---|---|
| `node ops/tatana/probar-firma.mjs` | `Todas las pruebas pasaron` (incluye 30 casos nuevos de docker y claves). |
| `python3 -I verificar-manifiesto-docker.py --autoprueba` / `estado-agente.py --autoprueba` (Python del host) | `RESULTADO=ok` / `RESULTADO=ok` |
| Las mismas con el Python de la imagen (amd64 emulado, `--network none`, sin `--privileged`) | `RESULTADO=ok` (42 y 29 chequeos); Python 3.11.17, cryptography 50.0.2. |
| `bash ci/tatana-docker/probar-versiones.sh` (bash 3.2 de la Mac) y con `--python` de la imagen | `Las 108 comparaciones dieron lo esperado` en los dos. |
| `bash ci/tatana-docker/prueba-cruzada.sh` y `--imagen factum-tatana-docker:0.0.2` | 9/9 casos coinciden Node ↔ Python en los dos. |
| `bash -n` de todos los scripts tocados/nuevos | OK. |
| shellcheck `-S warning -x` (imagen `koalaman/shellcheck:stable`, sin red) sobre todo lo nuevo/tocado + `ci/` | Sin hallazgos (los de los tests eran falsos positivos por `eval`: `disable` documentado). |
| actionlint (imagen `rhysd/actionlint`) de `tatana-docker.yml`, `fiscalia.yml`, `verificar.yml` | exit 0. |
| `caddy validate` con `caddy:2.11.7` | `Valid configuration`. |
| Caddy real (contenedor propio, puerto alto) con el Caddyfile | Manifiesto 200 + `Cache-Control: no-cache`; `/tatana/docker/` → 404 (sin listado); el log JSON trae la **URI original con la query** (`&` sin escapar); `/health` **no** se registra. |
| `estado-peritos.sh --registro` con ese log (+ un `.gz` rotado con `&`) | Lista equipo/IP/instalada/disponible/hace/resultado; `(!)` en la de hace 2 días; `--resumen` filtra por horas. |
| `armar-tatana-docker.sh --version 0.0.2 --agente-tar <agente de la imagen local> --canal --clave-extra` | OK en 1 min 27 s: autopruebas Linux/iOS y de los `.py` dentro de la imagen; **kit 44 KB**, imagen comprimida **289 MB**, paquete completo 289 MB; el kit no trae la imagen. |
| Payload + firma (clave efímera) + `verificar-manifiesto.mjs --producto tatana-docker --archivo --kit --payload` del canal | OK. |
| `armar-paquete-fiscalia.sh --actualizacion --version 0.0.2-prueba --imagenes-de 0.0.0-fiscalia-prueba --tatana-docker-canal … --deploy-de-trabajo` | OK: **289 MB** (todo Tatana; sin Tatana: `comun/` 88 KB + `servidor/` 8 KB, < 1 MB), 27 archivos, layout §8.2. Sin `--clave-extra` el canal **no entra** (`clave_desconocida`). |
| Instalador en la Mac (`--red-con-puertos --mock`, contenedor/puerto/datos propios, `--sin-actualizador`) | Instalación nueva OK; `instalacion.env` con las claves nuevas; **reinstalar sin `--origen` conserva `https://factum.internal`**; reemplazo con `cambiar_contenedor` y mantenimiento real contra el agente (200); con fixture "ocupado" corta y el contenedor queda intacto. |
| `cambiar_contenedor` con versión esperada que nunca responde | `rc=3`, vuelve **el mismo** contenedor (mismo ID) y responde. |
| `estado-tatana-docker.sh --actualizaciones` sin actualizador / `desinstalar --si` | Mensaje de "sumar-actualizador"; borra solo el contenedor de prueba; la evidencia sembrada queda. |

Limpieza en la Mac: borré solo lo mío (contenedores `fo-*`, la imagen `factum-tatana-docker:0.0.2`, las imágenes de
shellcheck y actionlint, y mis carpetas del scratchpad). No corrí nada privilegiado ni con systemd ni binfmt: la
emulación amd64 sigue andando (`uname -m` → `x86_64` al final). Posible resto: si `alpine:latest` no estaba, quedó
bajada por el último chequeo de emulación.

## No probado en la Mac (queda para CI y para el usuario)

- **`actualizar.sh` de punta a punta y `actualizador-tatana-docker.sh`**: necesitan root, systemd, `--network host`,
  flock y un plan A real. No lo corrí en la Mac a propósito: la limpieza de `actualizar.sh` borra los tags
  `factum-backend|frontend*` que no sean la versión nueva ni la anterior (en la Mac se llevaría `factum-backend:1.0.0`,
  `latest`, etc.). Por eso `ci/fiscalia/prueba-actualizar.sh` y los niveles 2/3 se niegan a correr fuera de CI
  (`GITHUB_ACTIONS=true` o `FACTUM_PRUEBA_VM_DESCARTABLE=1`).
- Qué espero de CI en el PR en borrador (§14.4): `verificar.yml` (shellcheck nuevo + autopruebas + versiones + cruzada),
  `tatana-docker.yml` en ensayo (armar → prueba nivel 2 → prueba-2004) y `fiscalia.yml`. Puntos con más riesgo de
  ajuste en el primer run: el contenedor de 20.04 con systemd (`--cgroupns=host` en cgroup v2 del runner), la
  plantilla `docker inspect` de `forma_contenedor`, y los tiempos (`no_arranca` espera 120 s + vuelta).
- §14.5 (usuario): PC Ubuntu 24.04 con celular real (captura durante una publicación, pareo del iPhone y adb después
  del cambio) y la migración en la fiscalía (§12.5 de la guía), que además necesita DU-1: promover `develop` → `main` y
  empujar `tatana-vX.Y.Z` (decisión del usuario; yo no lo hice).

## Decisiones no obvias

- **`UID` → `T_UID`/`T_GID`/`T_GIDS`** en bash (UID es de solo lectura); el archivo sigue usando las claves del contrato.
- **`run_completo_de_kit`**: el `docker run` del kit nuevo se arma en un `bash` aparte que carga el `comun.sh` de ese kit
  y devuelve los argumentos separados por NUL (no se mezclan funciones de dos versiones; D-T6).
- **`estado_agente` con `RED_CON_PUERTOS=1`** usa `--network container:tatana-docker` (en vez de `docker exec`): así
  el kit se monta igual que con red host y funciona en Docker Desktop (probado).
- **Agente que no responde** al pedir mantenimiento (HTTP=0): se cambia igual (no hay captura que proteger); cualquier
  otra respuesta rara → `error_agente` sin tocar nada.
- **`--volver-a-anterior` sin kit de la anterior** (las PCs migradas: `0.0.0-docker-prueba` no tenía kit): usa el
  `armar_run` del kit actual (el `docker run` es el mismo) y lo deja en el log. Espera "cualquier versión" al volver
  (las imágenes re-etiquetadas de las pruebas no responden con su etiqueta).
- **`requiere.txt`** admite una segunda columna `con-avx`/`sin-avx` para las de Mongo (si no, un servidor sin AVX
  exigiría `mongo:7` y uno con AVX `mongo:4.4`). Extiende §10.9 sin romperla (una imagen por línea).
- **Imágenes incluidas** en el paquete: `actualizar.sh` las lee de los `RepoTags` del `manifest.json` de cada
  `docker save` (no hay un índice aparte en el contrato).
- **Variables solo de prueba** (documentadas en el código): `FACTUM_TATANA_ESTADO_FIXTURE`,
  `FACTUM_TATANA_MARGEN_DISCO_MB` (§14.2), `FACTUM_TATANA_PRUEBA_CORTE=despues_rename` (corte a mitad, §14.2),
  `FACTUM_ESPERA_SANO` (baja los 300 s de `actualizar.sh` en CI), `FACTUM_TD_*` y `TATANA_DOCKER_CA_SISTEMA` (rutas).
- **Nivel 2 en release**: el kit publicado solo confía en trusted-keys; la prueba agrega su propia clave efímera a la
  base y, después de la actualización real, al kit instalado (solo en la VM) para las variantes (X+1, menor, etc.).
- La guía usa §12 en `instalacion-fiscalia.md` (al final, después de §11) para no renumerar las secciones existentes.
- `armar-paquete-fiscalia.sh` deja la carpeta del paquete a medias si falla la verificación de Tatana (la borra el
  próximo armado, como ya pasaba con cualquier error después del paso 6).

## Contrato (§10) — coincide con la SDD

- §10.1 manifiesto: sobre `schema,key_id,payload,signature`; payload `schema,product,version,released_at,commit,file,
  size,sha256,sha512,image,kit.file,kit.size,kit.sha256,notes` — mismos nombres en `_lib.mjs`, `armar-payload.mjs` y
  `verificar-manifiesto-docker.py`; prueba cruzada verde.
- §10.2 query `v=1&equipo&instalada&disponible&resultado`, saneada a `[A-Za-z0-9._-]` ≤ 64; GET y HEAD.
- §10.3 / §10.4 interfaces y claves de salida exactas (`VERSION IMAGEN ARCHIVO TAMANO SHA256 KIT_ARCHIVO KIT_TAMANO
  KIT_SHA256 KEY_ID COMPARACION` / `MOTIVO DETALLE`; `LIBRE MOTIVOS VERSION INALCANZABLE MANTENIMIENTO HTTP`); campos de
  `/agent/state` leídos tal como los serializa `AgentStateController` (`busy, maintenance, operations[].kind,
  background_operations[].kind, recordings, recording_session, unclaimed_recordings, version`).
- §10.5 `instalacion.env` (`PUERTO IMAGEN ORIGENES MOCK USUARIO UID GID GIDS DATOS EVIDENCIA ZONA RED_CON_PUERTOS`),
  §10.6 `actualizador.env` (`CANAL_URL CA HABILITADO`), §10.7 `estado.env` y los 20 códigos, §10.8 tabla en
  `ci/tatana-docker/versiones.txt`, §10.9 layout del paquete (con la extensión de `requiere.txt` de arriba), §10.10
  rutas del canal.

## Otros archivos en el árbol que NO son míos

`progress/sesiones/senkuch4n.md` (modificado), `.claude/skills/graphify/`, `docs/aplicacion-fredi-aprile.pptx`,
`docs/mp-informatica/`, `docs/mp_info_pedido/`, `docs/spike-extraccion-logica.md`, `Refactorizaciones/fiscalia-operacion.md`
y `docs/hu-fiscalia-operacion.md` (del arnés). No los toqué.

## Reintento 1 (review `progress/review_fiscalia-operacion.md`, hallazgo bloqueante 1)

**Problema:** `estado-agente.py` respondía `HTTP=0` ante cualquier `OSError` (incluido el timeout de 6 s), y
`cambiar_contenedor` toma `HTTP=0` como "agente apagado, nada que proteger" → un agente lento en medio de una captura
podía terminar con el contenedor bajado.

**Cambios:**
- `deploy/tatana-docker/paquete/estado-agente.py`: `conexion_rechazada(e)` (solo `ConnectionRefusedError`, o un
  `URLError` cuya `reason` lo sea) y `motivo_error(e)`.
  - `mantenimiento`: `HTTP=0` **solo** con conexión rechazada. Timeout, reset o cualquier otro error → `HTTP=-1` (y una
    respuesta HTTP rara sigue dando su código) → `cambiar_contenedor` devuelve 5 (`error_agente`) sin tocar nada.
  - `estado`: mismo criterio. Conexión rechazada → exit 3 + `INALCANZABLE=1`. Timeout/reset/HTTP ≠ 200/JSON roto →
    exit 4 + `ERROR_AGENTE=<timeout|reset|respuesta_invalida|…>` (antes era `INALCANZABLE=1`). Extiende §10.4 con esa
    clave nueva (documentada en la cabecera del script).
  - `version` queda igual (solo se usa para esperar a que el contenedor nuevo responda).
  - Autoprueba: casos nuevos con un socket que acepta y no contesta (timeout → `HTTP=-1`, `ERROR_AGENTE=timeout`,
    nunca `HTTP=0`), uno que corta la conexión (reset → `HTTP=-1`, `ERROR_AGENTE=reset`) y conexión rechazada → `HTTP=0`.
- `deploy/tatana-docker/paquete/actualizador-tatana-docker.sh`: en el paso 7, `ERROR_AGENTE` → `error_agente` (antes
  hubiera caído en `esperando_libre`). Además (observación no bloqueante del review) la recuperación espera 90 s en vez
  de 30 s antes de descartar el contenedor nuevo al reiniciar la PC.
- `deploy/tatana-docker/paquete/comun.sh`: comentario y registro de `cambiar_contenedor` dicen explícitamente "solo con
  conexión rechazada"; la lógica ya devolvía 5 para todo lo que no sea `HTTP=0`.
- `deploy/tatana-docker/paquete/instalar-tatana-docker.sh`: el mensaje del código 5 explica que Tatana puede estar
  capturando y que no se cambió nada.
- `ci/tatana-docker/probar-cambiar-contenedor.sh` **nuevo** (sin Docker ni red, con un `docker` doble que registra las
  llamadas): `HTTP=-1`, `HTTP=500` y salida vacía → rc 5 **sin** stop/rename/run/rm; 409 → rc 2 sin cambios; 200 y
  `HTTP=0` → cambia. Y uno de punta a punta con el `estado-agente.py` **real** contra un socket que acepta y nunca
  contesta → rc 5, el contenedor no se toca. Se suma a `verificar.yml`.

**Verificación:**
| Comando | Resultado |
|---|---|
| `python3 -I estado-agente.py --autoprueba` (host) | `RESULTADO=ok` (3,7 s; casos nuevos verdes) |
| Lo mismo con el Python de la imagen (`tatana-docker-prueba:0.0.1-docker-prueba` ya existente, `--network none`, solo lectura) | `RESULTADO=ok`, los 7 casos de apagado/colgado/reset verdes |
| `bash ci/tatana-docker/probar-cambiar-contenedor.sh` | 7/7 (el caso real tarda los 6 s del timeout) |
| shellcheck `-S warning -x` de `deploy/tatana-docker/paquete/*.sh ci/tatana-docker/*.sh` | sin hallazgos |
| Regresión: `probar-firma.mjs`, `verificar-manifiesto-docker.py --autoprueba`, `bash -n` | OK |

Igual que antes: sin commits, sin correr `actualizar.sh` ni el actualizador en la Mac, sin tocar imágenes ajenas
(volví a bajar la imagen de shellcheck y la borré al terminar). No hice nada sobre las otras observaciones no
bloqueantes: el primer run de shellcheck/actionlint en CI y `sumar-actualizador.sh` sin firma propia, que ya está
documentado como el único paso que confía en el TLS del servidor.
