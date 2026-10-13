# HU: El ZIP de evidencia no recomprime archivos que ya vienen comprimidos

**Slug:** `zip-evidencia-sin-recomprimir` · **Issue:** #51
**Apps afectadas (preliminar, lo confirma la SDD):** agente Tatana (`server/src/Factum.Agent/Services/Evidence/EvidenceZip.cs`,
quizás `CaseZipService.cs`). Según D1, también la copia del backend (`server/src/Factum.Backend/Services/Reports/EvidenceZip.cs`).
`client/` y `agent-ui/` sin cambios previstos (ver D5).

**Como** perito que genera un caso con videos largos
**quiero** que el ZIP cifrado de evidencia se arme en bastante menos tiempo
**para que** no tenga que esperar casi 10 minutos frente a "Armando el ZIP / Verificando el ZIP" sin que eso aporte nada:
los videos e imágenes ya vienen comprimidos y comprimirlos otra vez no achica el ZIP.

Origen: hallazgo H3 del QA de grabación larga y subida grande (2026-10-12).

---

## Contexto

### Qué existe hoy

- **Armado del ZIP en Tatana** (`POST /cases/{id}/zip`, `CaseZipService.BuildAsync`, `CaseZipService.cs:114-231`), en
  tres fases que se informan por WebSocket (`zip_progress`, `phase: hashing | zipping | verifying`; textos en
  `client/src/components/dashboard/GenerateStep.tsx:47-49`):
  1. `hashing`: recalcula el SHA-256 de cada archivo del caso y lo compara con el manifiesto.
  2. `zipping`: `EvidenceZip.WriteAsync` con SharpZipLib 1.4.2. **Todas las entradas van con Deflate nivel 6**
     (`EvidenceZip.cs:25` y `:39`, `zos.SetLevel(6)`), sin distinguir el tipo de archivo; AES-256 WinZip AE-2 por
     entrada si hay contraseña, `UseZip64.Dynamic` y nombres UTF-8 (`EvidenceZip.cs:38-82`).
  3. `verifying`: SHA-256 del ZIP completo y `EvidenceZip.VerifyAsync`, que reabre el ZIP, descifra e **infla** cada
     entrada y compara su SHA-256 con el del manifiesto (`EvidenceZip.cs:93-169`).
- **Copia en el backend.** `EvidenceZip.cs` está duplicado a propósito (decisión D-T1 de `zip-local-informe-servidor`,
  comentario en `Factum.Agent/.../EvidenceZip.cs:1-5`): "un cambio en una de las dos copias va también en la otra (y
  en sus tests)". En el backend lo usa solo `ReportService.GenerateAsync` (`ReportService.cs:144,154`), que es el
  **flujo viejo** que quedó para los borradores con evidencia ya subida al servidor (D8 de `zip-local-informe-servidor`).
  Tiene el mismo nivel 6 fijo (`Factum.Backend/.../EvidenceZip.cs:20,34`), así que tiene el mismo problema.
- **Tests existentes** (los dos proyectos, `server/tests/Factum.{Agent,Backend}.Tests/EvidenceZipTests.cs`): ida y vuelta
  cifrado y en claro, contraseña incorrecta, hash que no coincide, entradas de más o de menos, cifrado esperado vs. real,
  no pisar un archivo existente, el hash del ZIP no cambia al verificar, y apertura con `bsdtar` con y sin contraseña.
- **Lectores soportados** (`docs/hu-zip-cifrado-real.md`): 7-Zip, WinRAR, Keka y The Unarchiver. El Explorador de
  Windows y la Utilidad de Archivo de macOS ya hoy no abren ZIP AES.

### Qué midió el QA (`H3`)

Caso de 3 archivos, 5,5 GB en total (MKV 732 MB, MP4 1 GB, binario aleatorio de 3,5 GiB), AES-256, Zip64:
- **534,7 s (~8,9 min)** en total: rehash + escritura ~5,5 min, verificación ~3,4 min.
- Compresión inútil: el binario de 3,5 GiB quedó **más grande** (3 759 242 712 B > 3 758 096 384 B) y el MKV ganó 0,6 %.
- El mismo ZIP lo verifica `7zz t` en 10,7 s, así que el cuello de botella está en nuestro armado/verificación, no en el
  disco. Memoria de Tatana < 170 MB (eso está bien y se tiene que mantener).
- El ZIP resultante pasó la verificación propia, `python3 zipfile` (offsets > 4 GiB por Zip64) y `7zz t`.

### Qué es nuevo

El ZIP elige el método por entrada: los archivos que ya vienen comprimidos (video, audio, imagen, archivos
comprimidos) se guardan **sin recomprimir** (método *Stored*), cifrados igual con AES-256; el resto (texto, XML, bases
de datos, volcados sin compresión) sigue con Deflate. Todo lo demás (cifrado, Zip64, verificación contra el
manifiesto, nombres UTF-8, hash del ZIP, contrato con el cliente) queda igual.

**Advertencia para la SDD:** sacar Deflate elimina compresión e inflado, pero no el cifrado AES ni el HMAC-SHA1 de
SharpZipLib, ni el rehash previo ni el SHA-256 del ZIP. Que 7-Zip verifique en 10 s sugiere que parte del tiempo puede
venir del AES de SharpZipLib. Antes de comprometer el diseño conviene medir cuánto aporta cada fase (ver D3 y D4).

---

## Criterios de aceptación

```gherkin
Feature: ZIP de evidencia sin recompresión de archivos ya comprimidos

  Background:
    Given Tatana corriendo en la PC del perito
    And un caso con evidencia guardada en la carpeta del caso de Tatana

  Scenario: Video y archivos ya comprimidos se guardan sin recomprimir
    Given la evidencia incluye un .mkv, un .mp4, un .jpg y un .zip
    When el perito genera el caso con contraseña
    Then cada una de esas entradas queda en el ZIP con método Stored y cifrada con AES-256 (WinZip AE-2)
    And el tamaño de cada entrada en el ZIP es su tamaño original más solo la sobrecarga fija de AES (sal, verificador y HMAC)

  Scenario: Archivos comprimibles siguen comprimidos
    Given la evidencia incluye un .txt, un .xml o un .db
    When el perito genera el caso
    Then esas entradas quedan con Deflate, igual que hoy

  Scenario: Generación notablemente más rápida con videos grandes
    Given el mismo juego de prueba del QA (MKV 732 MB + MP4 1 GB + archivo de 3,5 GiB, total > 4 GiB) en la misma PC
    When se arma y verifica el ZIP con contraseña
    Then el tiempo total de POST /cases/{id}/zip baja al menos a la meta de D3
    And la memoria de Tatana se mantiene en el mismo orden que hoy (< 200 MB)

  Scenario: Zip64 sigue funcionando
    Given evidencia cuyo total supera 4 GiB, con una entrada Stored que empieza después de los 4 GiB
    When se arma el ZIP
    Then el ZIP es Zip64 válido
    And la verificación propia de Tatana pasa
    And `7zz t -p<contraseña>` informa "Everything is Ok"

  Scenario: La verificación sigue cubriendo las entradas sin comprimir
    Given un ZIP armado con entradas Stored y Deflate
    When Tatana lo verifica
    Then descifra cada entrada y compara su SHA-256 con el del manifiesto, sin importar el método
    And si una entrada Stored está corrupta o no coincide, la generación falla como hoy y la evidencia queda intacta

  Scenario: Compatibilidad con los lectores soportados
    Given un ZIP cifrado con entradas Stored y Deflate
    When se abre con 7-Zip, WinRAR, Keka o The Unarchiver con la contraseña
    Then se extraen todos los archivos con su SHA-256 original
    And sin contraseña no se extrae ninguno

  Scenario: ZIP sin contraseña
    Given la configuración de la instalación no cifra el ZIP
    When se genera el caso
    Then aplica la misma regla de métodos por tipo de archivo, sin cifrado

  Scenario: Nada cambia para el resto del sistema
    When se genera un caso
    Then el contrato de POST /cases/{id}/zip y /zip/commit, los eventos zip_progress y los datos guardados en el expediente no cambian
    And el hash del ZIP sigue siendo el SHA-256 del archivo final, escrito en el informe
```

---

## Datos que se registran

No hay datos nuevos. `zip_encryption` sigue en `aes256-ae2` (el método de compresión por entrada no es parte del
cifrado). Si en D6 se decide dejar constancia del método en el informe, se agrega acá.

---

## Diseño UX/UI

Sin cambios de pantalla. En `client/` (web) el paso "Generar" sigue mostrando las mismas fases ("Verificando la
evidencia", "Armando el ZIP", "Verificando el ZIP"); solo duran menos. `agent-ui/` (Electron) no cambia. Ver D5 sobre
el peso relativo de las fases en la barra de progreso.

---

## Fuera de alcance

- Hallazgos H1 y H2 del mismo QA (post-proceso MCI de grabaciones iOS, evento de falla de la interpolación): son HU
  propias.
- Cambiar de librería de ZIP, de algoritmo de cifrado (AES-256 AE-2 se mantiene) o de formato (7z, tar, etc.).
- Paralelizar el armado o partir el ZIP en volúmenes.
- Cambiar el rehash de la fase `hashing` (se mantiene como control de cadena de custodia), salvo lo que decida D4.
- Recomprimir o transcodificar los videos.
- Migrar o regenerar ZIP de casos ya completados: los ZIP existentes no se tocan (un caso `completed` no se regenera).
- Cambios en `client/` o `agent-ui/` (salvo lo que decida D5).

---

## Notas de implementación (mínimas; el detalle es de la SDD)

- SharpZipLib 1.4.2: `ZipEntry.CompressionMethod = CompressionMethod.Stored` por entrada. Con *Stored* SharpZipLib
  puede necesitar `Size`/`Crc` antes de escribir si el stream no es *seekable*; acá es un `FileStream` (seekable) y
  `Size` ya se setea. Lo que la SDD tiene que confirmar con un test o un spike **antes de fijar el diseño**: que
  Stored + AES-256 + Zip64 Dynamic funcionen juntos en esta versión y que el ZIP lo abran 7-Zip, `bsdtar` y
  `python zipfile`.
- La regla Stored/Deflate se decide por extensión (D2). Es la misma en las dos copias de `EvidenceZip.cs` si D1 = A.
- `VerifyAsync` no necesita cambios de lógica: `GetInputStream` maneja los dos métodos. Conviene sumar un test que
  verifique que una entrada Stored cifrada se valida y que una corrupta falla.
- Medir antes y después con el mismo juego de prueba del QA (D3) y dejar los números en el progress del implementador.
- Regla dura de datos (`AGENTS.md`): las pruebas limpian solo lo que crearon en `agent-data`, en la carpeta de
  evidencia y en `Storage:DataDirectory`.

---

## Dudas para validar con el usuario

> Cada duda tiene una opción **Recomendada** con su porqué.

**D1. ¿El ZIP del backend entra en el alcance?**
La copia `Factum.Backend/Services/Reports/EvidenceZip.cs` tiene el mismo nivel 6 fijo. Hoy solo la usa el flujo viejo
(borradores con evidencia subida al servidor antes de `zip-local-informe-servidor`).
- A) **Sí: el mismo cambio en las dos copias**, con sus tests.
- B) Solo Tatana; el backend queda como está.
- C) Solo Tatana, y se aprovecha para eliminar el flujo viejo del backend.
- **Recomendada: A.** Las dos copias están marcadas para cambiar juntas; si divergen, el próximo cambio en una se
  copia mal a la otra. El costo es chico (una constante y un `if` más los mismos tests). C es otra HU: depende de si
  quedan borradores viejos, que el usuario pidió conservar.

**D2. Qué formatos se guardan sin recomprimir.**
- A) **Lista fija de extensiones ya comprimidas**: video (`mp4`, `mkv`, `mov`, `m4v`, `webm`, `3gp`, `avi`), audio
  (`mp3`, `m4a`, `aac`, `ogg`, `opus`, `amr`), imagen (`jpg`, `jpeg`, `png`, `heic`, `heif`, `webp`, `gif`) y
  comprimidos/paquetes (`zip`, `7z`, `rar`, `gz`, `apk`, `ipa`, `tar.gz`); el resto con Deflate.
- B) Todo Stored (sin compresión para nada).
- C) Heurística por contenido: comprimir un primer bloque y, si no baja de cierto porcentaje, usar Stored.
- **Recomendada: A.** Cubre los archivos que pesan (grabaciones, fotos, extracciones del celular), es predecible y fácil
  de testear. B agranda los ZIP con muchos `.txt`/`.xml`/bases SQLite de extracciones, que sí comprimen bien. C es más
  general pero agrega lógica y casos raros a un componente de cadena de custodia. ¿Hay algún otro formato frecuente en
  sus casos (p. ej. `.ab` de backup Android, `.tar` de extracciones) que deban sumar?

**D3. Meta de tiempo.**
Hoy: 8,9 min para el juego de 5,5 GB del QA (ZIP de 5 512 429 180 B).
- A) **Bajar al menos a la mitad (≤ 4,5 min) con el mismo juego y la misma PC**, medido de punta a punta en
  `POST /cases/{id}/zip`.
- B) Sin meta numérica: alcanza con que sea "más rápido".
- C) Una meta más agresiva (p. ej. ≤ 2 min), aunque obligue a tocar el cifrado o el rehash.
- **Recomendada: A.** Sacar Deflate debería dar ese orden de mejora, pero el QA no separó cuánto tiempo era Deflate y
  cuánto AES/HMAC de SharpZipLib, así que una meta más agresiva puede no alcanzarse solo con este cambio. Si con Stored
  no se llega, se reporta con números y se decide (ver D4) en vez de agrandar la HU sobre la marcha.

**D4. Si Stored solo no alcanza la meta, ¿qué más se permite tocar dentro de esta HU?**
- A) **Nada más: se entrega la mejora de Stored con la medición por fase, y lo que falte (AES de SharpZipLib, rehash,
  verificación) va a una HU nueva.**
- B) Permitir también optimizaciones sin cambio de formato ni de controles (buffers más grandes, calcular el SHA-256
  del ZIP mientras se escribe en lugar de releerlo).
- C) Permitir además reemplazar el cifrado AES de SharpZipLib por uno propio o cambiar de librería.
- **Recomendada: B.** Son cambios de bajo riesgo que no alteran el formato del ZIP ni sacan ningún control de la
  cadena de custodia (el rehash contra el manifiesto y la verificación completa se mantienen). C toca la parte más
  delicada (el cifrado) y merece su propia HU con su propio QA de compatibilidad.

**D5. Peso de las fases en la barra de progreso.**
La barra del paso "Generar" usa los bytes de cada fase. Al acelerarse "Armando el ZIP", la proporción entre fases
cambia, pero la barra sigue avanzando por bytes.
- A) **No tocar el cliente.** La barra sigue funcionando igual; solo dura menos.
- B) Ajustar textos o pesos de las fases en `GenerateStep.tsx`.
- **Recomendada: A.** No hay un problema visible que resolver; mantener la HU solo en backend.

**D6. ¿El informe debe decir que los videos se guardaron sin comprimir?**
- A) **No.** El método de compresión es un detalle interno del contenedor; el informe ya declara el cifrado AES-256 y
  los SHA-256 de cada archivo y del ZIP, que es lo que importa para la integridad.
- B) Sí, una línea en el informe ("los archivos ya comprimidos se almacenan sin recompresión").
- **Recomendada: A.** El hash de cada archivo original es idéntico con o sin compresión, y agregar texto al informe
  implica tocar la plantilla v6 y el backend sin necesidad.

**D7. Cómo se verifica.**
- A) **Tests automáticos en los dos proyectos** (entrada Stored cifrada ida y vuelta, mezcla Stored + Deflate, Stored
  corrupta detectada, apertura con `bsdtar`) **+ una corrida manual con el juego del QA** (> 4 GiB, Zip64), midiendo
  tiempo por fase y probando el ZIP con `7zz t` y `python3 zipfile`.
- B) Solo tests automáticos.
- **Recomendada: A.** Zip64 con entradas > 4 GiB y la meta de tiempo no se cubren con tests chicos; la corrida manual
  ya está armada en el QA y se repite tal cual.
