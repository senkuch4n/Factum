# SDD: El ZIP de evidencia no recomprime archivos que ya vienen comprimidos

**Slug:** `zip-evidencia-sin-recomprimir` · **Issue:** #51
**HU:** `docs/hu-zip-evidencia-sin-recomprimir.md`. La validó el usuario en modo autónomo con las opciones
recomendadas: D1=A, D2=A, D3=A, D4=B, D5=A, D6=A, D7=A.
**Base de código:** `5320ca6` (`develop`). **Rama:** `feat/zip-evidencia-sin-recomprimir`.
**Asignación:** `implementer-backend`. No se lanza `implementer-frontend`.

> **Para el orquestador, en una línea:** el spike confirmó que Stored + AES-256 AE-2 + Zip64 Dynamic funciona en
> SharpZipLib 1.4.2 (lo abren 7-Zip, `bsdtar` y `python zipfile`). Ojo con la **decisión del arquitecto DA2**: el archivo
> de 3,5 GiB del QA se llamaba `.bin`, y con la lista de D2 sigue yendo con Deflate. La meta de D3 se mide con ese
> archivo renombrado a una extensión de la lista, y la corrida con el `.bin` tal cual se informa aparte, sin meta.

---

## 1. Resumen funcional

`EvidenceZip.WriteAsync` (las dos copias, la de Tatana y la del backend) deja de usar Deflate nivel 6 para todas las
entradas. Ahora elige el método por la extensión del archivo. Las extensiones de formatos que ya vienen comprimidos
(video, audio, imagen, archivos comprimidos) se guardan con **Stored**, sin recomprimir. El resto sigue con
**Deflate nivel 6**, igual que hoy. No cambia nada más: AES-256 WinZip AE-2 por entrada cuando hay contraseña,
`UseZip64.Dynamic`, nombres UTF-8, `FileMode.CreateNew`, `VerifyAsync` (descifra cada entrada y compara su SHA-256 con
el del manifiesto, sea cual sea el método), el SHA-256 del ZIP final (se sigue calculando en una pasada aparte, ya
cerrado el archivo), el rehash de la fase `hashing`, los eventos `zip_progress` y los contratos HTTP. La mejora sale
de no comprimir ni inflar los videos. En el spike, 1 GiB aleatorio con AES tardó 49 s en escribirse y 18 s en
verificarse con Deflate, contra 8 s y 10 s con Stored.

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | `Services/Reports/EvidenceZip.cs` (copia gemela, D1=A). |
| backend (Tatana) `server/src/Factum.Agent` | **sí** | `Services/Evidence/EvidenceZip.cs`. `CaseZipService.cs` **no** cambia. |
| tests .NET `server/tests/Factum.{Agent,Backend}.Tests` | **sí** | `EvidenceZipTests.cs` de los dos proyectos (que ya existen, ver §6). |
| client `client/` | **no** | D5=A. |
| agent-ui `agent-ui/` | **no** | — |
| Mongo / `Storage:DataDirectory` | **no** | No hay datos nuevos ni migraciones (D6=A). Los ZIP ya generados no se tocan. |

## 3. Modelo de datos

Sin cambios. `Case.ZipEncryption` sigue en `"aes256-ae2"`, porque el método de compresión no forma parte del cifrado.
No se agregan campos, índices ni colecciones. Los documentos existentes no se ven afectados.

## 4. Endpoints / mensajes WebSocket

Sin cambios de forma ni de semántica:

- Tatana: `POST /cases/{id}/zip` (`BuildZipRequest` → `BuildZipResponse`), `POST /cases/{id}/zip/commit` y el evento
  WebSocket `zip_progress` (`phase: hashing | zipping | verifying`, bytes por fase). La fase `zipping` sigue informando
  los bytes **leídos** del original (`onProgress(read)`), así que la barra avanza igual, solo que más rápido.
- Backend: `ReportService.GenerateAsync` (flujo viejo) usa `EvidenceZip.WriteAsync/VerifyAsync` con la misma firma.

## 5. Contrato compartido

**No cruza lados.** No se agrega, renombra ni quita ningún campo JSON (snake_case_lower) en
`client/src/types/` ↔ `server/src/Factum.Backend/DTOs/`, ni en el contrato `client/` ↔ Tatana
(`server/src/Factum.Agent/...` ↔ `client/src/lib/agent*`/`GenerateStep.tsx`). Si el implementador se encuentra
cambiando un DTO o un evento, se salió del alcance y tiene que pararse (`blocked`).

---

## 6. Proyectos de tests: qué hay de verdad

`AGENTS.md` dice "Todavía no hay proyectos de tests en .NET", pero **está desactualizado**. Ya existen:

- `server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj` (xUnit 2.9.3, referencia a `Factum.Agent`,
  `InternalsVisibleTo` ya configurado), con `EvidenceZipTests.cs` (305 líneas) y `CaseZipServiceTests.cs`.
- `server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj` (xUnit 2.9.3, referencia a `Factum.Backend`,
  `InternalsVisibleTo` ya configurado), con `EvidenceZipTests.cs` (303 líneas).

Los tests nuevos van en esos dos `EvidenceZipTests.cs`, uno en cada proyecto y con el mismo contenido (la copia de
Tatana es "COPIA de" la del backend). No se crea ningún proyecto. Corregir esa línea de `AGENTS.md` es tarea
administrativa del orquestador y no la hace el implementador.

`MakeEvidence()` ya mezcla extensiones (`.png` y `.mp4`, que pasan a Stored, y `.txt`, que sigue con Deflate). Por
eso los tests que ya existen (ida y vuelta, contraseña incorrecta, hash que no coincide, `bsdtar`, etc.) pasan a cubrir
también las entradas Stored sin tocarlos, y **tienen que seguir pasando sin cambios en sus aserciones**.

---

## 7. Spike hecho por el arquitecto (SharpZipLib 1.4.2, .NET 10, macOS arm64)

Fue una app de consola descartable en el scratchpad, con la misma configuración de `ZipOutputStream` que
`EvidenceZip.WriteAsync` más `entry.CompressionMethod = CompressionMethod.Stored`. Ya está borrada. Resultados:

| Prueba | Resultado |
|---|---|
| Stored + `AESKeySize = 256` + `UseZip64.Dynamic` + `Size` seteado, sobre `FileStream` seekable | Funciona. No hace falta setear `Crc` ni `CompressedSize` a mano. |
| Tamaño de una entrada Stored cifrada | `CompressedSize = Size + 28` (16 de sal, 2 del verificador de contraseña y 10 del HMAC). Es lo que pide el Gherkin. |
| Extra field 0x9901 de una entrada Stored | vendor version 2 (AE-2), strength 3 (AES-256), **método real 0** (Stored). El header dice método 99, como en cualquier AES. |
| `ZipFile` (SharpZipLib) al releer | `e.CompressionMethod == Stored`, `IsCrypted`, `AESKeySize == 256`. El SHA-256 de `GetInputStream` coincide con el original. |
| 7-Zip (`7zz t -p…`) | "Everything is Ok". Con una contraseña incorrecta: "Wrong password". |
| `bsdtar --passphrase … -xf` | Extrae los archivos con el SHA-256 original. |
| `python3 zipfile` | Lee las entradas (`compress_type` 99). |
| Zip64: entrada Stored de 4,3 GiB + `.jpg` Stored que **empieza en el offset 4 613 734 520 (> 4 GiB)** | `7zz t` "Everything is Ok", `python zipfile` lee bien el offset y la verificación propia pasa. RSS máx. del proceso: 64 MB. |
| Entrada Stored cifrada con un byte alterado en el medio | **SharpZipLib no valida el HMAC al leer** (no tira excepción), pero el SHA-256 de la entrada no coincide, así que `VerifyAsync` falla con "no coincide con su hash". 7-Zip lo marca como error. **Consecuencia:** la detección de corrupción depende del SHA-256 del manifiesto, que ya es parte de `VerifyAsync`. No hay que cambiar nada, pero el test T5 lo fija. |
| Tiempos con 1 GiB aleatorio + `.txt` de 220 KB, AES-256 | Deflate: escritura 48,9 s + hash del ZIP 1,3 s + verificación 17,8 s. **Stored: 7,9 s + 1,1 s + 10,5 s.** |
| Buffer de 80 KB contra 1 MB (lectura, escritura y `FileStream`) | Sin diferencia que se pueda medir (Stored: 7,9 s contra 7,7 s de escritura y 10,5 s contra 9,4 s de verificación). |

Estimación para el juego del QA con las tres entradas en Stored (5,5 GB): rehash ~6 s, escritura ~40 s, hash del ZIP
~6 s y verificación ~50 s, unos **2 min**, contra los 8,9 min de hoy. Si el archivo de 3,5 GiB va con Deflate (`.bin`,
ver DA2), la estimación sube a unos 4,5–5 min.

---

## 8. Diseño

### 8.1 Regla de método por entrada (D2=A)

En las **dos** copias de `EvidenceZip` se agregan dos miembros `internal` (`InternalsVisibleTo` ya cubre a los tests):

```csharp
/// Extensiones (sin punto, en minúsculas) de formatos que ya vienen comprimidos: se guardan con Stored
/// (zip-evidencia-sin-recomprimir D2). El resto va con Deflate nivel 6.
internal static readonly FrozenSet<string> StoredExtensions = new[]
{
    // video
    "mp4", "mkv", "mov", "m4v", "webm", "3gp", "avi",
    // audio
    "mp3", "m4a", "aac", "ogg", "opus", "amr",
    // imagen
    "jpg", "jpeg", "png", "heic", "heif", "webp", "gif",
    // comprimidos / paquetes ("x.tar.gz" cae en "gz")
    "zip", "7z", "rar", "gz", "apk", "ipa",
}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

internal static CompressionMethod MethodFor(string fileName)
{
    var ext = Path.GetExtension(fileName);          // ".MP4", "" si no tiene
    return ext.Length > 1 && StoredExtensions.Contains(ext[1..])
        ? CompressionMethod.Stored
        : CompressionMethod.Deflated;
}
```

- Decide **solo por la extensión del nombre de la entrada** (`Path.GetFileName(path)`), sin mirar el contenido. Es
  predecible y fácil de testear (D2).
- Sin distinguir mayúsculas (`VIDEO.MP4`, `IMG_0001.JPG` y `.HEIC` de iOS van con Stored).
- `tar.gz` de la lista de la HU queda cubierto por `gz` (`Path.GetExtension("x.tar.gz") == ".gz"`). No hace falta un
  caso especial.
- El orden y los comentarios de la lista son los de arriba. La lista es **idéntica** en las dos copias.

### 8.2 Cambio en `WriteAsync` (las dos copias)

- Se mantiene `zos.SetLevel(CompressionLevel)` (6), que aplica a las entradas Deflate.
- Al armar cada `ZipEntry` se agrega `CompressionMethod = MethodFor(path)` (o el nombre ya calculado). Todo lo demás
  queda como está: `DateTime`, `Size` (regla 3, la necesita Zip64 Dynamic y también Stored), `IsUnicodeText` y
  `AESKeySize` (regla 2).
- El comentario de la constante `CompressionLevel` pasa a aclarar que solo aplica a las entradas Deflate.
- El `<summary>` de la clase y el de `WriteAsync` mencionan la regla Stored/Deflate y la HU.
- En la copia de Tatana, el comentario de cabecera ("COPIA de… Diferencia única: …") se mantiene. Sigue siendo cierto,
  porque la diferencia sigue siendo solo el callback de progreso.
- No se cambian los buffers (`81920`). Ver DA1.

### 8.3 `VerifyAsync`

**Sin cambios de lógica.** `ZipFile.GetInputStream` maneja Stored y Deflate, y el spike confirmó que una entrada
Stored corrupta se detecta por el SHA-256. Solo se actualiza el comentario del `<summary>`: "el SHA-256 de cada entrada
(descomprimida si corresponde)". No se agrega ningún control nuevo sobre el método (por ejemplo, "una `.mp4` tiene que
ser Stored"), porque convertiría un detalle de rendimiento en un motivo para abortar la generación.

### 8.4 `CaseZipService` y `ReportService`

No cambian. El orden de las fases, el cálculo del hash del ZIP (`_store.Sha256Async` sobre el ZIP ya cerrado), la
verificación y el manejo de errores (borrar `.part`, `.zip` y `.sha256` pendientes y conservar la evidencia) quedan
iguales.

---

## 9. Decisiones técnicas

**D-T1 (de la HU, D1=A). Las dos copias cambian juntas y con los mismos tests.** Ya se aplicó en §8.

**D-T2 (de la HU, D2=A). Regla por extensión con la lista fija de §8.1.** Ya se aplicó.

**D-T3 (de la HU, D5=A y D6=A). No se toca `client/`, `agent-ui/`, el informe ni la plantilla.** Ya se aplicó.

**DA1. Decisión del arquitecto: dentro de lo que permite D4=B, no se hace ninguna optimización extra.**
- *Buffers más grandes:* el spike no mostró ninguna mejora que se pueda medir (§7). Cambiarlos sería un cambio sin
  beneficio.
- *SHA-256 del ZIP mientras se escribe:* **no se hace.** Primero, la pasada aparte cuesta ~1,1 s/GiB, unos 6 s sobre
  5,5 GB, poco frente al resto. Segundo, `ZipOutputStream` sobre un stream seekable puede volver atrás (Seek) para
  parchar los headers locales al cerrar cada entrada, así que el hash de los bytes "tal como se escribieron" podría no
  coincidir con el del archivo final. Eso rompería el hash que va al informe, y es justo el control de cadena de
  custodia que D4 prohíbe tocar. Hashear el archivo ya cerrado es la opción conservadora.
- Si la corrida manual (§11) no llega a la meta de D3 con la configuración de DA2, el implementador **no** improvisa
  otras optimizaciones. Informa los tiempos por fase en su progress y se decide una HU nueva (AES de SharpZipLib, que
  descifra a ~100–200 MB/s).

**DA2. Decisión del arquitecto (más conservadora, explícita): con qué juego se mide la meta de D3.**
El archivo de 3,5 GiB del QA es `qa_subida_larga.bin` (`dd if=/dev/urandom`), y `.bin` **no** está en la lista de D2.
Con D2=A tal cual, ese archivo sigue con Deflate, y es el que más pesa en el tiempo de hoy. Para no agrandar la lista
validada por el usuario (`.bin` puede ser un volcado que comprime bien), se mide así:
- **Corrida M1 (la que cuenta para la meta ≤ 4,5 min):** el mismo juego del QA (mismos tamaños: MKV 732 379 184 B,
  MP4 1 024 626 930 B y 3 758 096 384 B aleatorios), pero con el archivo grande nombrado con una extensión de la lista
  (`qa_subida_larga.zip`). Representa el caso real que motiva la HU: un archivo grande que ya viene comprimido.
- **Corrida M2 (informativa, sin meta):** el mismo juego con `qa_subida_larga.bin` tal cual. Deja a la vista el costo
  que queda para los binarios fuera de la lista.
- Si el usuario prefiere que `.bin` (u otras, como `.ab` o `.tar`, que la HU dejó como pregunta abierta en D2) vaya con
  Stored, es agregar un string a la lista de las dos copias y un caso al test T1. Queda como mejora posterior, **no**
  se hace en esta HU.

**DA3. Decisión del arquitecto: la corrupción de una entrada Stored cifrada se detecta por el SHA-256, no por el HMAC.**
SharpZipLib 1.4.2 no valida el código de autenticación AES al leer (§7). No se agrega una validación propia del HMAC:
sería tocar el cifrado (D4 prohíbe la opción C) y el SHA-256 contra el manifiesto ya cubre el escenario del Gherkin.
El test T5 fija este comportamiento.

**DA4. Decisión del arquitecto: el test de 7-Zip es opcional, como el de `bsdtar`.** Si `7zz` no está en la PC, el
test se omite sin aserciones (mismo patrón que `Bsdtar_OpensWithPassword_FailsWithout`). Zip64 > 4 GiB **no** se
cubre con tests automáticos (escribiría más de 4 GiB en cada `dotnet test`). Va en la corrida manual M1.

No hay decisiones abiertas que necesiten al usuario.

---

## 10. Checklist atómico — `implementer-backend`

> **Regla dura de datos de desarrollo (`AGENTS.md`):** la MongoDB de desarrollo, la carpeta `Storage:DataDirectory`
> del backend, el `agent-data` de Tatana y la carpeta de evidencia (`EvidenceDirectory`) pueden tener datos cargados a
> mano por el usuario, y **no son descartables**. No borrás ni modificás nada que no hayas creado vos. Las pruebas
> limpian **solo** las rutas o `_id` que ellas mismas crearon, nunca con un filtro amplio ni con un `rm -rf` sobre una
> carpeta compartida. Esta HU no necesita escribir en Mongo.
> Además: no mover tarjetas del Project, no correr `ops/harness/hu.mjs` y no tocar `progress/sesiones/`.

### Código

- [ ] B1. `server/src/Factum.Agent/Services/Evidence/EvidenceZip.cs`: agregar `using System.Collections.Frozen;`,
      `StoredExtensions` y `MethodFor` exactamente como en §8.1.
- [ ] B2. Lo mismo en `server/src/Factum.Backend/Services/Reports/EvidenceZip.cs`, con lista y lógica idénticas.
- [ ] B3. En `WriteAsync` de las dos copias: `CompressionMethod = MethodFor(path)` en el inicializador del `ZipEntry`.
      No cambiar `SetLevel`, `UseZip64`, `Password`, `Size`, `IsUnicodeText`, `AESKeySize` ni los buffers.
- [ ] B4. Actualizar los comentarios (§8.2 y §8.3) en las dos copias: la constante `CompressionLevel`, el `<summary>`
      de la clase, `WriteAsync` y `VerifyAsync`.
- [ ] B5. `diff` entre las dos copias: las únicas diferencias tienen que seguir siendo la cabecera "COPIA de…", el
      `namespace` y el callback `onProgress` (igual que en `5320ca6`).
- [ ] B6. `dotnet build server/src/Factum.Agent/Factum.Agent.csproj` y
      `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` sin errores ni warnings nuevos.

### Tests (en los dos `EvidenceZipTests.cs`, con el mismo contenido)

- [ ] T0. `ReadAesExtra` pasa a devolver también el método real (`data[5] | (data[6] << 8)`). Ajustar el único uso
      que hay (`RoundTrip_Encrypted`) sin cambiar lo que ya afirma.
- [ ] T1. `MethodFor` (`[Theory]`): cada una de las 26 extensiones de la lista da `Stored`. También `VIDEO.MP4`,
      `IMG_0001.JPG`, `foto.HEIC` y `backup.tar.gz` dan `Stored`. En cambio `notas.txt`, `datos.xml`, `mmssms.db`,
      `x.sqlite`, `a.json`, `volcado.bin`, `extraccion.tar`, `sin_extension` y `punto.` dan `Deflated`.
- [ ] T2. `RoundTrip_Encrypted_MixedMethods`: con `MakeEvidence()` y contraseña, `WriteAsync` + `VerifyAsync` pasan.
      Al reabrir con `ZipFile`, `.png`/`.mp4` tienen `CompressionMethod == Stored`, extra 0x9901 con método real 0 y
      `CompressedSize == Size + 28`. `.txt` tiene `Deflated` y método real 8. Todas `IsCrypted` y `AESKeySize == 256`.
- [ ] T3. `RoundTrip_Plain_MixedMethods`: sin contraseña, las Stored tienen `CompressedSize == Size` y
      `!IsCrypted`. La `.txt` (el contenido Unicode de `MakeEvidence`) es `Deflated`. `VerifyAsync(null)` pasa.
- [ ] T4. `Deflate_StillCompresses`: un `.txt` de ~256 KB con texto repetido termina en el ZIP con
      `CompressedSize < Size / 4` (con y sin contraseña). Es la regresión de "los comprimibles siguen comprimidos".
- [ ] T5. `Verify_CorruptStoredEntry_Throws` (`[Theory]` con contraseña y sin ella): ZIP con una sola entrada
      `evidencia.mp4` de 256 KiB aleatorios. Se invierte un byte en `fileLength / 2` (cae en los datos de la entrada) y
      `VerifyAsync` tira `EvidenceZipVerificationException`. El mensaje no contiene la contraseña. Se trabaja sobre una
      copia dentro de `_root`.
- [ ] T6. `SevenZip_TestsMixedZip` (opcional según DA4): busca `7zz` en `/opt/homebrew/bin`, `/usr/local/bin` y
      `/usr/bin`. Si no está, el test se omite sin aserciones. Si está, corre `7zz t -p<Password> <zip>` sobre el ZIP de
      `MakeEvidence()` y espera exit 0 y "Everything is Ok". Con `-pWRONGWRONGWRONG2` espera exit distinto de 0. Mismo
      patrón de proceso que `RunBsdtar` (stdin cerrado, timeout de 60 s, `ArgumentList`).
- [ ] T7. Los tests que ya existen en los dos `EvidenceZipTests.cs` y `CaseZipServiceTests.cs` pasan **sin cambiar sus
      aserciones**.
- [ ] T8. Todos los tests trabajan en `Path.GetTempPath()/<guid>` (el `_root` que ya existe) y lo borran en `Dispose`.
      No escriben en `agent-data`, en `EvidenceDirectory` ni en `Storage:DataDirectory`.

### Medición manual (D3, D7, DA2): ver §11

- [ ] M0. Antes de empezar: `df -h` del volumen. Hacen falta ~25 GB libres (5,5 GB de juego más un ZIP por corrida).
- [ ] M1/M2/M-base. Las corridas de §11, con los números en `progress/impl_backend_zip-evidencia-sin-recomprimir.md`.
- [ ] M-limpieza. Borrar solo lo que creó la medición (§11.4) y dejar constancia del `ls` de antes y de después.

### Cierre

- [ ] C1. Escribir `progress/impl_backend_zip-evidencia-sin-recomprimir.md` con: archivos tocados, salida resumida de
      `dotnet build` y `dotnet test` de los dos proyectos, la tabla de tiempos por fase (base, M1 y M2), la RSS máxima,
      los resultados de `7zz t` y `python3 zipfile` y la limpieza verificada.
- [ ] C2. Devolver `done -> progress/impl_backend_zip-evidencia-sin-recomprimir.md` (o `blocked -> …`).

---

## 11. Verificación

### 11.1 Comandos obligatorios antes del `done`

```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
```

Los dos `dotnet test` corren la suite **completa** de cada proyecto, no solo `EvidenceZip`.

### 11.2 Juego de prueba (en un directorio temporal propio)

Todo se genera en un directorio nuevo de la sesión (el scratchpad o `mktemp -d`), **nunca** dentro del repo ni en
`agent-data`:

```bash
W=$(mktemp -d)/zip-sin-recomprimir && mkdir -p "$W"
head -c 732379184  /dev/urandom > "$W/grabacion_qa_android.mkv"   # o ffmpeg, ver abajo
head -c 1024626930 /dev/urandom > "$W/grabacion_ios_qa.mp4"       # o ffmpeg, ver abajo
dd if=/dev/urandom of="$W/qa_subida_larga.zip" bs=4m count=896    # 3 758 096 384 B
```

- Tamaños exactos del QA: `.mkv` 732 379 184 B, `.mp4` 1 024 626 930 B y el grande 3 758 096 384 B
  (`dd if=/dev/urandom bs=4m count=896`).
- Para el `.mkv` y el `.mp4` vale **ffmpeg** con los comandos del QA (H.264, unos 6 min de generación) **o** `dd` desde
  `/dev/urandom` con esos tamaños (`head -c <bytes> /dev/urandom > …`). El ZIP solo mira la extensión, y los datos
  aleatorios son tan incompresibles como el H.264, así que la medición no cambia. Elegí `dd` si el tiempo pesa, y
  anotá en el progress cuál usaste.
- El archivo grande se crea **una vez** y se renombra entre corridas: `qa_subida_larga.zip` para M1 y
  `qa_subida_larga.bin` para M2.

### 11.3 Corridas

Se mide **de punta a punta en `POST /cases/{id}/zip` de Tatana**, como el QA (`docs/hu-…` §"Qué midió el QA"), con
contraseña, en esta misma PC:

1. Levantar Tatana desde la rama (`dotnet run --project server/src/Factum.Agent`, o como lo haga el entorno local) y
   anotar su `agent-data` y su `EvidenceDirectory` efectivos (`appsettings*.json`, `/health` o los logs de arranque).
2. Usar un **caso nuevo**: un GUID inventado como `{id}` y `case_ref` `QA-sin-recomprimir`. Antes de empezar,
   comprobar que no existen `agent-data/cases/{id}/` ni la carpeta del caso en `EvidenceDirectory`.
3. Meter los 3 archivos en el caso con el mismo camino que el QA (`POST /cases/{id}/evidence/import` desde `agent-data`
   o `POST /cases/{id}/evidence/upload` con `curl -T`), con el header `Origin` que acepte Tatana.
4. `time curl -X POST … /cases/{id}/zip` con `{case_ref, zip_filename, password, files:[{filename,size,sha256}]}`. Al
   mismo tiempo, escuchar el WebSocket `zip_progress` (o mirar los logs) para anotar **cuándo empieza cada fase**
   (`hashing`, `zipping`, `verifying`). Muestrear la RSS de Tatana (`ps -o rss= -p <pid>` cada 2 s, por ejemplo).
5. Sobre el ZIP pendiente (`<EvidenceDirectory>/<carpeta del caso>/.meta/pending/<zip>`):
   `7zz t -p<password> <zip>` ("Everything is Ok", `Characteristics = Zip64`) y
   `python3 -c "import zipfile;…infolist()"` (3 entradas, offset de la 3.ª > 4 GiB).
6. **M-base** (recomendada, para que la comparación sea justa en esta PC): la misma corrida M1 con el código de
   `develop` (un `git worktree` temporal de `origin/develop` fuera del repo, que se borra al final). Si no se hace,
   la base es el 534,7 s del QA y se aclara en el progress.

| Corrida | Archivo grande | Aceptación |
|---|---|---|
| M1 | `qa_subida_larga.zip` (Stored) | **Total ≤ 270 s (4,5 min)**, RSS máx. de Tatana < 200 MB, `7zz t` OK, Zip64 OK, verificación propia OK. |
| M2 | `qa_subida_larga.bin` (Deflate) | Sin meta. Se informan los números y el ZIP tiene que ser válido igual. |
| M-base | `qa_subida_larga.zip` con `develop` | Solo de referencia. |

Si M1 no llega a 270 s: el implementador **no** sigue optimizando (DA1). Deja la tabla por fase y declara `done`
igual, con el desvío explicado al principio del progress, para que el orquestador lo plantee al usuario.

### 11.4 Limpieza (regla dura de datos)

Al terminar, y **solo por rutas exactas creadas por la medición**:

- Del caso `{id}` de prueba: si se hizo `zip/commit` con `delete_files` (como el QA), borrar solo la carpeta final del
  caso `QA-sin-recomprimir` en `EvidenceDirectory`. Si no, cancelar o dejar sin confirmar y borrar
  `agent-data/cases/{id}/` y esa misma carpeta del caso (con su `.meta/pending`).
- El directorio temporal `$W` completo y el `git worktree` de M-base (`git worktree remove`).
- No se toca nada más en `agent-data`, `EvidenceDirectory`, Mongo ni `Storage:DataDirectory`. Dejar en el progress el
  `ls` de `agent-data/cases/` y de `EvidenceDirectory` de antes y de después, para mostrar que quedaron como estaban.

### 11.5 Prueba manual que queda para el usuario

1. En la web, generar un caso real con una grabación (`.mkv` de Android o `.mp4` de iOS), alguna captura `.png` y,
   si hay, un `device_pull_*.txt`/`.db`. Las fases "Verificando la evidencia / Armando el ZIP / Verificando el ZIP"
   se ven igual que antes y duran menos.
2. Abrir el ZIP resultante con 7-Zip o WinRAR en Windows y con Keka o The Unarchiver en macOS, con la contraseña: se
   extraen todos los archivos. Sin contraseña (o con una incorrecta), no se extrae ninguno.
3. Comprobar que el SHA-256 del ZIP que figura en el informe coincide con `shasum -a 256` / `certutil -hashfile … SHA256`
   del archivo.

---

## 12. Riesgos y notas

- **Lectores soportados:** AE-2 con método real Stored es parte de la especificación WinZip AES, y 7-Zip, `bsdtar`
  (libarchive) y `python zipfile` (lectura de headers) lo aceptaron en el spike. WinRAR, Keka y The Unarchiver se
  confirman en la prueba manual del usuario (§11.5). Keka usa 7-Zip por debajo y The Unarchiver soporta AE-x con
  Stored.
- **Hash del ZIP:** sigue siendo el SHA-256 del archivo final y cerrado, y cambia en cada generación igual que hoy
  (sal e IV aleatorios). No hay regresión para el informe.
- **Fuera de alcance que vale la pena anotar:** en la fase `verifying` de Tatana no hay progreso por bytes (ni el hash
  del ZIP ni `VerifyAsync` llaman a `progress.Add`), así que la barra queda quieta durante esa fase. Ya pasa hoy, no
  lo cambia esta HU (D5=A) y, con Stored, la fase dura menos. Si molesta, es otra HU.
- `AGENTS.md` dice que no hay proyectos de tests .NET, y sí los hay (§6). Lo corrige el orquestador.
