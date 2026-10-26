# QA — HU #109 `informe-extraccion` (informe pericial de extracción en DOCX)

**Fecha:** 2026-10-25 · **Rama:** `feat/informe-extraccion` · **QA:** API backend + Tatana (sin celular,
extracción **MockExtraction** real) · **Skill:** `qa-factum`

## Veredicto

**QA OK — sin hallazgos. Listo para promover.** No se encontró ningún defecto, ni bloqueante ni menor.
El flujo completo (generar extracción → generar informe → guardar en la carpeta → cadena de custodia) funciona
tal como lo describe la SDD `Refactorizaciones/informe-extraccion.md`, incluidos los casos borde.

## Gravedad máxima: ninguna

Tabla de hallazgos: **vacía**.

## Entorno de la prueba

- Backend `Factum.Backend` en `:8080` (perfil Development → DB compartida `factum_dev`), Tatana `Factum.Agent` en
  `:8765` con `Agent:Mock=true` y `Agent:DataDirectory` aislado en el scratchpad. Mongo compartido
  (`evidentia-v2-mongo-1`) y el proyecto en `:3000` **no se tocaron**. Ambos builds limpios (0 errores).
- Login en modo `dev` con DNI `99000001` (usuario `qa.robustez`). Destino de las extracciones: carpeta real en el
  scratchpad (`.../scratchpad/dest/...`).
- `/health.capabilities` de Tatana incluye `extraction_report_v1`.

## Qué se probó y resultado

### 1. Extracción MockExtraction `completed` (sin caso) — flujo principal
- POST `/extractions` (mock) → carpeta real con `ACTA.txt/.json`, `MANIFIESTO.tsv` + `MANIFIESTO.tsv.sha256`,
  `CIERRE.txt`, `LEEME.txt`, `SHA256SUMS.txt`, `datos/`, `archivos/`. 25 entradas de manifiesto.
- `MANIFIESTO.tsv.sha256` = `febec6ad3045cf01bc5e682a7e48400a19d813008c4e520fdb924176b809b8f2  MANIFIESTO.tsv`
  (verificado: coincide con el `shasum -a 256` real del `MANIFIESTO.tsv`).
- Registrada en el backend vía `PUT /api/extractions/{id}` con el `record` que arma Tatana (officer = 99000001).
  El backend obtuvo un sello RFC 3161 **granted** de `Free TSA` (freetsa.org) → permite ejercitar la rama "sellado".

### 2. Generación del informe — `GET /api/extractions/{id}/report`
- `200 OK`, `Content-Type` wordprocessingml, `Content-Disposition: attachment; filename=...; filename*=UTF-8''...`,
  `X-Report-Sha256` = SHA-256 real del cuerpo (verificado: `ccb1caff...`). 144.708 bytes.
- DOCX válido: `unzip -t` OK; partes OpenXML completas (document/header1-2/footer1-2/styles/numbering/theme...).
- **0 placeholders `{…}` sin reemplazar** en ningún part (document + headers + footers).
- Contenido verificado presente:
  - Sección fija **"Alcances y limitaciones"** con el texto D3 (cubre contactos/SMS/calendario/apps por adb RO +
    registro de llamadas por la app auxiliar firmada + archivos de `/sdcard`; no cubre `/data/data`, almacenamiento
    aislado Android 11+, mensajería cifrada, física/root/exploit, borrados; cierra: "no una limitación de esta
    herramienta ni un pendiente de desarrollo").
  - Identificación del equipo (`samsung SM-M127F`), perito (`Qa Robustez`) y DNI (`99000001`).
  - Metodología y categorías con cantidades (Contactos, Calendario, Aplicaciones, Registro de llamadas, Descargas,
    etc. con su método/resultado).
  - Hash del manifiesto (`febec6ad...`) y "Entradas del manifiesto: 25".
  - Estado del sellado: **granted** — "el hash del manifiesto fue sellado por una autoridad de sellado de tiempo
    independiente (Free TSA) el 25/10/2026 23:14:03".
  - Pie "Realizado con Factum" y subtítulo de portada "Informe de extracción lógica".

### 3. Guardar el informe en la carpeta — Tatana `PUT /extractions/{id}/report`
- Respuesta `{ directory, filename:"INFORME.docx", sha256, bytes:144708 }`.
- En la **raíz** de la carpeta aparecen `INFORME.docx` + `INFORME.docx.sha256`.
- `INFORME.docx.sha256` = `ccb1caff...  INFORME.docx`, que coincide con (a) el SHA-256 real del archivo en disco y
  (b) el `X-Report-Sha256` del backend. Formato `sha256sum` correcto.

### 4. CRÍTICO — cadena de custodia (byte-identidad)
- Baseline SHA-256 de **todos** los archivos de la carpeta tomado antes de escribir el informe (incluido un
  `sellos/MANIFIESTO.tsv.tsr` + `sellos/LEEME.txt` **plantados a propósito** para probar que no se tocan).
- Tras escribir `INFORME.docx`: `diff` baseline vs. after = **vacío**. `MANIFIESTO.tsv`, `MANIFIESTO.tsv.sha256`,
  `CIERRE.txt`, `SHA256SUMS.txt`, `sellos/*`, `ACTA*`, `datos/`, `archivos/` **byte-idénticos**. El informe es un
  artefacto posterior puro (solo agrega `INFORME.docx` + `.sha256`), no re-sella ni reordena nada.
- **Regeneración (idempotencia):** 2º `GET` + `PUT` sobrescribe `INFORME.docx` (hash nuevo por ids internos de
  OpenXML), recalcula el `.sha256`, mantiene el nombre fijo (no duplica con nombre descriptivo) y deja el
  `MANIFIESTO.tsv` intacto.

### 5. Casos borde
- **Sin caso asociado:** la extracción `completed` nunca se vinculó a un caso → el informe se genera igual y la
  sección de actuación/caso **no aparece** (0 ocurrencias de "actuación"). Confirmado también en el informe de la
  extracción interrumpida.
- **Interrumpida** (`MockExtractionOutcome=interrupted`, `device_disconnected`): informe `200`, zip OK, 0
  placeholders, refleja la interrupción y el motivo (desconexión).
- **Cancelada** (`cancelled_slow` + `POST /cancel`): status `cancelled`, informe `200`, zip OK, 0 placeholders,
  refleja la cancelación. Ambas conservan la sección "Alcances y limitaciones".

### 6. Robustez / seguridad (API)
- `GET report` de una extracción con **otro dueño** → `404` (aislamiento por officer). Sin auth → `401`.
- Tatana `PUT report` con **base64 inválido** → `400 invalid_request` ("El informe no es base64.").
- Tatana `PUT report` con **id inexistente** → `404`.

### 7. Recorrido por la web (paso 6, opcional)
- **No ejecutado.** El flujo end-to-end quedó validado a nivel de contrato (backend + Tatana) con bytes reales. El
  front (`client/`) es una capa de orquestación delgada (ya con `tsc` limpio en la impl) sobre esos mismos endpoints.
  No se levantó el cliente en `:3001` ni se usó el navegador para no interferir con la sesión del usuario. Queda como
  verificación manual liviana (ver abajo).

## Lo que anduvo bien (números)
- DOCX: 144.708 B (completed), 144.622 B (interrumpida), 144.528 B (cancelada); todos OpenXML válidos, 0 placeholders.
- Hash del informe consistente en 3 puntos (backend `X-Report-Sha256` = `INFORME.docx.sha256` = archivo en disco).
- Sello RFC 3161 granted real contra freetsa.org dentro del presupuesto inline.
- Cadena de custodia: 0 archivos preexistentes modificados tras escribir/regenerar el informe.

## DOCX de muestra (para abrir a mano)
`/private/tmp/claude-501/-Users-joelmiguelserrudo-Projects-Factum/6083dee2-99c1-423e-906e-8a8297fbab4c/scratchpad/informe-109-muestra.docx`
(extracción `completed` con sello granted, 144.708 B, OpenXML válido).

## Verificación manual que queda para el usuario
- Abrir el DOCX de muestra en Word/WPS: confirmar membrete/marca, colores, legibilidad de las 10 secciones.
- Recorrido web real en `/extracciones/[id]`: botón "Generar informe" → descarga del DOCX + aviso "Informe guardado
  en la carpeta" (Tatana local), y el fallback descarga-solo cuando la extracción es `onlyApi` o Tatana es viejo.
- End-to-end con el Samsung real (una extracción de verdad) para cerrar el ciclo con dispositivo.
- Caso **con** actuación asociada (cubierto por tests unitarios del backend; no se creó un caso en `factum_dev` para
  no ensuciar datos de negocio).

## Constancia de limpieza (regla dura de datos)
- Creados en `factum_dev`: 3 docs en `extractions` (ids `7319d827-…`, `ba552d64-…`, `51064047-…`) y sus 3 docs en
  `case_timestamps` (sellos). **Borrados por `_id`/`ExtractionId` exacto** al terminar (deleteMany acotado a esos 3
  ids; 0 restantes). No se tocó ningún otro documento ni colección.
- Carpetas de extracción + diarios: en el scratchpad aislado (`.../scratchpad/dest`, `.../scratchpad/agent-data`),
  nunca en `~/Factum/Evidencia`.
- Backend y Tatana levantados por esta QA: **bajados** al terminar. Mongo compartido y el proyecto en `:3000`:
  intactos. No se movieron tarjetas del Project ni se tocó `progress/sesiones/`. Sin commits.
