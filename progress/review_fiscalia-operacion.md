# Review — fiscalia-operacion (#95) — veredicto vigente: APROBADO (tercera revisión, delta de CI)

**APROBADO**

El bloqueante 1 quedó resuelto en todos los caminos y no se rompió nada de lo ya aprobado.

## Verificado por mí (reintento 1)
- `estado-agente.py`: `conexion_rechazada()` es True solo con `ConnectionRefusedError` (directo o como `reason` de `URLError`). Mantenimiento (líneas 146-151 y rama fixture 141-142): rechazada -> `HTTP=0`; timeout, reset u otro -> `HTTP=-1`; respuesta HTTP rara conserva su código. `comun.sh:366` solo cambia sin mantenimiento con `HTTP=0`; todo lo demás (incluida salida vacía) -> rc 5 sin stop/rename.
- Acción `estado` (líneas 123-131): rechazada -> `INALCANZABLE=1` (exit 3); timeout/reset/respuesta inválida -> `ERROR_AGENTE=<motivo>` (exit 4). `ERROR_AGENTE` coherente entre escritor (`estado-agente.py:130`) y único lector (`actualizador-tatana-docker.sh:401` -> `error_agente`, código ya existente en §10.7 y en `docs/tatana-docker.md:209`). Orden de chequeos en el paso 7: INALCANZABLE, ERROR_AGENTE, LIBRE; una salida vacía (docker run falló) cae en `esperando_libre`, que es seguro. `comun.sh:362` usa `estado` solo para MOTIVOS.
- Pruebas corridas en la Mac: `estado-agente.py --autoprueba` OK (casos nuevos de colgado/reset/rechazada verdes); `ci/tatana-docker/probar-cambiar-contenedor.sh` 7/7 (incluido el caso real con socket que acepta y no contesta: rc 5 y cero llamadas que cambian); `verificar-manifiesto-docker.py --autoprueba`, `prueba-cruzada.sh`, `probar-versiones.sh` OK; `bash -n` de todos los .sh de `deploy/tatana-docker` y `ci/tatana-docker` OK; `verificar.yml` sigue siendo YAML válido y ahora invoca `probar-cambiar-contenedor.sh` (línea 62).
- No ejecuté `actualizar.sh` ni el actualizador ni toqué imágenes Docker.

## Checkpoints
- C1: [x]  C2: [x]  C3: [x]  C4: [x] (nivel 1 verde; niveles 2/3 y prueba del servidor siguen siendo solo CI)  C5: [x]

## Observaciones no bloqueantes
- Los niveles 2 y 3 y `fiscalia.yml` no corrieron nunca: mirar el primer run en CI (shellcheck/actionlint no están en la Mac).
- `sumar-actualizador.sh` se publica suelto en el canal; verifica el manifiesto firmado antes de instalar, pero en PC nueva la primera confianza es el servidor (documentado).
- La espera de 90 s en la recuperación al reiniciar la PC es razonable.

---

## Historial: primera revisión (RECHAZADO)


**RECHAZADO** (1 hallazgo bloqueante, de seguridad de la evidencia; el resto del diff está sano)

## Verificado por mí
- `bash -n` sobre todos los .sh de `deploy/fiscalia`, `deploy/tatana-docker`, `ci/*`: sin errores.
- Los 3 workflows (`verificar.yml`, `tatana-docker.yml`, `fiscalia.yml`) parsean como YAML válido y el flujo tiene sentido (preparar -> armar -> firmar -> prueba/prueba-2004 -> publicar; release solo desde main; secretos vía `env:`; clave efímera solo en ensayo). No hay `actionlint` ni `shellcheck` en la Mac: no corrieron.
- Nivel 1 en la Mac: los 2 `.py --autoprueba` OK, `probar-versiones.sh` (108 comparaciones), `prueba-cruzada.sh` (Node <-> Python) y `ops/tatana/probar-firma.mjs` pasan. `./ops/harness/verify.sh` OK.
- No ejecuté `actualizar.sh` ni el actualizador. Las pruebas de CI (`prueba-actualizar.sh`, `_prueba-lib.sh`) tienen guardas (root, Linux, `GITHUB_ACTIONS` o `FACTUM_PRUEBA_VM_DESCARTABLE`) y rechazan correr en una máquina de trabajo.

## Hallazgos

### Bloqueante (alta)
1. **Un timeout del agente se interpreta como "agente apagado" y se cambia el contenedor igual, pudiendo matar una captura.**
   `deploy/tatana-docker/paquete/estado-agente.py:119` captura `OSError` (incluye `TimeoutError`, timeout de 6 s) y emite `HTTP=0`; `deploy/tatana-docker/paquete/comun.sh:366` trata `HTTP=0` como "no responde, no hay captura que proteger" y sigue con `stop`/`rename`. Entre el chequeo "libre" (paso 7 del actualizador) y `cambiar_contenedor` pasan `docker load`, autoprueba y extracción del kit (minutos, con disco/CPU cargados): es justo cuando un perito puede haber arrancado una captura y el agente tardar >6 s en contestar. Resultado: se baja el contenedor durante una captura, violando la regla "nunca durante una captura".
   Cambio requerido: distinguir "conexión rechazada / no hay nadie escuchando" (`ConnectionRefusedError`, o `URLError` cuya `reason` sea eso) de timeout/reset/cualquier otro error; solo lo primero puede dar `HTTP=0`; el resto debe dar `MANTENIMIENTO=error` con otro código (p. ej. `HTTP=-1`) para que `cambiar_contenedor` devuelva 5 (`error_agente`, sin tocar nada). Idéntico criterio en la acción `estado` (hoy `INALCANZABLE=1` por timeout también termina en `error_agente`, que es seguro; solo el camino de mantenimiento es el peligroso). Agregar un fixture/caso a la autoprueba (agente que acepta la conexión y no contesta) y verificar que NO cambia.

## Verificado y correcto (puntos de atención pedidos)
- Firma Ed25519 antes de instalar: el actualizador verifica con kit, claves y Python de la versión INSTALADA, `--network none`, nunca con lo que manda el servidor; payload valida nombre de imagen/archivo/kit, tamaño y SHA-256 antes de renombrar; sin downgrade (`COMPARACION=mayor`), versión fallida no se reintenta, carga + autoprueba solo con Tatana libre.
- `actualizar.sh`: backup previo obligatorio, vuelta atrás de archivos (compose, Caddyfile, mongo-init*, scripts, `config.env`) e imágenes (las anteriores no se borran hasta el final) tanto por `sano()` fallido como por corte a mitad (`trap al_salir`). La limpieza solo hace `docker image rm` de tags `factum-(backend|frontend-servidor|frontend)` ajenos a nueva/anterior y sin contenedores; el canal solo borra `imagen/kit-tatana-docker-v*` viejos. No toca `datos/`, evidencia, `config/.env` (salvo `FACTUM_VERSION`), `appsettings.Local.json`, `branding/` ni la CA de Caddy.
- Actualizador de PCs: cambio con rename/start-anterior, recuperación de cambio interrumpido, limpieza limitada a `imagenes.txt`, kits y descargas; datos, evidencia, `lockdown` y config se montan, no se tocan.
- `requiere.txt`: escritor (`armar-paquete-fiscalia.sh`: `mongo:X con-avx` / `mongo:4.4 sin-avx` / `caddy`) y lector (`actualizar.sh`: `read -r img modo _`) coherentes; línea sin columna = siempre.
- Candado `flock` heredado entre actualizar/backup/restaurar/instalar; Caddy sirve el canal solo lectura y registra solo `/tatana/*`.

## Checkpoints
- C1: [x] `verify.sh` OK (no verifiqué Fase del Project: no me corresponde moverlo)
- C2: [x] cadena de documentos y Contrato compartido (nombres coherentes entre `_lib.mjs`, `armar-payload.mjs` y `verificar-manifiesto-docker.py`; prueba cruzada verde)
- C3: [ ] seguridad operativa: `estado-agente.py:119` / `comun.sh:366` (hallazgo 1). Alcance correcto (nada fuera de scope; sin `server/src`, `client/`, `agent-ui/`).
- C4: [ ] niveles 2/3 y prueba del servidor no corrieron (solo CI; no verificable acá), y la autoprueba no cubre el caso del timeout. Nivel 1 sí pasa.
- C5: [x] progress del implementador presente; sin temporales ni datos ajenos tocados.

## Observaciones no bloqueantes
- Sin `shellcheck`/`actionlint` locales: el job `scripts` de `fiscalia.yml` y el de `verificar.yml` lo correrán en CI; conviene mirar su primer resultado.
- `sumar-actualizador.sh` se publica suelto en el canal sin firma propia; mitiga que verifica el manifiesto firmado antes de instalar, pero quien lo baje por primera vez confía en el servidor (documentado como PC nueva, con aviso).
- En `recuperar()` (actualizador), si coexisten `tatana-docker` y `tatana-docker-anterior` y el nuevo tarda >30 s en responder al reiniciar la PC, se descarta el nuevo y se vuelve al anterior; es conservador y seguro, solo anotarlo.
- Los caddy-logs (`/tatana/*`) guardan hostname de PC y versiones, sin datos de evidencia; OK.


---

## Tercera revisión (delta de CI) — `5d3d9fd..HEAD`

**APROBADO** (veredicto vigente)

Revisé el diff completo del delta (11 archivos). Cada cambio ataca la causa raíz y no debilita las garantías aprobadas.

- `actualizar.sh` (paso 2): el `du` con `pipefail`/`set -e` cortaba el script en silencio cuando el paquete no trae `imagenes/` (caso `--imagenes-de`, DU-3=A). Ahora `KB_IMG` solo se mide si existe el directorio, y `du`/`df`/`tar` en sustituciones llevan `|| true` con validación numérica. No toca backup previo, vuelta atrás, ni limpieza. Efecto lateral aceptable: si `du`/`df` del backup fallan, el chequeo de espacio se saltea (antes abortaba sin mensaje); anotado abajo.
- `comun.sh`: bajo systemd no hay `HOME`; ahora `HOME` -> `getent` -> `/root`. Verifiqué con `env -u HOME` y `set -u` que ya no explota (`DATOS=/root/.local/share/tatana-docker`). Root sigue tomando DATOS/EVIDENCIA reales de `/etc/factum-tatana-docker/instalacion.env` (`leer_instalacion_root`), así que no hay riesgo de escribir en rutas equivocadas ni de tocar datos del perito. Era un bug real que hubiera dejado el actualizador muerto en todas las PCs.
- `estado-tatana-docker.sh`: distingue "no se pudo preguntar a systemd" de "timer inactivo"; solo mensaje.
- Pruebas: CA en 644 (certutil corre como perito), logs legibles para el artifact, `version_health` toma la primera clave `version` (la del agente), `dbus` y `serial-getty` enmascarado en la imagen de 20.04. La única relajación de un chequeo es aceptar systemd `starting` además de `running`/`degraded` en 20.04, con `list-jobs` guardado como evidencia: es una condición de entorno de contenedor, no de producto, y los chequeos de comportamiento (timer en `list-timers`, corrida feliz, vuelta atrás con `systemctl start`, `systemd-analyze verify`) siguen intactos. Ningún chequeo de "nunca durante captura", rollback o conservación de datos fue tocado ni quitado.
- Composite action `.github/actions/dotnet-10`: `setup-dotnet` primero (con `continue-on-error`), y solo si falla, `dotnet-install.sh` desde `builds.dotnet.microsoft.com` por HTTPS con versión explícita y `--azure-feed` oficial. Acción pineada por SHA como antes; YAML válido. Usada en `verificar.yml`, `tatana-docker.yml` y `tatana-release.yml` (esta última es de release: el cambio es solo la fuente del SDK en un job de build, no toca firma ni publicación).
- Estado de CI: `gh pr checks 103` todo pass o skipping, incluidos nivel 2, nivel 3 y `actualizar.sh en un servidor de prueba` (los que antes nunca habían corrido). `bash -n` de scripts y parseo YAML de los workflows tocados: OK.

### Observaciones no bloqueantes (delta)
- Si `du`/`df` fallan al medir el espacio para el backup previo, `actualizar.sh` omite ese chequeo sin avisar; convendría un `aviso` ("no pude medir el espacio"). Menor.
- El fallback de .NET ejecuta un script descargado sin verificar hash; es el CDN oficial de Microsoft por HTTPS y solo en caso de fallo, pero en `tatana-release.yml` conviene retirarlo cuando se resuelva la caída del espejo.
- `tatana-release.yml` y `verificar.yml` quedaron tocados fuera del núcleo de #95 (justificado por el 409 de infraestructura); a mencionar en el PR.
