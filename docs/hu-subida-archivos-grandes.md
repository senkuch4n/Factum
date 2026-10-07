# HU: Subir a la evidencia del caso grabaciones y videos grandes (cientos de MB a varios GB)

**Slug:** `subida-archivos-grandes`
**Apps afectadas:** API (`server/src/Factum.Backend`: `CasesController.UploadFile`, `CaseService.UploadFileAsync`,
`StorageService.SaveFileAsync`, config `Storage`) y web (`client/`: `lib/api.ts` `uploadFile`,
`hooks/useFileManager.ts` `handleUploadAndContinue`, `components/CaptureStep.tsx`, botón "Enviar evidencia y
continuar"). **Sin cambios** en Tatana (`server/src/Factum.Agent`) ni en `agent-ui/`. Probablemente sin cambios en
`deploy/windows/` (ver D1).
**Origen:** bug preexistente detectado por el reviewer de `instalacion-poca-ram` (2026-10-02) al medir con videos de
150-300 MiB: `POST /api/cases/{id}/files` responde "Request body too large" (ver
`progress/review_instalacion-poca-ram.md`, observaciones, y `progress/impl_backend_instalacion-poca-ram.md` L131-135
y L209).

**Como** perito que adquiere evidencia de un celular
**quiero** poder enviar al caso grabaciones de pantalla, videos traídos del dispositivo, videos de la cámara externa y
adjuntos de la PC sin importar que pesen cientos de MB o algunos GB
**para que** toda la evidencia adquirida quede en el expediente (y en el ZIP con su hash) sin tener que partirla,
recortarla ni copiarla a mano, y sin que una subida fallida deje basura o archivos truncados en la evidencia.

---

## Contexto

### Qué existe hoy (arqueología sobre `d15970a`)

**Recorrido de un archivo hasta la evidencia** (todo pasa por el navegador; el backend nunca habla con Tatana):

1. **Captura.** Según el origen, el archivo nace en dos lugares:
   - **En Tatana** (`localhost:8765`, carpeta `Storage:DataDirectory` del agente, default `./agent-data`):
     capturas de pantalla, **grabaciones de pantalla** Android (scrcpy, `AdbService` L139 `--video-bit-rate 4M`) e
     iOS (`IosService`: DVT ~2 FPS, AirPlay, qvh, con/sin mic), fotos de webcam por ffmpeg y archivos traídos del
     celular con el explorador (`POST /devices/{serial}/explorer/pull`, p. ej. videos de WhatsApp). El navegador se
     entera por WebSocket (`recording_stopped`, `photo_taken`, `screenshot_taken`) y los agrega a la lista con
     `addFile` (`client/src/app/dashboard/page.tsx` L155-175).
   - **En el navegador**: fotos y **grabación con cámara externa** (`CameraRecordModal.tsx`, `MediaRecorder`, corte
     automático a los 10 min, `MAX_SECONDS = 600`) y **adjuntos desde la PC** (`CaptureStep.tsx` L256,
     `accept="image/*,video/*,audio/*"`). Quedan como `Blob` en `pendingBlobs` (`useFileManager.handlePhotoBlob`).
   - Las variantes de video que genera Tatana para iOS (`video_variant_ready`, "MCI") solo se muestran; **no se suben**.
2. **Envío.** El botón "Enviar evidencia y continuar" (`CaptureStep.tsx` L742-745) llama a
   `useFileManager.handleUploadAndContinue` (L119-153): recorre los archivos no subidos **de a uno**; si el archivo
   está en Tatana lo baja entero como `Blob` (`agent.downloadFile`, `lib/agent.ts` L167-171, `res.blob()`), y lo
   sube con `api.uploadFile` (`lib/api.ts` L374-391): `fetch` POST con el `Blob` como cuerpo crudo, sin timeout,
   sin progreso. Al primer error corta el bucle y muestra `err.error || "Upload failed: <status>"` en el aviso
   global; los archivos ya subidos quedan marcados `uploaded` y un nuevo clic sigue con los que faltan. Durante el
   envío la UI solo muestra "Enviando…" en el botón y el estado "Enviando archivos al servidor...".
3. **Recepción.** `CasesController.UploadFile` (L90-100) pasa `Request.Body` a `CaseService.UploadFileAsync`
   (L302-315), que valida dueño del caso (`LoadOwnedAsync`, **no** valida que el caso sea editable) y llama a
   `StorageService.SaveFileAsync` (L39-51): `File.Create(Path.Combine(<data>/cases/<id>, filename))` y copia el
   cuerpo a través de un `CryptoStream` SHA-256. Devuelve `FileInfoDto` (`name`, `size`, `hash`, ...). El hash de la
   subida **no se persiste**: el que cuenta forensemente es el del ZIP al generar.
4. **Generación.** `GenerateAsync` lista el directorio del caso (todo lo que haya ahí entra al ZIP, salvo artefactos
   generados) y empaqueta por streaming (ver HU `instalacion-poca-ram`, Contexto).

**El límite.** No hay `MaxRequestBodySize`, `[RequestSizeLimit]` ni `[DisableRequestSizeLimit]` en ningún lado;
`Program.cs` L46 configura Kestrel solo con el puerto. Rige el default de Kestrel: **30.000.000 bytes (~28,6 MiB)**
por request.

**Tamaños reales esperables** (por qué el límite rompe el uso normal, no solo casos extremos):

| Origen | Tasa aproximada | Ejemplos |
|---|---|---|
| Grabación de pantalla Android (scrcpy a 4 Mbps) | ~30 MB/min | **1 min ya supera el límite**; 10 min ≈ 300 MB; 30 min ≈ 900 MB |
| Grabación iOS (AirPlay/qvh; DVT ~2 FPS es mucho menor) | del orden de decenas de MB/min | varios minutos = cientos de MB |
| Cámara externa (`MediaRecorder`, tope 10 min) | ~2,5-8 Mbps según resolución | 10 min ≈ 190-600 MB |
| Explorador del celular (videos de WhatsApp/galería) | — | lo que haya en el teléfono: videos de cámara 4K de varios GB; WhatsApp admite documentos de hasta 2 GB |
| Adjuntos de la PC | — | sin cota natural |

**Qué ve hoy el perito.** Como el `Blob` va con `Content-Length`, Kestrel rechaza en la primera lectura del cuerpo
(`BadHttpRequestException`, 413). No hay manejador de excepciones ni cuerpo JSON, y la respuesta de error muy
probablemente sale sin los encabezados CORS (el front está en `:3000`, el backend en `:8080`), así que el navegador
lo reporta como error de red: el perito ve "Failed to fetch" o, en el mejor caso, "Upload failed: 413". Nada le dice
que el archivo es grande ni qué hacer. (A confirmar en la medición de D12; el comportamiento exacto no cambia lo que
hay que construir.)

**Problemas de integridad en el mismo camino** (hoy, independientes del límite):
- **Archivo vacío o truncado en la evidencia.** `File.Create` crea el archivo final **antes** de leer el cuerpo. Si
  Kestrel rechaza por tamaño, si el navegador cierra la pestaña o se corta la conexión a mitad de un video, queda en
  `cases/<id>/` un archivo de 0 bytes o parcial con el nombre definitivo. `ListFilesAsync`/`GenerateAsync` lo toman
  como evidencia y **termina dentro del ZIP con su hash**. Un reintento lo pisa, pero si el perito no reintenta (o
  saca ese archivo de la lista), el parcial queda.
- **Pisado silencioso.** `File.Create` trunca un archivo existente con el mismo nombre. Los nombres de captura son
  únicos por timestamp, pero un adjunto de la PC o un archivo del explorador pueden repetirse.
- **Nombre con ruta.** `filename` viene del query string y va directo a `Path.Combine`: un nombre con `../` escribe
  fuera de la carpeta del caso (el front nunca lo manda, pero el endpoint lo acepta).
- **Caso no editable.** Se puede subir a un caso `generating`/`completed` (el resto de las escrituras usan
  `IsEditable`, L87-88): el archivo quedaría en la carpeta pero fuera del ZIP ya generado.

**Memoria, timeouts y disco** (lo que **no** es problema, verificado en código):
- **Backend: streaming real.** `Request.Body` no se bufferiza (no hay `EnableBuffering` ni model binding de form) y
  `CopyToAsync` usa un buffer de 80 KB: el consumo de RAM no depende del tamaño del archivo. Con el tope de 768 MB del
  perfil poca RAM (`deploy/windows/docker-compose.poca-ram.yml`) no hay riesgo de OOM por subir un video de varios GB;
  sí sube la *page cache* del contenedor, que es memoria recuperable (igual que lo medido en `instalacion-poca-ram`).
- **Timeouts.** Kestrel no tiene timeout total de request; su `MinRequestBodyDataRate` (240 B/s con 5 s de gracia) no
  se dispara en una subida local. `fetch` no tiene timeout. No hay proxy: en la instalación Windows el navegador va
  directo a `127.0.0.1:8080` (`deploy/windows/docker-compose.yml` L70). Un GB tarda segundos a decenas de segundos
  (la escritura a `C:\Factum\evidencia` pasa por el bind mount de Docker Desktop/WSL2, más lento que un disco local).
- **Navegador.** El archivo de Tatana se baja entero como `Blob` antes de subirlo. Chrome/Edge guardan blobs grandes
  en disco temporal, no en RAM, pero en la PC de 4 GB conviene medirlo (D10). Durante la subida el archivo ocupa
  espacio dos veces (Tatana + carpeta de evidencia) y transitoriamente una tercera (temporal del navegador).
- **Disco.** Nada valida espacio libre antes de aceptar un archivo; si el disco se llena a mitad de camino el
  backend tira `IOException`, responde 500 genérico y deja el parcial. El instalador solo avisa con < 50 GB libres al
  instalar (Q5, `deploy/windows/scripts/_comun.ps1` L726-737).

**Tatana no tiene el problema.** No recibe cuerpos grandes (ningún `Request.Body`/`IFormFile`; sus POST son JSON
chicos) y sirve los archivos con `PhysicalFile` (`Controllers/FilesController.cs` L17-26), que hace streaming sin
límite de tamaño de respuesta. El límite de 30 MB de Kestrel aplica solo a cuerpos de request.

### Qué es nuevo

1. El endpoint de subida acepta archivos grandes hasta un tope alto y configurable (D1, D2).
2. La escritura a la evidencia es atómica: se escribe a un temporal y el archivo aparece en la carpeta del caso solo
   completo; una subida cortada, cancelada o rechazada no deja nada (D6). Mismo cambio cierra el pisado silencioso, el
   nombre con ruta y la subida a casos no editables (D7, D11).
3. Rechazos claros y tempranos: archivo sobre el tope y espacio insuficiente responden con un error JSON legible
   (D4, D5).
4. En la web: progreso de subida por archivo y total, mensajes claros y cancelación (D3, D8).

---

## Criterios de aceptación

```gherkin
Feature: Subida de evidencia grande al caso

  Background:
    Given un perito logueado con un caso propio en estado editable
    And Factum con la configuración por defecto (tope de subida según D1)

  Scenario: Grabación de pantalla larga de Android
    Given una grabación de pantalla Android de 10 minutos (~300 MB) capturada con Tatana
    When el perito toca "Enviar evidencia y continuar"
    Then la grabación queda en la carpeta del caso con el mismo tamaño en bytes que en Tatana
    And su SHA-256 en la carpeta del caso es igual al del archivo en Tatana
    And el caso pasa al paso siguiente

  Scenario: Video de varios GB traído del celular o adjuntado desde la PC
    Given un video de 2 GB agregado a la evidencia (explorador del celular o adjunto de la PC)
    When el perito envía la evidencia
    Then el video se sube completo y su SHA-256 coincide con el original
    And el uso de memoria del backend no crece con el tamaño del archivo (perfil poca RAM incluido)

  Scenario: Progreso visible
    Given varios archivos para enviar, alguno de más de 50 MB
    When el perito envía la evidencia
    Then ve qué archivo se está subiendo, cuántos van del total y el avance en porcentaje o MB del archivo actual
    And los controles de captura quedan deshabilitados mientras dura el envío

  Scenario: Archivo que supera el tope
    Given un archivo más grande que el tope configurado
    When el perito envía la evidencia
    Then el backend lo rechaza antes de escribir nada en la carpeta del caso
    And el perito ve un mensaje que nombra el archivo, su tamaño y el tope ("El archivo X pesa 5,2 GB; el máximo es 4 GB")
    And los archivos anteriores que ya se subieron siguen subidos

  Scenario: Espacio insuficiente en el disco de la evidencia
    Given el disco donde vive la evidencia no tiene lugar para el archivo más el margen definido en D5
    When el perito envía la evidencia
    Then el backend lo rechaza sin escribir nada en la carpeta del caso
    And el perito ve "No hay espacio suficiente en el disco para guardar X (hace falta N GB, quedan M GB)"

  Scenario: Se llena el disco a mitad de una subida
    Given el espacio alcanzaba al empezar pero se agota durante la escritura
    When falla la escritura
    Then no queda ningún archivo parcial en la carpeta del caso ni temporal huérfano
    And el perito ve el mensaje de espacio insuficiente

  Scenario: Subida cortada (pestaña cerrada, red, backend reiniciado)
    Given un video de 1 GB a mitad de la subida
    When la conexión se corta antes de terminar
    Then en la carpeta del caso no aparece ese archivo, ni vacío ni truncado
    And la próxima generación del informe no incluye nada de ese intento
    And al reintentar el envío el archivo se sube completo

  Scenario: Cancelar el envío
    Given un envío en curso
    When el perito toca "Cancelar"
    Then se interrumpe la subida del archivo actual y no queda parcial en la carpeta del caso
    And los archivos que ya se habían subido siguen subidos
    And "Enviar evidencia y continuar" vuelve a estar disponible para seguir con los que faltan

  Scenario: Reintento después de un error
    Given un envío que falló en el tercer archivo de cinco
    When el perito vuelve a tocar "Enviar evidencia y continuar"
    Then se suben solo el tercero, el cuarto y el quinto

  Scenario: Nombre de archivo que ya existe en el caso
    Given en la carpeta del caso ya hay un archivo con el mismo nombre (ver D7)
    When llega una subida con ese nombre
    Then el archivo previo nunca queda truncado ni mezclado: se aplica la regla de D7 de forma atómica

  Scenario: Caso no editable
    Given un caso en estado generating o completed
    When llega una subida a ese caso
    Then se rechaza con 409 sin escribir nada (ver D11)

  Scenario: Evidencia existente
    Given casos creados antes de esta HU, con su evidencia en disco y su ZIP ya generado
    When se actualiza Factum con esta HU
    Then ningún archivo de evidencia existente se mueve, renombra ni modifica
    And los casos editables siguen aceptando subidas y generan igual que antes
```

---

## Datos que se registran

No hay datos nuevos en MongoDB ni cambios en el formato de la evidencia en disco.

| Dato | Obligatorio | Uso |
|---|---|---|
| `Storage:MaxUploadBytes` (env `Storage__MaxUploadBytes`) | No (default en código, ver D1) | Tope por archivo del endpoint de subida. |
| Margen mínimo de espacio libre (p. ej. `Storage:MinFreeBytes`) | No (default en código, ver D5) | Rechazar antes de escribir si no hay lugar. |
| Archivos temporales de subida | — | Viven fuera del listado del caso mientras se escriben; se renombran al terminar o se borran si falla (D6). Nunca son evidencia. |

---

## Diseño UX/UI (`client/`, paso 3 "Captura" del dashboard)

**Durante el envío** (reemplaza al "Enviando…" mudo):
- Un panel de progreso cerca del botón "Enviar evidencia y continuar": "Enviando 3 de 7 — grabacion_2026...mp4
  (184 / 310 MB)" con barra determinada del archivo actual y una línea con el total. Para archivos de Tatana se
  distingue la fase "Preparando desde el agente…" (bajada) de "Subiendo al servidor…".
- En la bandeja de evidencia (`capture/EvidenceTray.tsx`) cada archivo muestra su estado: pendiente / subiendo /
  subido / con error.
- Botón "Cancelar envío" (secundario) mientras dure; los controles de captura y quitar archivos, deshabilitados.
- Accesibilidad: barra con `role="progressbar"` y valores; el cambio de archivo se anuncia en una región `aria-live`
  educada, no en cada porcentaje.

**Errores** (en el aviso global de siempre, con el nombre del archivo y la acción):
- Sobre el tope: "El archivo X pesa 5,2 GB y el máximo permitido es 4 GB. Quitalo de la lista o pedí que se suba el
  tope." (D1).
- Sin espacio: "No hay espacio en el disco del servidor para guardar X (hace falta 2,1 GB, quedan 1,3 GB). Liberá
  espacio y volvé a enviar."
- Corte de red / backend caído: "Se cortó el envío de X. Los archivos anteriores quedaron guardados; tocá 'Enviar
  evidencia y continuar' para seguir."
- Cancelado: aviso informativo, no de error.
- Ningún mensaje muestra "Failed to fetch" ni "HTTP 413" crudos.

**Feedback final:** igual que hoy (pasa al paso 4). Sin cambios en `agent-ui/`.

---

## Fuera de alcance

- Subidas reanudables por bloques (retomar un archivo de 3 GB desde el byte donde se cortó): un corte reinicia ese
  archivo desde cero.
- Subir en paralelo varios archivos.
- Que el backend traiga los archivos directo de Tatana sin pasar por el navegador, o streaming de Tatana al backend
  sin `Blob` intermedio (Chrome solo permite cuerpos `ReadableStream` sobre HTTP/2, y el backend local es HTTP/1.1).
- Comparar el hash calculado por Tatana con el del backend (integridad extremo a extremo): Tatana hoy no calcula
  hashes. Candidata a HU propia (D9).
- Persistir el hash de la subida en Mongo o mostrarlo en la UI.
- Comprimir, recortar o transcodificar videos; cambiar el bitrate de scrcpy o el tope de 10 min de la cámara externa.
- Límites en otros endpoints (todos los demás reciben JSON chico).
- Cambios en Tatana o `agent-ui/`.
- Limpieza de la carpeta de Tatana después de subir (comportamiento actual sin cambios).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- El tope se puede fijar por endpoint con `IHttpMaxRequestBodySizeFeature` leyendo la config (un atributo
  `[RequestSizeLimit]` necesita una constante). Ojo: debe fijarse antes de leer el cuerpo.
- El 413/507 tiene que salir con cuerpo `{ error }` y **con CORS**, si no el navegador lo muestra como error de red.
  Si se rechaza por `Content-Length` antes de leer, el navegador puede seguir mandando el cuerpo: verificar que el
  error llegue igual al front.
- Temporal en el mismo volumen que la carpeta del caso (para que el renombrado sea atómico) y fuera de lo que enumera
  `ListFilesAsync` (subcarpeta o prefijo filtrado); limpiar huérfanos al arrancar.
- Verificar bytes recibidos == `Content-Length` antes de renombrar.
- Progreso de subida en el navegador: `fetch` no lo da; `XMLHttpRequest.upload.onprogress` sí (y `abort()` para
  cancelar).
- Espacio libre del volumen montado: confirmar en la SDD que `DriveInfo` dentro del contenedor ve el espacio real de
  `C:` a través del bind mount de Docker Desktop.
- Contrato compartido: nombres exactos de errores/códigos (`snake_case_lower`), en `lib/api.ts`.
- Regla dura de datos (`AGENTS.md`): las pruebas suben a un caso creado por la prueba y borran **solo** los archivos
  que subieron; nada de tocar `dev-data` ajena ni `C:\Factum\evidencia` real.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. Tope por archivo.**
- A) **Tope alto configurable, default 4 GB** por archivo (`Storage:MaxUploadBytes`), sin tocar `deploy/windows/`
  (el default vive en el código; quien necesite más lo sube por variable de entorno).
- B) Sin límite.
- C) Tope más bajo (1-2 GB).
- **Recomendada: A.** 4 GB cubre una grabación de pantalla de más de 2 horas a 4 Mbps, el máximo de documentos de
  WhatsApp (2 GB) y la mayoría de los videos de cámara del teléfono. Sin límite (B), un archivo equivocado (p. ej.
  una imagen de disco) puede llenar el disco de la evidencia; el chequeo de espacio de D5 lo mitiga pero un tope
  explícito da un mensaje más claro. C deja afuera videos reales del explorador. **¿Esperás videos de más de 4 GB
  (p. ej. grabaciones 4K largas del celular)?** Si sí, subimos el default.

**D2. Alcance del cambio de límite.**
- A) **Solo el endpoint `POST /api/cases/{id}/files`**, con el valor de la config.
- B) Global en Kestrel (`MaxRequestBodySize` para todo el backend).
- **Recomendada: A.** Es el único endpoint que recibe archivos; el resto (login, textos, catálogos) recibe JSON chico
  y conviene que siga con el límite de 30 MB como protección.

**D3. Progreso de subida en la UI.**
- A) **Barra de progreso real** por archivo (XHR con `upload.onprogress`) + contador "3 de 7" + estado por archivo en
  la bandeja.
- B) Solo texto "Enviando 3 de 7: nombre (310 MB)", sin porcentaje (sigue con `fetch`).
- C) Sin cambios.
- **Recomendada: A.** Con un video de 1-2 GB el envío dura de segundos a más de un minuto en la PC de 4 GB; sin
  avance el perito cree que se colgó y recarga, que es justo lo que corta la subida. B es más barato pero no
  distingue "lento" de "trabado".

**D4. Mensajes de error.**
- A) **El backend responde JSON `{ error }` con código propio** (413 sobre el tope, 507 sin espacio, 409 caso no
  editable, 400 nombre inválido) y con CORS; el front muestra el mensaje con el nombre del archivo y qué hacer. Los
  cortes de red se traducen a un texto en castellano.
- B) Solo mejorar el texto en el front a partir del código HTTP.
- **Recomendada: A.** Hoy el front ni siquiera recibe el 413 (le llega como error de red); sin arreglar el lado del
  backend, B no tiene con qué distinguir los casos.

**D5. Validación de espacio libre antes de aceptar.**
- A) **Sí:** antes de escribir, comparar `Content-Length` + un margen fijo (propuesta: 1 GB, configurable) contra el
  espacio libre del volumen de la evidencia; si no alcanza, 507 sin escribir nada. Además, si el disco se llena a
  mitad de camino igual, se borra el temporal y se responde 507.
- B) Solo atrapar el error de disco lleno durante la escritura y limpiar.
- **Recomendada: A.** Falla en un segundo en vez de después de subir 2 GB, y el margen evita dejar el disco al 100 %
  (Mongo vive en la misma unidad y se rompe sin espacio). B igual se incluye como red de seguridad.

**D6. Archivos parciales en la evidencia.**
- A) **Escritura atómica:** se escribe a un temporal fuera del listado del caso, se verifica que los bytes recibidos
  coincidan con `Content-Length`, y recién ahí se renombra al nombre final. Ante cualquier error, corte o
  cancelación se borra el temporal. Al arrancar el backend se borran temporales huérfanos (p. ej. por un reinicio a
  mitad de subida).
- B) Mantener la escritura directa y borrar el archivo final si falla.
- **Recomendada: A.** B no cubre el caso de que el proceso muera a mitad de escritura (OOM, reinicio de Docker,
  apagón): el parcial quedaría con el nombre definitivo y entraría al ZIP con hash, que es el peor resultado forense
  posible. Con A, lo que está en la carpeta del caso está siempre completo. No toca la evidencia existente.

**D7. Subida con un nombre que ya existe en el caso.**
- A) **Reemplazo atómico** del archivo existente al terminar la subida (el viejo queda intacto hasta que el nuevo
  está completo). Mantiene el comportamiento de hoy para reintentos, sin el riesgo de truncado.
- B) Rechazar con 409 si ya existe.
- C) Renombrar el nuevo (`nombre (2).mp4`).
- **Recomendada: A.** Los reintentos del front reusan el mismo nombre y tienen que funcionar; los nombres de captura
  son únicos por timestamp. B rompería un reintento después de un corte "tardío" (el servidor terminó pero la
  respuesta no llegó). C cambiaría nombres que el perito ya marcó con rol (`capture_roles` es por nombre). **Si
  preferís que nunca se pueda reemplazar evidencia en el servidor, B es la opción**, y el front trataría el 409 de un
  archivo idéntico como "ya subido".

**D8. Cancelación y reintento.**
- A) **Botón "Cancelar envío"** que aborta el archivo en curso (el backend descarta el temporal) y deja los ya
  subidos; el reintento es el mismo botón de siempre y sigue desde el archivo que falló. Sin reanudación por bytes.
- B) Sin cancelación (solo reintento como hoy).
- **Recomendada: A.** Con archivos de GB, el perito tiene que poder frenar si se equivocó de archivo sin recargar la
  página (recargar pierde la lista local de blobs de cámara/adjuntos). La reanudación por bytes es otra HU.

**D9. Integridad extremo a extremo (Tatana → backend).**
- A) **Fuera de esta HU:** se garantiza que lo escrito es exactamente lo recibido (bytes == `Content-Length`, hash
  calculado al vuelo) y que no quedan parciales; comparar contra un hash calculado por Tatana al capturar queda para
  una HU propia.
- B) Incluirlo: Tatana calcula SHA-256 al terminar cada captura, el front lo manda y el backend lo compara.
- **Recomendada: A.** B toca Tatana y el contrato entre el front y el agente; es valioso para la cadena de
  custodia pero es otro alcance. HTTP sobre localhost no corrompe datos en tránsito; el riesgo real hoy son los
  parciales, que cubre D6.

**D10. Archivos de Tatana pasando por el navegador.**
- A) **Mantener el flujo** (Tatana → `Blob` → backend) y medir en el perfil poca RAM con un video de 1-2 GB que el
  navegador no se quede sin memoria (Chrome/Edge guardan blobs grandes en disco).
- B) Rediseñar para que el archivo no pase entero por el navegador.
- **Recomendada: A.** El backend corre en Docker y no puede leer la carpeta de Tatana sin cambios de despliegue; el
  streaming directo desde el navegador no es posible sobre HTTP/1.1 (ver Fuera de alcance). Si la medición muestra
  problemas, se abre una HU específica.

**D11. Endurecimientos baratos del mismo endpoint.**
- A) **Incluir:** (a) rechazar con 400 un `filename` que traiga ruta o caracteres inválidos (hoy `../` escribe fuera
  de la carpeta del caso); (b) rechazar con 409 subidas a casos `generating`/`completed`, igual que el resto de las
  escrituras del caso.
- B) Dejarlos para otra HU.
- **Recomendada: A.** Son pocas líneas en el mismo código que se reescribe para D6, y (b) evita que aparezca en la
  carpeta de un caso cerrado un archivo que no está en su ZIP. Ningún flujo actual del front sube a un caso cerrado
  ni manda nombres con ruta, así que no rompe nada.

**D12. Cómo verificarlo.**
- A) En la Mac, con la pila del perfil poca RAM (backend 768m) en otro `COMPOSE_PROJECT_NAME` y otra carpeta (regla
  dura de datos): subir 150 MB, 1 GB y 3 GB comparando SHA-256 origen/destino y mirando `docker stats` del backend;
  un archivo sobre el tope; disco lleno simulado (volumen chico); corte a mitad (matar `curl` y reiniciar el backend
  durante la subida) verificando que no queda nada en la carpeta del caso; y la prueba de punta a punta en el
  navegador con una grabación real de Android de varios minutos. La prueba en la PC de 4 GB queda en la checklist
  manual.
- B) Solo prueba manual.
- **Recomendada: A.** Los escenarios de corte y disco lleno no se pueden ver a mano con confianza, y son los que
  protegen la evidencia.

## Validación (2026-10-02)

El usuario validó las 12 dudas en la opción **A** (recomendada).
