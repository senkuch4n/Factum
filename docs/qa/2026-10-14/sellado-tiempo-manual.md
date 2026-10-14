# QA manual — HU #78 sellado de tiempo RFC 3161 (pasos 1 a 7)

- Fecha: 2026-10-14, ~14:05–14:20 UTC (11:05–11:20 UTC-03:00).
- Rama: `feat/sellado-tiempo` (HEAD `0df6a12`). No se cambió código ni se hicieron commits.
- Entorno:
  - backend compartido en :8080 (Development, `factum_dev`, sellado con FreeTSA + DigiCert);
  - Tatana real en :8765 (`0.0.0.0-dev`, con `case_seals_v1`);
  - Samsung SM-M127F por USB (R58RA3R5AGA, Android 11).
- Método: flujo `agent` por API, en el mismo orden que la web (`page.tsx` `handleGenerateAgent`, `useFileManager.saveOne`).
  - Captura: `POST /devices/{serial}/screenshot?case_id=…`.
  - Guardado: `POST /cases/{id}/evidence/import` en Tatana y `PUT /api/cases/{id}/evidence`.
  - Generación: `generate/prepare`, `POST /cases/{id}/zip` en Tatana, `generate/finish` (multipart), `zip/commit`, `GET …/timestamps/files` y `PUT /cases/{id}/zip/seals` en Tatana (`syncCaseSeals`).
- Usuario QA: DNI `99000078` (Auth `dev`), con un perfil de perito creado para la prueba y borrado al final.

## Resultado

| Paso | Qué | Resultado |
|---|---|---|
| 1 | Línea de arranque del sellado | **OK** |
| 2 | Evidencia sellada al guardar y evento de trazabilidad | **OK** (API; la web no se miró, ver "No verificado") |
| 3 | Sello del ZIP y del informe, y carpeta `sellos/` en Tatana | **OK** |
| 4 | Contenido del DOCX | **OK** |
| 5 | Verificación independiente con `openssl` | **OK** |
| 6 | Sin autoridad (:8081) y recuperación por la cola de :8080 | **OK** |
| 7 | Sellado apagado (:8082) | **OK** |

**7 OK / 0 FALLA.** No hay hallazgos de producto con severidad Crítica, Alta ni Media. Abajo van observaciones de severidad
Baja o informativas.

---

## Paso 1 — Arranque · OK

En `scratchpad/logs/backend.log`, línea 14:

```
info: Factum.Backend[0]
      Sellado de tiempo: habilitado (freetsa.org, timestamp.digicert.com), timeout 10 s, 12 intentos máx.
```

`GET /api/config/public` → `"timestamping_enabled": true`.

## Paso 2 — Evidencia sellada · OK

- Caso nuevo `QA-SELLO-01` (`87b75e7a-0976-49a2-a30d-76fe4495b159`), `evidence_storage: "agent"`.
- 3 capturas reales del Samsung. Hubo que despertar la pantalla con `adb shell input keyevent KEYCODE_WAKEUP`: el primer
  intento dio `screen_off`, que es el chequeo de pantalla funcionando.
- Cada archivo se guardó por separado, como `saveOne`, y la respuesta de `PUT /api/cases/{id}/evidence` trae el estado del
  sello:

```
== screenshot_20261014_110826_693.png
HTTP 200 en 1.16s
timestamping_enabled= True authority= Free TSA
  screenshot_20261014_110826_693.png granted Free TSA 14/10/2026 11:09:01 (UTC-03:00) serial= 092ECD99 attempts= 1
== screenshot_20261014_110830_426.png
HTTP 200 en 0.25s
  ... 110830_426.png granted Free TSA 14/10/2026 11:09:01 (UTC-03:00) serial= 092ECD9C attempts= 1
== screenshot_20261014_110834_130.png
HTTP 200 en 0.24s
  ... 110834_130.png granted Free TSA 14/10/2026 11:09:02 (UTC-03:00) serial= 092ECD9F attempts= 1
```

Trazabilidad (`GET /api/cases/{id}/events`): un `timestamp_granted` por archivo, justo después de su `evidence_added`.

```
14:09:00.292Z evidence_added    screenshot_20261014_110826_693.png
14:09:01.392Z timestamp_granted screenshot_20261014_110826_693.png {target: evidence, authority: Free TSA, serial_number: 092ECD99, gen_time: 2026-10-14T14:09:01Z, tsa_url: https://freetsa.org/tsr}
14:09:01.503Z evidence_added    screenshot_20261014_110830_426.png
14:09:01.742Z timestamp_granted screenshot_20261014_110830_426.png {… serial_number: 092ECD9C …}
14:09:01.829Z evidence_added    screenshot_20261014_110834_130.png
14:09:02.063Z timestamp_granted screenshot_20261014_110834_130.png {… serial_number: 092ECD9F …}
```

En la web, `timestamp_granted` se muestra como "Sello de tiempo obtenido (evidencia)" (`lib/case-events.ts`). Eso no se
miró en pantalla.

Extra (SDD §13.2 paso 3): un re-registro idéntico del primer archivo respondió 200 en 0,005 s, con los mismos 3 sellos y
**sin un evento nuevo de sello**.

## Paso 3 — Generar · OK

```
prepare   200 0.02s  zip_filename=evidencia_QA_SELLO_01_QA_Denunciante.zip
buildZip  200 0.19s  zip_hash=d8e78595…2ca1  encrypted=True
finish    200 0.83s  zip_hash=d8e78595…2ca1  report_hash=74054382…b44e
commit    200 0.01s  deleted=3
timestamps/files 200 [evidencia_…zip.tsr, informe_pericial_…docx.tsr, screenshot_…693.png.tsr, screenshot_…426.png.tsr, screenshot_…130.png.tsr]
PUT seals (Tatana) 200 {"directory": ".../Evidencia/QA_SELLO_01_87b75e7a/sellos", "written": 6, "unchanged": 0}
```

`GET /api/cases/{id}/timestamps`:
- `enabled: true`, `case_mode: "enabled"`, `evidence_sealed: 3`, `evidence_total: 3`.
- `zip`: `granted`, Free TSA, `14/10/2026 11:09:56 (UTC-03:00)`, serie `092ECF1A`, `in_report: true`.
- `report`: `granted`, Free TSA, `14/10/2026 11:09:57 (UTC-03:00)`, serie `092ECF1C`.

La respuesta de `generate/finish` no trae los sellos: solo `case.timestamp_mode = "enabled"`. Coincide con la SDD (§7.2 no
cambia `GenerateResult`): la web los lee de `GET …/timestamps`.

Carpeta del caso en Tatana (`~/Factum/Evidencia/QA_SELLO_01_87b75e7a/`):

```
evidencia_QA_SELLO_01_QA_Denunciante.zip
sellos/
  evidencia_QA_SELLO_01_QA_Denunciante.zip.tsr        4642 B
  informe_pericial_QA_SELLO_01_QA_Denunciante.docx.tsr 4644 B
  LEEME-verificacion.txt                               6683 B
  screenshot_20261014_110826_693.png.tsr               4643 B
  screenshot_20261014_110830_426.png.tsr               4642 B
  screenshot_20261014_110834_130.png.tsr               4644 B
```

Contenido:
- un `.tsr` por archivo de evidencia, el del ZIP, el del informe y el LEEME;
- el LEEME trae los comandos exactos de cada sello, con fecha, serie y SHA-256;
- el LEEME manda a bajar el `cacert.pem` del sitio de FreeTSA en vez de incluirlo.

## Paso 4 — Informe (DOCX) · OK

DOCX bajado de `GET /api/cases/{id}/download/informe_pericial_QA_SELLO_01_QA_Denunciante.docx`. Su SHA-256 (`74054382…b44e`)
coincide con `report_hash`. Texto extraído de `word/document.xml`, tabla "NOMBRE / HASH SHA-256":

```
screenshot_20261014_110826_693.png
Sello de tiempo: 14/10/2026 11:09:01 (UTC-03:00) · Free TSA
screenshot_20261014_110830_426.png
Sello de tiempo: 14/10/2026 11:09:01 (UTC-03:00) · Free TSA
screenshot_20261014_110834_130.png
Sello de tiempo: 14/10/2026 11:09:02 (UTC-03:00) · Free TSA
evidencia_QA_SELLO_01_QA_Denunciante.zip
Contenedor de la evidencia
Sello de tiempo: 14/10/2026 11:09:56 (UTC-03:00) · Free TSA · N.º de serie 092ECF1A
```

Bloque explicativo debajo de la tabla:

> Los valores hash SHA-256 de la evidencia fueron sellados por una autoridad de sellado de tiempo independiente (Free TSA),
> conforme al estándar RFC 3161. […]
> El sello del contenedor de la evidencia fue emitido el 14/10/2026 11:09:56 (UTC-03:00), con número de serie 092ECF1A. 3 de
> 3 archivos cuentan además con un sello propio […]
> Los sellos se entregan como archivos .tsr en la carpeta "sellos" […] siguiendo el procedimiento del archivo
> LEEME-verificacion.txt, sin necesidad de la contraseña del contenedor.

El sello del informe **no figura**: ni la serie `092ECF1C` ni la hora `11:09:57` aparecen en ningún archivo del DOCX
descomprimido (`grep -r` sin resultados).

## Paso 5 — Verificación independiente con openssl · OK

- Todo se hizo sobre **copias** en el scratchpad (`qa-sello/verif/`); la carpeta del caso no se tocó.
- `cacert.pem` bajado de `https://freetsa.org/files/cacert.pem` a un directorio nuevo (`qa-sello/ca-dl/`):
  - `subject=O=Free TSA, OU=Root CA, CN=www.freetsa.org`;
  - SHA-256 `A6:37:9E:7C:…:18:AA:BC`.
- Se usó OpenSSL 3.6.4 y los comandos del LEEME.

```
## a) ZIP
$ openssl ts -verify -data evidencia_QA_SELLO_01_QA_Denunciante.zip -in sellos/evidencia_QA_SELLO_01_QA_Denunciante.zip.tsr -CAfile sellos/ca-freetsa.pem
Verification: OK

## c) evidencia con -digest (sha256 de la tabla del informe)
$ openssl ts -verify -digest 0c30a85a…b1a1 -in sellos/screenshot_20261014_110830_426.png.tsr -CAfile sellos/ca-freetsa.pem
Verification: OK

## b) DOCX descargado de Factum
$ openssl ts -verify -data informe_pericial_QA_SELLO_01_QA_Denunciante.docx -in sellos/informe_pericial_…docx.tsr -CAfile sellos/ca-freetsa.pem
Verification: OK

## copia del ZIP con un byte cambiado (byte 2985333 XOR 0x01; sha 85d8ab62…72f5)
$ openssl ts -verify -data tampered.zip -in sellos/evidencia_QA_SELLO_01_QA_Denunciante.zip.tsr -CAfile sellos/ca-freetsa.pem
…:error:17800067:time stamp routines:ts_check_imprints:message imprint mismatch:…
Verification: FAILED
```

Pruebas extra:
- Se extrajo una captura del ZIP con 7zz, usando la contraseña de `GET …/zip-password` (no se transcribe).
  `openssl ts -verify -data ext/screenshot_20261014_110834_130.png …` → `Verification: OK`.
- `openssl ts -reply -text` del ZIP:
  - `Status: Granted.`;
  - `Policy OID: tsa_policy1`;
  - `Hash Algorithm: sha256`;
  - `Serial number: 0x092ECF1A`;
  - `Time stamp: Oct 14 14:09:56 2026 GMT`;
  - `TSA: …CN=www.freetsa.org…`.
  Coincide con lo que muestran el DOCX y la API.

## Paso 6 — Sin autoridad (:8081) · OK

### Cómo se levantó la instancia

```
cd server/src/Factum.Backend
ASPNETCORE_ENVIRONMENT=Development PORT=8081 Storage__DataDirectory=<scratchpad>/qa-sello/dd8081 \
Timestamping__Urls=http://10.255.255.1/tsr ./bin/Debug/net10.0/Factum.Backend
```

- El puerto se pasa con `PORT`, no con `ASPNETCORE_URLS`: `Program.cs:45-49` hace `ListenAnyIP(PORT ?? 8080)`.
- La lista de URLs es **un string** separado por comas o espacios. `TimestampingOptions.ParseUrls(section["Urls"])` coincide
  con la SDD §5.6.1. Ver la observación O-1.
- `Storage:DataDirectory` apuntó a un directorio propio del scratchpad. Así la instancia no barre los temporales ni las
  generaciones huérfanas de la carpeta compartida al arrancar. Mongo (`factum_dev`) sí es el compartido.
- Log de arranque: `Sellado de tiempo: habilitado (10.255.255.1), timeout 10 s, 12 intentos máx.`

### Guardado

Caso `QA-SELLO-02-SIN-TSA` (`7ef61a14-59a3-4ea6-b47d-41712a2aa720`), con 2 capturas reales.

```
== archivo 1
HTTP 200 en 6.07s
timestamping_enabled= True authority= 10.255.255.1
  screenshot_20261014_111139_324.png pending attempts= 1 err= 10.255.255.1: sin conexión
== archivo 2 (enseguida)
HTTP 200 en 0.019s          <- disyuntor: no reintenta inline
  screenshot_20261014_111142_020.png pending attempts= 0
```

- El guardado tardó 6 s, por debajo del tope de unos 10 s, y quedó `pending`.
- En macOS, `10.255.255.1` falla por conexión (`ConnectionError`), no por timeout. Ver O-3.
- Log de :8081: `Sellado de tiempo: sin conexión con 10.255.255.1 (ConnectionError)`.

### Trazabilidad

```
timestamp_failed screenshot_20261014_111139_324.png {target: evidence, reason: "10.255.255.1: sin conexión", attempt: 1, final: false}
timestamp_failed screenshot_20261014_111142_020.png {target: evidence, reason: "10.255.255.1: sin conexión", attempt: 1, final: false}   <- lo intentó la cola de :8081
```

En la web, `timestamp_failed` es "No se pudo obtener el sello de tiempo (evidencia)". No se miró en pantalla.

### Generación contra :8081

```
prepare 200 0.02s · buildZip 200 0.09s · finish 200 13.32s (≤ 2 × 15 s de presupuesto) · commit 200
timestamps/files → files: []   (no hay sellos granted; no se llama a Tatana)
GET timestamps: sealed 0/2; zip pending (err "10.255.255.1: sin conexión"); report pending; evidencia pending
eventos: timestamp_failed (zip) y timestamp_failed (report), attempt 1, final false; report_generated
```

DOCX (`report_hash 55b7f564…b0d0`):

```
screenshot_20261014_111139_324.png
Sin sello de tiempo de un tercero al emitir este informe
screenshot_20261014_111142_020.png
Sin sello de tiempo de un tercero al emitir este informe
evidencia_QA_SELLO_02_SIN_TSA_QA_Denunciante.zip
Sin sello de tiempo de un tercero al emitir este informe

El contenedor de la evidencia no cuenta con sello de tiempo de un tercero al momento de emitir este informe: la autoridad
de sellado de tiempo no respondió. Si el sello se obtiene después, llevará su fecha real, posterior a la emisión de este
informe.
Ningún archivo de la evidencia cuenta con sello de tiempo de un tercero al emitir este informe.
```

### Recuperación por :8080 (cola compartida en Mongo)

- La instancia :8081 se detuvo a las 14:12:34 UTC.
- Un loop `until`, con sondeo cada 10 s y tope de 7 min, esperó a que todos los sellos del caso quedaran `granted`.
  **Tardó 51 s** (14:13:32).

```
sealed 2 / 2
zip    granted Free TSA 14/10/2026 11:13:27 (UTC-03:00) serie 092ED284 in_report=False attempts=2
report granted Free TSA 14/10/2026 11:13:27 (UTC-03:00) serie 092ED286 attempts=2
ev     granted … 11:12:56 serie 092ED208 attempts=2
ev     granted … 11:13:26 serie 092ED283 attempts=2
```

Log de :8080 (líneas 526–616):

```
Caso 7ef61a14-…: sello de tiempo obtenido (evidence, freetsa.org)   ×2
Caso 7ef61a14-…: sello de tiempo obtenido (zip, freetsa.org)
Caso 7ef61a14-…: sello de tiempo obtenido (report, freetsa.org)
```

Resultado de la recuperación:
- **Eventos:** los 4 `timestamp_granted` tienen `tsa_url: https://freetsa.org/tsr`.
- **DOCX intacto:** `report_hash` del caso sigue siendo `55b7f564…b0d0`, igual al hash del DOCX bajado antes.
- **ZIP:** `in_report: false`, es decir, obtenido después de emitir el informe, como pide la SDD.

Extra (SDD §13.2 paso 9, "Copiar sellos"):
- `GET …/timestamps/files` + `PUT /cases/{id}/zip/seals` → `written: 5`.
- El LEEME trae la nota "este sello se obtuvo después de emitir el informe; el informe indica que el contenedor no tenía
  sello de tiempo al momento de su emisión."
- `openssl ts -verify` del ZIP y del DOCX con los sellos tardíos → `Verification: OK` en los dos.

## Paso 7 — Sellado apagado (:8082) · OK

Instancia levantada con `PORT=8082`, `Timestamping__Enabled=false` y un `Storage__DataDirectory` propio. Log:

```
Sellado de tiempo: deshabilitado
Sellado de tiempo: deshabilitado; la cola no corre
```

`GET /api/config/public` → `"timestamping_enabled": false`.

Caso `QA-SELLO-03-APAGADO` (`710b7764-be11-4b30-8643-b22e3febf82c`), con 2 capturas reales:
- **Guardado:** HTTP 200 en 0,08 s y 0,02 s, con `timestamping_enabled=False`, `authority=None` y `timestamps=[]`.
- **Generación:** `finish` en 0,32 s. `timestamps/files` → `[]`, así que no se llama a Tatana.
- **`GET …/timestamps`:** `enabled: false`, `case_mode: "disabled"`, `zip: null`, `report: null` y evidencia sin sello.
- **`GET …/timestamps/bundle`:** `404 {"error":"El caso no tiene sellos de tiempo"}`.
- **Eventos:** `case_created`, `evidence_added` ×2 y `report_generated`; ninguno de sellado.
- **Mongo:** 0 documentos en `case_timestamps` para ese caso.
- **DOCX:** la tabla trae solo nombre y hash. No hay líneas "Sello de tiempo", ni bloque explicativo, ni "RFC 3161" o "TSA".
  Las únicas apariciones de "sello" son el nombre del caso QA.
- **Carpeta del caso en Tatana:** solo el ZIP y `.factum/`, sin `sellos/`.

---

## Hallazgos y observaciones

| ID | Severidad | Dónde | Detalle |
|---|---|---|---|
| O-1 | Baja (procedimiento/doc) | Prompt de QA; `TimestampingOptions.cs:60,82-86` | El pedido sugería `Timestamping__Urls__0=…`, pero `Urls` es **un string**, no un array. Con `__0`, .NET crea una clave hija que `section["Urls"]` no lee, y queda el valor de `appsettings.Development.json`: la instancia sellaría con FreeTSA **sin avisar** y la prueba de "sin autoridad" daría un falso OK. Lo correcto es `Timestamping__Urls=http://10.255.255.1/tsr`, que es lo que dice la SDD §13.2 paso 8. Sugerencia opcional: avisar al arrancar si existe `Timestamping:Urls:0`. |
| O-2 | Baja (procedimiento/doc) | `Program.cs:45-49` | `ASPNETCORE_URLS` no cambia el puerto del backend: se usa `PORT`. Ya figura en la SDD y en el código, pero el procedimiento de QA del prompt lo daba al revés. |
| O-3 | Informativa | Prueba de "sin autoridad" en macOS | `10.255.255.1` dio `ConnectionError` a los ~6 s, no un timeout de 10 s. Se probó el camino "sin conexión", pero no el corte exacto por `TimeoutSeconds`: ese caso lo cubren los tests unitarios de la SDD §11. |
| O-4 | Informativa | Capturas | El Samsung con la pantalla quieta dio 3 PNG idénticos (mismo SHA-256 `0c30a85a…b1a1`). El sistema los trató bien: un documento y un sello por `(filename, sha256)`, con series distintas. Una corrida futura puede cambiar la pantalla entre capturas para tener hashes distintos. |
| O-5 | Informativa | `generate/finish` | La respuesta no incluye los sellos del ZIP ni del informe; se leen de `GET …/timestamps`. Es lo que dice el contrato de la SDD §7.2. |

No se encontraron fallas de producto.

## No verificado

- **Lo visual en la web** (reloj con check y tooltip en la bandeja, bloque "Sello de tiempo" del resultado, etapa
  "Sellando el ZIP, generando el informe y sellándolo…", textos de la trazabilidad).
  - Los casos QA pertenecen al DNI `99000078`. Para verlos en el navegador había que iniciar sesión como ese usuario en
    `localhost:3000`, y eso reemplaza la sesión del usuario, que la skill `qa-factum` prohíbe cerrar.
  - No se usaron las herramientas de Chrome. Lo que muestran esos componentes sale de los campos verificados por API
    (`timestamps[]`, `gen_time_display`, `authority`, `evidence_sealed/total`, `in_report`, tipos de evento).
  - Para verlo, el usuario puede recorrer los pasos 2 y 4 de la SDD §13.2 con su propia sesión.
- **El aviso "No se pudo obtener el sello de tiempo de …" una sola vez en la UI**, y el paso del reloj gris al check por
  polling. Por API sí se confirmó el disyuntor: el segundo guardado tardó 0,019 s.
- **SDD §13.2 pasos 3b, 7, 10b, 11 y 12** (reemplazo con el mismo nombre, descarga del bundle con sellos, pendientes viejos
  con el sellado apagado, casos previos, Tatana viejo): fuera del alcance de los pasos 1 a 7 pedidos. Del bundle solo se
  vio el 404 con el sellado apagado.

## Datos creados y limpieza

Baseline de conteos en `factum_dev` antes de empezar:
- `account_brandings` 1, `agent_events` 17, `case_events` 0, `case_timestamps` 0, `cases` 5;
- `catalog_entries` 10, `catalog_seeds` 2, `expert_profiles` 1, `user_admin_events` 3, `users` 2.

Se calculó la diferencia de `_id` contra ese baseline y se confirmó que cada documento nuevo era de los 3 casos QA o del DNI
`99000078`. Todo se borró **por `_id`**:

| Colección | `_id` creados y borrados |
|---|---|
| `cases` (3) | `87b75e7a-0976-49a2-a30d-76fe4495b159` (QA-SELLO-01), `7ef61a14-59a3-4ea6-b47d-41712a2aa720` (QA-SELLO-02-SIN-TSA), `710b7764-be11-4b30-8643-b22e3febf82c` (QA-SELLO-03-APAGADO) |
| `case_timestamps` (9) | `231e0d163dd84d6c89c4eaf8ad1532f3`, `0246db4c05714f71979bba654be5c46e`, `d0b1bb4acbc142dcb8494b6254189f31`, `49e180e050b64ef0ba8567af0ca6995f`, `bbfc9e89d2a74e4f82c439979eb2c4e2`, `3ab6ca212b024731938113bdc23da2cb`, `477c45b581d942d2843b3e460326c689`, `4282e0a08c8f4c3db446512bce926873`, `efdeb0435fd84bcb8aee0cfb6644d836` |
| `case_events` (26) | `fc68aa4c-0689-4b41-b9c9-6811f92809b3`, `0a45bd8b-7b10-4eaf-adaf-95645731f403`, `a1275a92-e4c6-4007-9d17-587ed8135f94`, `2ff8f471-e0bf-453a-8456-b04b0613fb84`, `0c212565-d354-4b55-82af-f9226d3840a7`, `1cfc7c55-5fde-45be-b9d0-3bb7cb8ed033`, `7b53998d-ee55-40c2-9259-49b74c114243`, `10994fa3-a281-4a94-b3b0-5ac15b051ef4`, `2758e477-0676-49da-87a7-6fad7d9037e3`, `2e524fa8-9ced-44b3-8029-90351c510b43`, `a7995ece-ba46-44d5-bfaa-821857b393ec`, `95b6b15a-d7cd-4cfe-b6dd-7862276f183e`, `26f5455d-1160-4bc8-8d36-9e17b3d6b942`, `f02d4c8b-623f-4136-9a66-9aafe18439ef`, `b82688eb-a757-475f-80c7-b8a92df79c26`, `a05d398c-68e0-4282-affd-a04e526651b9`, `d6aa37b6-661e-4504-941a-048e6659d07b`, `6da2df7d-960d-479d-b106-5ca1a9c88437`, `25c3dfb6-f7cf-409f-a7e2-43e453a7aafd`, `a3e8985a-9fee-4969-974b-4884a4471253`, `560535b0-1699-4af2-a91c-066c23159e19`, `c792c116-a286-486d-adb2-0a51dc08e2c8`, `e049107a-fc77-4165-bd7d-36d26abefad9`, `63c6e7d3-7f56-4534-bd22-5d68180c4600`, `e7122115-a619-46e6-83dd-b0cf87c8955d`, `acfb7882-5873-491f-832f-654db3beaed8` |
| `catalog_entries` (5, `OwnerDni` 99000078, autoaprendidos al crear el caso) | `23cce583-b0fc-4e10-9f96-746d747e82ec`, `a87866b8-ef7a-4865-893f-5bd17f162bca`, `267f4018-3d17-49fd-a603-96a4a2034da0`, `798fbd0e-3a2e-4218-b2b0-7e5a98c2fcd3`, `a97f496a-2e06-4aa1-bbb0-36b03c5717fa` |
| `expert_profiles` (1) | `99000078` |
| `agent_events` | ninguno: los registra la web y acá no se usó |

**Después de borrar, los conteos son idénticos al baseline** (`diff` vacío).

Carpetas borradas, por ruta exacta:
- `~/Factum/Evidencia/QA_SELLO_01_87b75e7a`, `QA_SELLO_02_SIN_TSA_7ef61a14` y `QA_SELLO_03_APAGADO_710b7764` (ZIP, `sellos/` y
  `.factum/`);
- `server/src/Factum.Backend/dev-data/cases/87b75e7a-0976-49a2-a30d-76fe4495b159` (DOCX del caso 1);
- los DOCX de los casos 2 y 3 estaban en directorios del scratchpad (`dd8081`, `dd8082`), también borrados;
- la carpeta de trabajo de Tatana (`agent-data/cases/<id>`) ya no existía para ninguno de los 3: el `zip/commit` la vació. No
  quedaron capturas QA sueltas en la raíz de Tatana.

Instancias:
- :8081 (PID 56730) y :8082 detenidas; `lsof` no muestra nada escuchando en esos puertos;
- :8080, Tatana :8765 y la web :3000 no se tocaron.

Quedan en el scratchpad (`qa-sello/`, fuera del repo) las copias de verificación, el `cacert.pem`, los logs de :8081/:8082 y
los JSON de respuesta, sin contraseñas. El token de sesión QA quedó en `qa-sello/token`.
