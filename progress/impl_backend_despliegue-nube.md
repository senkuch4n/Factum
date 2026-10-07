# impl_backend — despliegue-nube (#14)

**Estado:** done (sin commit, como pidió el orquestador)
**Rama:** `feat/despliegue-nube`
**Fecha:** 2026-10-06
**SDD:** `Refactorizaciones/despliegue-nube.md` §8.1 (P1 a P5 y DT1 aceptados; D9 = deploy automático al mergear a `main`)

## Archivos

### Backend (API)

| Archivo | Cambio |
|---|---|
| `server/src/Factum.Backend/Infrastructure/MongoHealthProbe.cs` | **Nuevo**: `IMongoHealthProbe`, `MongoHealthProbe` y `HealthReady.ToResult` (B1). |
| `server/src/Factum.Backend/Program.cs` | Registra el probe y mapea `GET /health/ready` después de `/health`, que no cambia (B2). |
| `server/tests/Factum.Backend.Tests/HealthReadyTests.cs` | **Nuevo** (B3): los dos mapeos serializados en snake_case, el probe contra `mongodb://127.0.0.1:1` (`false` en < 5 s, no toca ninguna base) y la cancelación del request. |
| `server/tests/Factum.Agent.Tests/OriginPolicyTests.cs` | `[Fact] Parse_BakedCloudList` con la lista horneada (B5, opcional). |

Detalle del probe:

- Cliente propio con `ServerSelectionTimeout` 2 s, `ConnectTimeout` 2 s y `SocketTimeout` 3 s.
- `ping` con un CTS enlazado a 3 s.
- Si falla, warning con el **tipo** de excepción, como mucho uno por minuto.
- Si el cliente cortó el request, propaga la cancelación.

### Empaquetado de Tatana

| Archivo | Cambio |
|---|---|
| `deploy/windows/hornear-origenes-tatana.py` | **Nuevo** (T1): regex de §5.3, chequeo de puerto 1-65535, dedup, JSON UTF-8 sin BOM con indent 2, `--probar`. |
| `deploy/windows/armar-tatana-portable.sh` | Flags `--origenes` y `--client-url` (T2). |
| `deploy/cloud/armar-tatana-nube.sh` | **Nuevo** (T3): `git archive` del `--ref` (default `origin/main`) → `armar-tatana-portable.sh` → `Tatana-Portable-vX.Y.Z-Windows-nube.zip` + `.sha256` → imprime `scp`/`install`. |

Comportamiento de los flags de `armar-tatana-portable.sh`:

- Los orígenes se validan **antes** de la descarga de unos 300 MB.
- Se hornean después del publish, con otro `verificar_mock`.
- `.ini` "nube" con `CLIENT_URL` y `UPDATE_URL` vacío, en CRLF.
- `--client-url` sin `--origenes` es error.
- Chequeo final con `unzip -p "$ZIP" appsettings.json`.

### Infraestructura (`deploy/cloud/`, todo nuevo)

| Archivo | Contenido |
|---|---|
| `docker-compose.yml` | Ver la lista de abajo. |
| `compose.bootstrap.yml`, `compose.bootstrap-2.yml` | Superadmins 0 y 1 (ver A1). |
| `Caddyfile` | El de §6.2, con comentarios. |
| `caddy-sitios/LEEME.md` | Uso de la redirección del dominio anterior. |
| `factum.env.example` | Solo placeholders `CAMBIAR`, sin DNIs ni contraseñas. Suma `FACTUM_AGENT_URL` y `FACTUM_TATANA_DESCARGA_URL` (opcionales, solo para `--construir-local`). |
| `mongo-init/01-usuario-app.js` | Crea `factum_app`. |
| `LEEME.md` | Mapa de la carpeta (I12). |
| `.gitattributes` | `*.sh`, `Caddyfile`, `*.yml`, `*.js` y `*.example` con `eol=lf` (I11). |

`docker-compose.yml`:

- `name: factum`, `caddy:2.11.7` (la última 2.x estable en Docker Hub y en GitHub releases el 2026-10-06) y `mongo:7.0.43`.
- Solo Caddy publica puertos.
- La red `datos` es `internal`.
- Logging `local` 10m×5.
- Branding global mapeado a `Branding__OrganizationName` y `Branding__OrganizationLogo` (las claves reales de `BrandingOptions`).
- `FACTUM_PUERTO_HTTP`/`HTTPS` con default 80/443, solo para la prueba local.

Scripts en `deploy/cloud/scripts/` (todos con `+x`):

| Script | Qué hace |
|---|---|
| `_comun.sh` | Carga y valida el env, `compose()`, salud, locks y la re-ejecución desde una copia (DT9). Ver la lista de abajo. |
| `compose.sh` | Wrapper para operar a mano. |
| `preparar-servidor.sh` | Bloques 1-10. Chequea AVX y `x86_64` **primero**. `bin/deploy-forzado.sh` queda de root (la clave de CI no puede reescribir su propio comando). |
| `desplegar.sh` | §6.5.1 completo. Códigos de salida: 0 OK; 1 falló (con rollback OK o sin versión previa); 2 rollback falló o uso inválido; 3 lock tomado; 4 guarda de commit o de dominio. |
| `deploy-forzado.sh` | Regex exacta `^desplegar (sha-[0-9a-f]{40})$` o `estado`. Todo lo demás: `comando no permitido`, exit 2. Registra en `logger -t factum-deploy`. |
| `revertir.sh` | Rollback manual. |
| `backup.sh` | §6.5.2. |
| `restaurar.sh` | §6.5.3. |
| `verificar-informes.sh` | Solo lectura. |

Lo que hace `_comun.sh`:

- Lee el `factum.env` con un **parser propio**, no con `source`. Así un valor con espacios o con `$( )` no ejecuta nada.
- Rechaza `$` en los valores, porque compose los interpreta.
- Valida obligatorias, `CAMBIAR`, JWT ≥ 32, dominio (sin esquema ni `/`, en minúsculas), registro en minúsculas,
  contraseñas de Mongo alfanuméricas ≥ 16 y distintas entre sí, recipient `age1…` y DNI/nombre/temporal de los
  superadmins.
- Los mensajes nombran la **clave**, nunca el valor.

### CI/CD

| Archivo | Contenido |
|---|---|
| `.github/workflows/desplegar-produccion.yml` | Ver la lista de abajo (C1). |
| `.github/workflows/verificar.yml` | PR a `develop`/`main` + `workflow_dispatch`. Job .NET (build y test de los 2 proyectos) y job Node 22 (`npm ci` + `tsc` de `client/` y de `agent-ui/`, este con `ELECTRON_SKIP_BINARY_DOWNLOAD=1`) (C2). |

`desplegar-produccion.yml`:

- Actions fijadas por SHA de commit (resuelto con `gh api` el 2026-10-06), con la versión en un comentario:
  - `checkout` v7.0.1;
  - `setup-buildx` v4.4.1;
  - `login` v4.6.0;
  - `build-push` v7.4.0.
- `concurrency` sin cancelar.
- Todos los `vars`/`secrets` llegan a los `run:` por `env:`.
- `if: vars.FACTUM_DEPLOY_HABILITADO == 'true'` en el job `desplegar` (DT11).
- `environment: production`.
- Valida `FACTUM_URL_PUBLICA` (`^https://[a-z0-9.-]+$`) y que el dueño esté en minúsculas.
- Label `factum.url_publica` en la imagen del front.
- `IdentitiesOnly=yes`.
- La clave se borra con `if: always()`.

### Docs y otros

| Archivo | Cambio |
|---|---|
| `docs/despliegue-nube.md` | **Nuevo** (D1). Ver la lista de abajo. |
| `README.md` | Subsección "Despliegue en la nube" con el link a la guía, tabla "Chequeos de salud del backend" con `/health` y `/health/ready`, `deploy/` y `.github/workflows/` en la estructura, y el CI de GitLab aclarado (sigue para Tatana; el deploy de la web es GitHub Actions). La fila de `NEXT_PUBLIC_TATANA_DOWNLOAD_URL` ya la había puesto el frontend (D2). |
| `.gitignore` | Reglas de §6.10, más `deploy/cloud/dist/` (salida de `armar-tatana-nube.sh`) (I13). |

`docs/despliegue-nube.md`:

- Secciones 0 a 18 con la estructura de §7, más el **Anexo A** (prueba local).
- Etiquetas [A mano]/[Repo], "*(a confirmar en el panel)*" en todo lo de DonWeb y placeholders `<MAYÚSCULAS>`.
- Ningún valor real.

**No se tocaron:**

- `client/` ni `agent-ui/`;
- `deploy/windows/docker-compose.yml`, `deploy/windows/scripts/*` ni `armar-paquete.sh`;
- `server/src/Factum.Agent/appsettings.json` (sigue con el `Mock: true` local del usuario, sin tocar);
- `progress/sesiones/`.

## Decisiones no obvias

### A1: superadmins con DNI vacío → variante `compose.bootstrap*.yml`

`AuthSettingsResolver.ResolveLocal` recorre `GetChildren()` de `Auth:Local:BootstrapSuperadmins`. Un elemento con
`Dni=""` produce el error `…:Dni="" no es válido` y el backend no arranca. Por eso las variables de superadmins **no**
están en `docker-compose.yml`:

- `compose.bootstrap.yml` (índice 0) y `compose.bootstrap-2.yml` (índice 1, independiente del 0) llevan `:?`.
- `_comun.sh::compose()` los suma con `-f` solo si `FACTUM_SUPERADMIN_<n>_DNI` tiene valor.
- Como **todo** `docker compose` pasa por `compose()`, la regla vale para `desplegar`, `compose.sh` y los demás.

Verificado en la prueba local: con el 0 cargado se crea el superadmin; con todo vacío (segundo proyecto restaurado),
arranca sin errores.

### Glob vacío de Caddy

Con `caddy validate` en `caddy:2.11.7`:

- con `caddy-sitios/` vacía (solo `LEEME.md`): **válido**, con un warning "No files matching import glob pattern";
- con un `anterior.caddy` de redirección: válido.

No hace falta `00-vacio.caddy`. La carpeta existe en git gracias al `LEEME.md`.

### Clave `age` obligatoria desde el primer deploy

`desplegar.sh` hace un backup pre-deploy cifrado en cada deploy (salvo el primero), así que
`FACTUM_BACKUP_AGE_RECIPIENT` es obligatoria en `cargar_env`. La guía hace generar la clave en la sección 6.
`FACTUM_BACKUP_DESTINO` puede quedar en `CAMBIAR` hasta la sección 12: solo la exige `backup.sh` sin `--solo-local`.

### Secretos fuera de la línea de comandos

Nada se pasa por argv del host (se vería en `ps`):

- `mongodump`/`mongorestore` reciben la contraseña de root con un `--config` YAML temporal **dentro** del contenedor,
  que ya tiene `MONGO_INITDB_ROOT_PASSWORD`.
- `verificar-informes.sh` y el conteo de `restaurar.sh` usan `mongosh --nodb` + `connect()` con `process.env`.

Verificado que el `mongodump --config` de `mongo:7.0.43` funciona.

### Estado guarda el tag completo

`estado/desplegado` y `estado/anterior` guardan `sha-<40hex>` o `local-<40hex>`, no solo el SHA. Así el rollback sabe si
la imagen es de GHCR o local.

### `restaurar.sh` en un host nuevo

Sin `estado/desplegado`, usa el tag de `version.txt` del backup:

1. `git fetch`;
2. `checkout`;
3. `pull`;
4. levanta **solo** Mongo (el backend no corrió nunca, así que la base está vacía y la guarda pasa);
5. restaura y levanta todo;
6. anota `estado/desplegado`.

Tiene la re-ejecución desde una copia (DT9), porque hace checkout. Los DOCX se extraen **encima** con un
`docker run --network none` de la imagen del backend (los archivos existentes pueden ser de root) y no se borra nada.
Con `--reemplazar` pide escribir el dominio.

### Carrera del init de Mongo (encontrada en la prueba)

En un datadir nuevo, el entrypoint de `mongo:7` levanta un `mongod` temporal que ya responde al healthcheck y después lo
reinicia. El primer conteo como root falló con `Authentication failed`. Se agregó un reintento de hasta 60 s en
`restaurar.sh`. El backend no tiene ese problema: el primer deploy local salió OK.

### Datos en Mongo en PascalCase

`verificar-informes.sh` consulta `cases.{_id, PdfFilename, ReportHash}`: el modelo no tiene `BsonElement` ni
convention pack. La SDD decía `pdf_filename`, que es el nombre en JSON. Los nombres de archivo se validan contra path
traversal.

### Retención local

`backup.sh` conserva los `FACTUM_BACKUP_LOCALES` más nuevos que no son `pre-deploy` y, aparte, los 3 últimos
`pre-deploy`. Si el disco está al 85 % o más, hace ping a `/fail` y sigue. El log del cron se vacía al pasar 1 MB.

### Otros

- **Errores de git con mensaje claro:** un `git fetch` o un `checkout` que falla en el VPS (deploy key vencida,
  archivos editados en el clon) corta con un `ERROR:` legible y vuelve al checkout anterior cuando corresponde.
- **P3:** `environment: production` declarado; actionlint no lo objeta. Si el plan diera error al usarlo, se saca (no
  se puede verificar sin correr el workflow en GitHub).

## Verificación (§9.1)

```
dotnet build server/src/Factum.Backend/Factum.Backend.csproj   → 0 Errores, 4 Advertencia(s)
dotnet build server/src/Factum.Agent/Factum.Agent.csproj       → 0 Errores, 0 Advertencia(s)
dotnet test server/tests/Factum.Backend.Tests  → Correctas! Con error: 0, Superado: 794, Omitido: 7, Total: 801
dotnet test server/tests/Factum.Agent.Tests    → Correctas! Con error: 0, Superado: 139, Omitido: 0, Total: 139
./ops/harness/verify.sh                        → Arnés OK (client/agent-ui tsc limpio, API y Tatana build limpio), exit 0
```

- **Warnings del backend:** son los avisos NuGet de siempre (`NU1902` SharpCompress 0.30.1, `NU1903` Snappier 1.0.0),
  cada uno contado dos veces. Ninguno viene de los archivos nuevos.
- **Tests omitidos:** los 7 son preexistentes, no de esta HU.
- **Tests y Mongo:** ningún test necesita Mongo, así que `verificar.yml` no lleva `--filter`.

```
bash -n deploy/cloud/scripts/*.sh deploy/cloud/armar-tatana-nube.sh deploy/windows/armar-tatana-portable.sh → OK
shellcheck (koalaman/shellcheck:stable, por docker) sobre los mismos archivos → exit 0, sin warnings
  (2 disable SC2016 justificados: JS y sh que se evalúan dentro del contenedor; SC1091 del /etc/os-release)
actionlint (rhysd/actionlint:latest, por docker) → exit 0
python3 deploy/windows/hornear-origenes-tatana.py --probar → OK  5 válidos, 17 inválidos y deduplicado
hornear sobre una COPIA (git show HEAD:server/src/Factum.Agent/appsettings.json) con https://factum-piloto.duckdns.org
  → AllowedOrigins = [localhost:3000, 127.0.0.1:3000, https://factum-piloto.duckdns.org], resto del JSON intacto
hornear 'https://x.com/path' → ERROR … exit=2
docker compose -f deploy/cloud/docker-compose.yml [-f compose.bootstrap.yml] --env-file <prueba> config → OK
caddy:2.11.7 caddy validate (caddy-sitios vacía, y con un anterior.caddy) → Valid configuration
cargar_env con factum.env.example → "todavía tienen el placeholder CAMBIAR …: FACTUM_REGISTRO FACTUM_DOMINIO …" (sin valores)
```

**D15:** `git diff --stat deploy/windows` muestra solo `armar-tatana-portable.sh`, y el `.py` es nuevo. No se armó el
zip completo (unos 300 MB y descargas de terceros). El argumento:

- todos los agregados están detrás de `[[ -n "$ORIGENES" ]]`, `[[ -n "$CLIENT_URL" ]]` o
  `[[ ${#ORIGENES_LISTA[@]} -gt 0 ]]`;
- sin flags, el `.ini` sale del mismo heredoc de antes, byte a byte;
- `appsettings.json` no se toca;
- la lista de archivos del zip no cambia.

Queda para el usuario en §9.3 punto 11.

### Prueba local del compose de producción (§9.2), hecha

La prueba se hizo en un clon aislado del repo en el scratchpad (nunca en la copia de trabajo) con:

- proyecto `factum-prod-test` (y `factum-prod-test2` para el restore);
- puertos 18080/18443 (y 18081/18444);
- `FACTUM_DOMINIO=localhost`;
- datos en un directorio temporal propio;
- los scripts corriendo dentro de un contenedor Ubuntu 24.04 con el CLI de Docker (`flock`, `find -printf` y `age` no
  están en la Mac).

**No** se tocó `evidentia-v2-mongo-1`, `factum_dev`, `dev-data/`, ni los procesos del usuario. Todos los datos de
negocio de la prueba eran ficticios (DNI `11222333`, caso `prueba-1`) y estaban en la Mongo **propia** del proyecto de
prueba.

| Prueba | Resultado |
|---|---|
| `desplegar.sh --construir-local` | `OK desplegado 97b87bb8`: Mongo inicializó `factum_app`, se creó el superadmin y Caddy respondió con su CA interna. |
| Headers (`curl -kI`) | HSTS `max-age=31536000`, nosniff, Referrer-Policy, `X-Frame-Options: DENY`, CSP `frame-ancestors 'none'`, CSP-Report-Only completa, Permissions-Policy. Sin header `Server`. |
| `http://` | `308` a `https://`. |
| `/swagger/index.html` | 404 (cae en el front). |
| `/health/ready` | `200 {"status":"ok","mongo":"ok"}`. Con `compose.sh stop mongo`: `503 {"status":"unavailable","mongo":"down"}` en unos 2,4 s, mientras `/health` sigue en 200. |
| Log del probe con Mongo caído | Un solo `warn … Mongo no respondió el ping (TimeoutException)`. |
| Puertos | Solo Caddy publica (18080/18443 tcp+udp); backend, frontend y Mongo sin `ports`. |
| Login de superadmin por Caddy | `must_change_password: true`, `/api/cases` en 403; `change-password` 200; con la nueva, `/api/cases` 200. |
| Logs (backend, caddy, mongo) | Sin la contraseña de `factum_app`. Muestran `CORS: orígenes permitidos https://localhost` y `Auth: modo local`. |
| `verificar-informes.sh` | `OK prueba-1 informe.docx`, exit 0. |
| `backup.sh --solo-local --motivo prueba` | `.tar.age` con `mongo.archive.gz`, `docx.tar.gz`, `version.txt` y `SHA256SUMS`, archivos en 600. |
| `restaurar.sh` sobre otro datadir y proyecto | Integridad OK, mongorestore, DOCX, salud y `verificar-informes` OK; la clave privada se borró. El login con la contraseña **actual** dio `must_change_password: false`. Un segundo restore sin `--reemplazar` se negó ("ya tiene 2 documentos"). |
| Rollback automático | Ver abajo. |
| Guarda de ancestro (DT7) | `--tag sha-<commit que no está en origin/main>` dio `no está en origin/main`, exit 4, sin checkout. |
| Pull que falla | Volvió al checkout anterior, exit 1, sin tocar lo que corría. |
| `deploy-forzado.sh` | `ls`, `desplegar sha-abc` y `desplegar sha-<40>; rm -rf /` dan `comando no permitido`; `estado` va a `desplegar.sh --estado`. |
| `--estado` | Muestra desplegado, anterior y la tabla de servicios. |

Detalle del rollback automático:

1. Se armó un commit roto a propósito en el clon (healthcheck del backend contra el puerto 9999).
2. Se tomó el backup `pre-deploy-703386f3`.
3. `up` falló.
4. Se imprimieron las 80 líneas del backend.
5. Salió `ROLLBACK a 97b87bb8 OK`, exit 1, con `estado` sin cambios y el checkout vuelto a `97b87bb`.

**Limpieza:**

- `docker compose -p factum-prod-test down -v` y `-p factum-prod-test2 down -v` (solo esos proyectos);
- se borraron las imágenes `local/factum-prueba/*` y `factum-operador-prueba`;
- se borraron los directorios de prueba del scratchpad.

Quedaron descargadas las imágenes públicas `caddy:2.11.7`, `ubuntu:24.04`, `koalaman/shellcheck:stable` y
`rhysd/actionlint:latest`.

Nota de entorno: dentro del contenedor, `df` del disco compartido de la Mac marcaba 99 % de uso. `backup.sh` lo avisa
(es el comportamiento esperado) y no tiene relación con la HU.

## Límites de terceros consultados (2026-10-06, fuentes oficiales)

| Fuente | Dato |
|---|---|
| Cloudflare R2 (`developers.cloudflare.com/r2/pricing`) | Capa gratis de 10 GB-mes de almacenamiento, 1 M de operaciones clase A y 10 M de clase B por mes; egreso gratis. |
| GitHub (`docs.github.com`, "Product usage included") | Minutos de Actions incluidos en repos privados: 2.000 (Free) y 3.000 (Pro). |
| UptimeRobot (`uptimerobot.com/pricing`) | "Free includes 5-minute intervals". La cantidad de monitores del plan gratis no quedó clara: va como "a confirmar". |
| healthchecks.io (`healthchecks.io/pricing`) | Existe el plan gratis "Hobbyist"; la cantidad de checks va como "a confirmar". |

- Todos van en la guía como "(a confirmar, consultado el 2026-10-06)".
- **No se citó ningún precio ni nombre de plan de DonWeb.** Todo lo del panel de DonWeb está marcado "*(a confirmar en
  el panel)*".
- La aprobación manual en environments de repos privados va como "a confirmar".

## Pendiente para la prueba real (usuario, §9.3)

Todo el checklist de §9.3 en el VPS real. En particular, lo que no se pudo probar acá:

1. `preparar-servidor.sh` (necesita Ubuntu 24.04 como root): ufw, swap, usuarios, sshd y el chequeo de AVX.
2. El certificado real de Let's Encrypt y el escaneo `nmap`.
3. El workflow en GitHub:
   - el build `linux/amd64` y el push a GHCR;
   - que `environment: production` no dé error en el plan de la cuenta (P3);
   - el SSH con `command=` forzado;
   - la guarda de dominio con imágenes reales de GHCR. Su lógica se ejecutó en la prueba local, pero solo por la rama
     que coincide.
4. `backup.sh` contra R2 con rclone y el ping a healthchecks.io.
5. `restaurar.sh` en un host nuevo sin `estado`, por la rama que usa `version.txt` + `pull` de GHCR. En la prueba local
   se sembró `estado/desplegado`.
6. Tatana "nube" en una PC Windows con Chrome/Edge:
   - armar el zip completo con `armar-tatana-nube.sh`;
   - el permiso de red local;
   - el flujo del perito.
7. D15: armar `deploy/windows/armar-paquete.sh` y comprobar que el portátil local sale igual.
8. Reinicio del VPS y alertas de UptimeRobot.

## Bloqueos

Ninguno.

## Contrato (coincide con la SDD §5)

- **`GET /health/ready`:** `200 {"status":"ok","mongo":"ok"}` / `503 {"status":"unavailable","mongo":"down"}`.
  Verificado en el test y por HTTP real.
- **Build args del front en el workflow:** `NEXT_PUBLIC_BACKEND_URL`, `NEXT_PUBLIC_AGENT_URL` (default
  `http://localhost:8765`) y `NEXT_PUBLIC_TATANA_DOWNLOAD_URL`. Coinciden con el `ARG` que el frontend agregó en
  `client/Dockerfile` y con `next.config.ts`.
- **Label:** `factum.url_publica`.
- **Variables de GitHub:** `FACTUM_URL_PUBLICA`, `FACTUM_AGENT_URL`, `FACTUM_TATANA_DESCARGA_URL`, `DEPLOY_PORT`,
  `DEPLOY_USER`, `FACTUM_DEPLOY_HABILITADO`.
- **Secrets de GitHub:** `DEPLOY_SSH_KEY`, `DEPLOY_KNOWN_HOSTS`, `DEPLOY_HOST`.
- **`Agent:AllowedOrigins`:** localhost + los orígenes pedidos. `Cors__AllowedOrigins__0` = `https://${FACTUM_DOMINIO}`.
- **Claves del `.env`:** las de §6.3, más las dos opcionales del plan B.

## Ajustes post-review

Corrigen las dos observaciones no bloqueantes de `progress/review_despliegue-nube.md`. Sin commit.

1. **`.github/workflows/desplegar-produccion.yml`:** el job `imagenes` ahora tiene
   `if: github.ref == 'refs/heads/main'`, y `desplegar` pasa a
   `if: github.ref == 'refs/heads/main' && vars.FACTUM_DEPLOY_HABILITADO == 'true'`. Así, si se lanza con
   `workflow_dispatch` desde otra rama, no se publican imágenes, `latest` no se mueve y no hay deploy. Los dos jobs
   quedan en "skipped" y el run sale en verde. La condición va explícita también en `desplegar`, aunque el
   `needs: imagenes` ya lo saltearía, para que no dependa de ese efecto indirecto.
2. **`docs/despliegue-nube.md` §11.3:** se agregó un párrafo para cuando SSH no usa el puerto 22. Explica que la
   línea de `DEPLOY_KNOWN_HOSTS` tiene que empezar con `[<IP>]:<puerto>`, cómo obtenerla con
   `ssh-keyscan -t ed25519 -p <PUERTO> <IP>`, cómo comprobar la huella y que el host tiene que ser el mismo que
   `DEPLOY_HOST`.

**Verificación:** `docker run --rm -v "$PWD":/repo -w /repo rhysd/actionlint:latest -no-color` no dio ninguna salida
y terminó con exit 0 (revisa los dos workflows de `.github/workflows/`).
