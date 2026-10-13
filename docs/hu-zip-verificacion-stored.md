# HU: El ZIP de evidencia se genera y verifica bien con cualquier tamaño de archivo

**Slug:** `zip-verificacion-stored` · **Issue:** #58 · **Prioridad:** crítica (bloquea promover `develop` a `main`)
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent/Services/Evidence/EvidenceZip.cs`)
y la copia del backend (`server/src/Factum.Backend/Services/Reports/EvidenceZip.cs`), con sus tests
(`server/tests/Factum.{Agent,Backend}.Tests/EvidenceZipTests.cs`). En `client/` y `agent-ui/` no hay cambios previstos (ver D4).

**Como** perito que cierra un caso
**quiero** que el ZIP cifrado de evidencia se genere y verifique bien sin importar el tamaño de los archivos
**para que** no me quede trabado en el paso "Generar" con un error que no se resuelve reintentando, y para que el ZIP que
entrego sea íntegro y comprobable.

Origen: hallazgo **C1** del QA de robustez del 2026-10-13 (`docs/qa/2026-10-13/e2e-navegador.md`, fila C1;
`docs/qa/2026-10-13/INFORME.md`, propuesta 1). Hay una reproducción offline en `docs/qa/2026-10-13/ziprepro/Program.cs`.

---

## Contexto

### Qué existe hoy

- **Armado del ZIP en Tatana** (`POST /cases/{id}/zip`, `CaseZipService.BuildAsync`, `CaseZipService.cs:189-225`):
  primero `hashing` (rehash contra el manifiesto), después `zipping` (`EvidenceZip.WriteAsync`) y por último `verifying`
  (SHA-256 del ZIP completo y `EvidenceZip.VerifyAsync`). Si algo falla, se borran el `.part`, el `.zip` y el `.sha`
  pendientes y se responde `500 zip_failed`. Si la excepción es `EvidenceZipVerificationException`, el mensaje es el de
  la excepción (p. ej. "No se pudo verificar el ZIP de evidencia: el archivo X no coincide con su hash"). Si no, va un
  mensaje genérico. La evidencia original no se toca.
- **Escritura** (`EvidenceZip.WriteAsync`, SharpZipLib 1.4.2): AES-256 WinZip AE-2 por entrada si hay contraseña,
  `UseZip64.Dynamic`, nombres UTF-8. Desde el **PR #54** (HU #51 `zip-evidencia-sin-recomprimir`,
  `docs/hu-zip-evidencia-sin-recomprimir.md`), el método va por extensión: **Stored** para video, audio, imagen y
  archivos comprimidos (`StoredExtensions`, 26 extensiones) y Deflate nivel 6 para el resto.
- **Verificación** (`EvidenceZip.VerifyAsync`, `Factum.Agent/.../EvidenceZip.cs:133-209`, misma lógica en
  `Factum.Backend/.../EvidenceZip.cs:115-191`): reabre el ZIP en solo lectura y controla la cantidad de entradas, que
  sean archivos, que estén en el manifiesto, que no se repitan y que tengan el cifrado AES-256 esperado (o ninguno).
  Después calcula el SHA-256 de cada entrada con
  `await SHA256.HashDataAsync(zf.GetInputStream(e), ct)` (líneas 185-186 en Tatana, 167-168 en el backend) y lo compara
  con el del manifiesto. Como SharpZipLib no valida el HMAC de AES al leer, ese SHA-256 es el único control de
  integridad del contenido.
- **Copias duplicadas a propósito** (D-T1 de `zip-local-informe-servidor`): "un cambio en una de las dos copias va
  también en la otra (y en sus tests)". En el backend lo usa solo el flujo viejo `ReportService.GenerateAsync`
  (`ReportService.cs:154`).
- **Tests actuales** (en los dos proyectos): ida y vuelta cifrado y en claro, contraseña incorrecta, hash que no
  coincide, entradas de más o de menos, cifrado esperado contra real, `MethodFor` por extensión, mezcla Stored+Deflate,
  entrada Stored corrupta detectada, `bsdtar` y `7zz`. **Ninguno recorre tamaños de archivo**: usan contenidos fijos
  que justo no caen en los tamaños que fallan.
- **En el cliente** (`client/src/lib/agent-messages.ts:102-103`), `zip_failed` se muestra siempre con el texto genérico
  "No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta. Reintentá.".

### Qué encontró el QA (C1)

- Un caso con `screenshot_20261013_110052_566.png` (44 214 B) **siempre** falla al generar: 500, el toast pide
  "Reintentá" y Tatana loguea `EvidenceZipVerificationException: … no coincide con su hash`. El hash en disco es el
  correcto. Reintentar no sirve, y el caso solo se pudo cerrar **borrando esa captura**.
- Reproducción offline con SharpZipLib 1.4.2 (`ziprepro/Program.cs`, modo `sweep`). Con entrada `.png` (Stored) + AES,
  leer con `SHA256.HashDataAsync(zf.GetInputStream(e))` da un hash distinto para algunos tamaños: **fallan 4095, 44 214
  y 44 216 B** y **pasan 4096 y 44 660 B**. Depende del tamaño y del contenido. Con lectura **síncrona**
  (`SHA256.HashData`) hubo 0 fallos en 120 órdenes de archivos y en todo el barrido.
- Es una regresión que expuso el PR #54: antes todas las entradas iban con Deflate.

### Qué no se sabe todavía

No está demostrado si **el ZIP escrito es correcto** y solo falla nuestra lectura async, o si **el ZIP queda mal escrito**
(escritura async de una entrada Stored+AES) y la lectura sync lo "tapa". Esto importa mucho: en el primer caso el
arreglo va en `VerifyAsync`; en el segundo, un ZIP que pasa la verificación podría no abrirse bien en 7-Zip. Por eso la
HU pide determinarlo antes de fijar el arreglo (ver criterios y D1).

> Hipótesis (no verificada) para el `architect`: el stream AES de SharpZipLib deriva de `CryptoStream` y redefine
> `Read(byte[], int, int)` para separar los 10 bytes del HMAC. Si no redefine `ReadAsync`/`Read(Span)`, la lectura async
> usaría el camino interno de `CryptoStream` y no esa lógica. Con Deflate, el inflador lee de forma síncrona por encima
> y por eso no se notaba. Si se confirma, el ZIP estaría bien y el problema sería solo de lectura.

### Qué es nuevo

1. La verificación da el resultado correcto para entradas Stored y Deflate, cifradas o no, **con cualquier tamaño**.
2. Queda confirmado con herramientas externas (7-Zip y `python3 zipfile`) que el ZIP generado es correcto.
3. Tests de regresión con barrido de tamaños y los dos métodos, en las dos copias.
4. Si la verificación falla por un problema nuestro y no por la evidencia, el error lo deja claro.

El formato del ZIP, el cifrado, la regla Stored/Deflate y todos los controles de la verificación quedan como están.

---

## Criterios de aceptación

```gherkin
Feature: ZIP de evidencia correcto y verificado con cualquier tamaño de archivo

  Background:
    Given Tatana corriendo en la PC del perito
    And un caso con evidencia guardada en la carpeta del caso de Tatana

  Scenario: El caso del QA se genera
    Given la evidencia incluye una captura PNG de 44 214 bytes (el archivo del hallazgo C1 o uno del mismo tamaño)
    When el perito genera el caso con contraseña
    Then POST /cases/{id}/zip responde OK en el primer intento
    And el ZIP queda cifrado con AES-256 (WinZip AE-2), con la entrada en Stored
    And el SHA-256 de la entrada extraída coincide con el del manifiesto

  Scenario Outline: La verificación es correcta para cualquier tamaño y método
    Given un archivo de <tamaño> bytes con contenido aleatorio y método <método>
    When se arma el ZIP <cifrado> y se verifica con EvidenceZip.VerifyAsync
    Then la verificación pasa
    And el SHA-256 calculado al verificar es igual al del archivo original

    Examples:
      | tamaño | método  | cifrado         |
      | 0      | Stored  | con contraseña  |
      | 1      | Stored  | con contraseña  |
      | 4095   | Stored  | con contraseña  |
      | 4096   | Stored  | con contraseña  |
      | 4097   | Stored  | con contraseña  |
      | 44214  | Stored  | con contraseña  |
      | 44216  | Stored  | con contraseña  |
      | 44660  | Stored  | con contraseña  |
      | 81920  | Stored  | con contraseña  |
      | 81921  | Stored  | con contraseña  |
      | 4095   | Deflate | con contraseña  |
      | 44214  | Deflate | con contraseña  |
      | 4095   | Stored  | sin contraseña  |
      | 44214  | Stored  | sin contraseña  |

  Scenario: Barrido de tamaños en los tests de regresión
    Given los tests de EvidenceZip de Factum.Agent.Tests y de Factum.Backend.Tests
    When se corren
    Then incluyen un barrido de tamaños alrededor de los límites de bloque AES (16 B), de buffer (4 KiB y 80 KiB)
      y de los tamaños que fallaron en el QA (4095, 44 214 y 44 216), más los tamaños de la tabla de arriba
    And el barrido cubre Stored y Deflate, con y sin contraseña
    And los dos proyectos tienen la misma cobertura
    And con el código anterior al arreglo el barrido falla (se demuestra que el test detecta el bug)

  Scenario: El ZIP generado es correcto según herramientas externas
    Given un ZIP armado por EvidenceZip.WriteAsync con contraseña y entradas Stored de los tamaños que fallaban
    When se prueba con `7zz t -p<contraseña>` y se extrae con `python3 zipfile` (o con 7zz)
    Then 7-Zip informa "Everything is Ok" (incluido el control del HMAC de AES)
    And el SHA-256 de cada archivo extraído coincide con el original

  Scenario: Se determina dónde estaba el problema
    Given un ZIP que con el código actual falla la verificación (p. ej. armado con la reproducción del QA)
    When se lo prueba con 7zz y con python3 zipfile antes de aplicar el arreglo
    Then el progress del implementador deja escrito si el ZIP estaba bien (falla solo la lectura async)
      o si estaba mal escrito (falla también la escritura)
    And el arreglo corrige la causa real que se encontró, no solo el síntoma

  Scenario: La verificación sigue detectando ZIP alterados
    Given un ZIP armado correctamente
    When se altera un byte del contenido de una entrada Stored o Deflate, o se usa una contraseña incorrecta,
      o falta o sobra una entrada, o el cifrado no es el esperado
    Then la verificación falla igual que hoy y la generación se corta sin tocar la evidencia

  Scenario: Formato y contrato sin cambios
    When se genera un caso
    Then cada archivo sigue con el método que le asigna MethodFor (Stored para la lista D2 de #51, Deflate para el resto)
    And se mantienen el cifrado AES-256 AE-2, Zip64 Dynamic y los nombres UTF-8
    And no cambian el contrato de POST /cases/{id}/zip y /zip/commit, los eventos zip_progress ni los datos del expediente
    And la memoria de Tatana sigue en el mismo orden que hoy con archivos de varios GB (el arreglo no carga entradas enteras en memoria)
    And el tiempo de verificación con el juego de 5,5 GB del QA de #51 no empeora de forma notable (ver D5)

  Scenario: Error claro si la verificación falla por un problema nuestro
    Given que la verificación del ZIP falla aunque el rehash de la evidencia contra el manifiesto dio OK
    When Tatana responde zip_failed
    Then el mensaje deja claro que la evidencia está intacta y que el ZIP que se armó no pasó el control
    And no le pide al perito que borre evidencia
    And el log de Tatana registra la entrada, su tamaño, el método (Stored/Deflate), si está cifrada y los dos hashes
      (esperado y obtenido), sin la contraseña

  Scenario: Las dos copias quedan iguales
    When se aplica el arreglo
    Then el cambio de lectura (y de escritura si hiciera falta) está en las dos copias de EvidenceZip.cs
    And la única diferencia entre las copias sigue siendo el callback de progreso de WriteAsync en Tatana
```

---

## Datos que se registran

No hay datos nuevos en la base ni en el expediente. `zip_encryption` sigue en `aes256-ae2`.
Lo único que se suma es detalle en el **log** de Tatana y del backend cuando la verificación falla (entrada, tamaño,
método, cifrado y los dos hashes), sin la contraseña.

---

## Diseño UX/UI

No hay cambios de pantalla previstos. En `client/` (web), el paso "Generar" sigue igual. Lo visible es que el caso del
QA deja de fallar. `agent-ui/` (Electron) no cambia. Hoy el cliente muestra el texto genérico de `zip_failed`
("… Reintentá.") y no el mensaje de Tatana. D4 decide si eso se toca en esta HU.

---

## Fuera de alcance

- Los demás hallazgos del QA del 2026-10-13 (C2 grabación Android, A1-A6, T-01..T-18, informe, API): tienen sus propias HU.
- T-08 (contraseña vacía) y T-09 (contraseña de 100 caracteres o más que 7-Zip trunca): son de `tatana-ws-y-hosts` o
  de otra HU, salvo que la SDD encuentre que comparten el arreglo.
- Cambiar de librería de ZIP, de cifrado o de formato. Volver todo a Deflate o cambiar la lista de extensiones Stored
  (salvo que D1 lleve a eso como contingencia).
- Optimizar el tiempo del ZIP más allá de no empeorarlo.
- Validar el HMAC de AES en nuestra verificación (hoy no se valida y el SHA-256 lo cubre). Si D1 muestra que hace
  falta, se discute aparte.
- Regenerar ZIP de casos ya completados, salvo lo que decida D3.
- Cambios en `agent-ui/`, y en `client/` salvo lo que decida D4.

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- Usar `docs/qa/2026-10-13/ziprepro/Program.cs` (modo `sweep`) como reproducción de partida, y pasarla a tests xUnit en
  los dos proyectos.
- El barrido tiene que ser liviano (pocos segundos, archivos chicos, contenido con semilla fija) para correr en cada
  build. Si se quiere uno amplio (miles de tamaños), que vaya aparte y marcado.
- Los tests de `7zz`/`bsdtar` existentes se saltean si la herramienta no está. Los tamaños nuevos se suman ahí.
- Regla dura de datos (`AGENTS.md`): los tests trabajan en carpetas temporales propias y no tocan `agent-data`, la
  carpeta de evidencia ni `Storage:DataDirectory`. Los casos QA que quedaron en la base de desarrollo
  (`docs/qa/2026-10-13/INFORME.md`, "Pendiente de limpieza") no se tocan.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. ¿Arreglo mínimo en la lectura o revisar también la escritura?**
- A) Arreglo mínimo: en `VerifyAsync`, hashear con lectura síncrona (`SHA256.HashData` o `IncrementalHash` con un
  buffer y `Read` síncrono) sin investigar más.
- B) **Primero determinar si el ZIP queda bien escrito**: tomar un ZIP que falla hoy, probarlo con `7zz t` y extraerlo
  con `python3 zipfile`, comparar hashes y dejar el resultado en el progress. Si el ZIP está bien, se aplica A (lectura
  síncrona por bloques, sin cargar la entrada en memoria). Si está mal, se corrige también la escritura (p. ej. escribir
  la entrada con llamadas síncronas) y se vuelve a validar con 7zz.
- C) Esquivar el problema: volver a Deflate para todo (deshacer #51) o copiar cada entrada a un buffer en memoria antes
  de hashear.
- **Recomendada: B.** Es la única opción que asegura que el ZIP entregado sea íntegro, no solo que nuestra verificación
  dé OK. Si la escritura estuviera rota, A dejaría pasar ZIP que 7-Zip rechaza, que es peor que el bug actual porque
  sería silencioso. El diagnóstico cuesta minutos (la reproducción ya está). C pierde la mejora de #51 o carga en
  memoria grabaciones de varios GB.

**D2. ¿Se reporta el bug a SharpZipLib?**
- A) **Sí, un issue con una reproducción mínima (sin datos de casos) cuando se confirme la causa**, y un comentario en
  el código que apunte al issue.
- B) No. Se arregla de nuestro lado y listo.
- C) Además mandar un PR upstream.
- **Recomendada: A.** Cuesta poco, deja registro para cuando se actualice la librería (y para saber si se puede volver
  a async) y no frena esta HU. C lleva tiempo y no depende de nosotros. Se puede hacer después.

**D3. ¿Se revisan los ZIP ya generados desde #54?**
`develop` no se promovió a `main`, así que en producción no hay ZIP armados con Stored. Solo existen los de desarrollo
y QA.
- A) **No se revisa nada en producción (no hay). Si D1 muestra que la escritura estaba rota, se prueban con `7zz t`
  los ZIP de desarrollo/QA armados desde #54 y se anota el resultado, sin regenerarlos ni borrarlos.**
- B) Revisar siempre los ZIP de desarrollo, aunque el problema sea solo de lectura.
- C) No revisar nada.
- **Recomendada: A.** Si el problema es solo de lectura, esos ZIP pasaron una verificación que, en el peor caso, dio un
  falso negativo y nunca un falso positivo, así que los que se completaron están bien. Si la escritura estaba rota,
  conviene saber si algún ZIP de prueba quedó mal. Por la regla de datos, no se tocan.

**D4. ¿Qué ve el perito si la verificación falla?**
Hoy Tatana manda un mensaje específico, pero el cliente lo reemplaza por el genérico "… Reintentá.", y en este bug
reintentar nunca sirve.
- A) **Solo backend: mejorar el mensaje de `zip_failed` cuando falla la verificación ("La evidencia está intacta, pero
  el ZIP armado no pasó el control de integridad. Si se repite, contactá a soporte.") y el log detallado. El cliente
  queda como está.**
- B) A más un cambio chico en `client/src/lib/agent-messages.ts` para mostrar el mensaje de Tatana (o un texto que no
  diga "Reintentá" cuando falla la verificación).
- C) Un código de error nuevo (p. ej. `zip_verification_failed`) con su propio texto en el cliente.
- **Recomendada: A.** Con el bug arreglado, este error debería ser rarísimo, y la HU es urgente y solo de backend.
  Mientras tanto, B o C abren un contrato client-server y un `implementer-frontend` extra. Si se quiere mejorar el
  texto en el cliente, va mejor en `wizard-concurrencia-pestanas`, que ya toca `GenerateStep`.

**D5. Tiempo de verificación.**
Pasar de lectura async a sync puede cambiar el tiempo con archivos grandes.
- A) **Que no empeore más de un 10 % con el juego de 5,5 GB del QA de #51 (o con uno de al menos 1 GB si ese no está a
  mano), medido antes y después y anotado en el progress.**
- B) Sin medir.
- **Recomendada: A.** #51 se hizo justamente para acelerar el ZIP, y este arreglo no debería deshacer esa mejora sin que
  nos enteremos. Con una sola medición alcanza.

**D6. ¿Esta HU sale sola hacia `main`?**
- A) **Sí: se cierra con prioridad y su PR contra `develop` se mergea apenas se apruebe. La promoción a `main` sigue
  esperando a C2 (grabación Android), como dice el INFORME.**
- B) Agruparla con otras HU del QA en un solo PR.
- **Recomendada: A.** Es chica, crítica y aislada (dos archivos y sus tests). Mezclarla con otras HU demora el arreglo y
  complica la revisión.

## Validación del usuario (2026-10-13)
Todas con la opción recomendada: D1 B (diagnosticar la escritura con 7zz/python antes; si está bien, lectura síncrona por bloques), D2 A (issue upstream con repro mínima), D3 A, D4 A (solo backend), D5 A (≤ 10 % más lento, medido), D6 A (sale sola).
- **D5 (2026-10-13, tras la medición):** la verificación correcta tarda ~1,7× (48 s vs 28 s en 5,5 GB; ZIP completo ~75 s vs 190 s antes de #51). El usuario aceptó el costo (opción a). D2: el usuario decidió no publicar el issue upstream; se quitaron los huecos de URL.
