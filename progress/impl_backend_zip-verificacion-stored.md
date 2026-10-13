# impl_backend — zip-verificacion-stored (HU #58)

**Estado:** `done` (sin commit, como pidió el orquestador; rama `feat/zip-verificacion-stored`, HEAD `36140d4`).

## ⚠ Desvío de D5 (leer primero)

**La verificación nueva tarda ~1,7× la vieja** en el juego del QA de #51 (5,5 GB). La meta era ≤ 1,10×.
Sigue en `done` porque la SDD (§9.2) lo indica así: el orquestador lo tiene que plantear al usuario.

| Corrida (macOS, mismo ZIP de 5 515 104 046 B) | Vieja (`HashDataAsync`, `useAsync: true`) | Nueva (`Read` síncrono, `useAsync: false`) | Nueva con `useAsync: true` |
|---|---|---|---|
| 1 | 26,61 s | 47,88 s | 46,39 s |
| 2 | 28,43 s | 47,22 s | 45,56 s |
| 3 | 27,78 s | 48,63 s | 45,81 s |
| **Mediana** | **27,78 s** | **47,88 s (1,72×)** | **45,81 s (1,65×)** |
| RSS máx. | 59 MB | 47 MB | 47 MB |
| CPU (user/real) | ~50 s / 28 s (varios hilos) | ~46 s / 48 s (un hilo) | ~44 s / 46 s |

- **Causa (con evidencia, no es el I/O):** por reflexión sobre SharpZipLib 1.4.2, `ZipAESStream.Read(byte[],int,int)`
  → `ReadAndTransform` → `TransformAndBufferBlock` trabaja de a `CRYPTO_BLOCK_SIZE = 16` bytes: un append de
  HMAC-SHA1 + transform por cada bloque de 16 B. El camino viejo (`ReadAsync(Memory)` → `CryptoStream.ReadAsyncCore`)
  transforma de a bloques grandes y superpone trabajo en varios hilos, **pero es justamente el que da datos
  erróneos**. La versión nueva queda CPU-bound en un solo hilo (user ≈ real). El tamaño de buffer no cambia nada
  (1 MiB: 52,7 s). `ReadAsync(byte[],int,int,ct)`, que es correcto, es todavía más lento (67,9 s).
- **Fallback de D-T2 (`useAsync: true`) probado:** con el mismo loop, alternados, `false` vs `true` dan medianas
  46,21 s vs 46,07 s (empate). En macOS no cambia nada. Queda `false`, que es lo que pidió el arquitecto por Windows.
- **RSS:** cumple. Unos 47 MB fijos, sin crecer con el tamaño de la entrada (antes 59 MB).
- **Throughput resultante en esta Mac:** ~115 MB/s en la fase `verifying` (5,5 GB en ~48 s, contra ~28 s antes).
  Producción es Windows: no lo medí ahí.
- **Opciones para plantearle al usuario** (ninguna implementada; quedan fuera del alcance validado):
  (a) aceptar el costo, porque es correcto, y ahora además controla el HMAC (ver hallazgo abajo);
  (b) verificar varias entradas en paralelo, cada una con su `ZipFile`/`FileStream`. Solo ayuda si hay varias
  entradas grandes: en el juego del QA domina una de 3,7 GB;
  (c) un descifrador AES-CTR + HMAC propio por bloques grandes. Es más rápido pero reimplementa cripto: no lo
  recomiendo.

## Hallazgo adicional: con lectura síncrona, SharpZipLib sí valida el HMAC

Con `Read(byte[],…)`, al final de la entrada `ZipAESStream` compara el auth code y, si no cuadra, tira
`ZipException: AES Authentication Code does not match…`. Lo verifiqué invirtiendo un byte de un ZIP AES Stored.
El test T4 lo muestra: el ZIP corrupto ahora da **"no se puede leer"** (el HMAC salta antes que el SHA-256), y
el test acepta cualquiera de los dos mensajes. Corregí el `<summary>` de `VerifyAsync`, que decía "SharpZipLib no
valida el HMAC de AES al leer" (las dos copias). La nota de riesgo de la SDD §11 ("HMAC: sigue sin validarse") ya no
aplica: ahora se valida, sin cambios de código extra.

## Paso 0 — Diagnóstico de D1 → **Rama A (el ZIP está bien escrito)**

Repro propia en `scratchpad/zvs-backend/diag`, basada en `docs/qa/2026-10-13/ziprepro/`. El original del repo no
se tocó. SharpZipLib 1.4.2, .NET 10.0.301, macOS. Escritura **idéntica** a `WriteAsync` de Tatana: `FileStream`
async, `SetLevel(6)`, `UseZip64.Dynamic`, contraseña antes del primer `PutNextEntryAsync`, `Size`, `IsUnicodeText`,
`AESKeySize = 256`, `CloseEntryAsync`/`FinishAsync`. La corrí por los dos caminos: `CopyToAsync` (copy) y el bucle
de `onProgress` (prog). Contenido: `new Random(1)` en secuencia sobre 4095, 4096, 44214, 44216 y 44660, `.png`
Stored. Además, un ZIP con las 5 entradas + un `.txt` Deflate (multi). Contraseña `x123`.

| Tamaño | `HashDataAsync` (hoy) | `Read` síncrono por bloques | `7zz t -p` | `7zz x` + SHA-256 vs original | `zipfile` estructura | Sin contraseña (`zipfile` extrae + SHA-256) |
|---|---|---|---|---|---|---|
| 4095 | **`ZipException: Auth code missing from input stream`** | ok | Everything is Ok | igual | método 99, flag 0x809, csize = size + 28 | igual |
| 4096 | ok | ok | Everything is Ok | igual | idem | igual |
| 44214 | **hash distinto** | ok | Everything is Ok | igual | idem | igual |
| 44216 | **`Auth code missing`** | ok | Everything is Ok | igual | idem | igual |
| 44660 | ok | ok | Everything is Ok | igual | idem | igual |

- Iguales en los dos caminos de escritura (copy y prog) y en los ZIP multi (las 6 entradas: 7zz OK y SHA-256
  iguales; el `.txt` Deflate lee bien por las dos vías).
- SHA-256 de los originales: f4095 `f9c88054…a4dd6`, f4096 `79197076…68a95`, f44214 `e0de596b…cdc97`,
  f44216 `d3adccbc…a77007`, f44660 `c4ca2a6b…1cec82`.
- **Conclusión:** coincide con §3 de la SDD. Falla solo la lectura async (`ReadAsync(Memory)` no redefinido en
  `ZipAESStream`). `WriteAsync` **no se tocó** (B4 no aplica).

## Tests antes del arreglo (T8)

Para que T5 compilara contra el código viejo, hice el orden así: primero B2 (`Entry` en la excepción, que no
cambia la lectura), después los tests y después la corrida contra la lectura vieja (`HashDataAsync`).

- **Agent:** 5 fallan. `Sweep_RoundTrip_AllSizes(".png", true)`, `Sweep_QaSequence_Encrypted_Stored`,
  `Sweep_QaSequence_Encrypted_Stored_WithProgress`, `SevenZip_ExtractsSweepSizes` (falla el `VerifyAsync` previo,
  con 4095) y `Sweep_DetectsCorruption_Stored_Encrypted` (falla el `VerifyAsync` del ZIP sano, con 44214).
- **Backend:** los mismos 4, sin el de progreso, que es solo de Agent.
- Tamaños de `SweepSizes` que fallaban en `.png` cifrado, los mismos en los dos proyectos: **31, 4094, 4095,
  8191, 16383, 44216, 44218, 65535 y 81919** con "Auth code missing", y **44214** con hash distinto. `.png` sin
  contraseña y `.txt` (con y sin) pasaban.
- El canario T7 pasaba (detecta 4095, 44214 y 44216).

## Archivos tocados

- `server/src/Factum.Agent/Services/Evidence/EvidenceZip.cs` y `server/src/Factum.Backend/Services/Reports/EvidenceZip.cs`
  (B1–B3, mismo código):
  - `Sha256OfEntry`: `Read(byte[],0,n)` + `IncrementalHash`, `ct.ThrowIfCancellationRequested()` por bloque, con el
    comentario de D-T2 y el placeholder del issue upstream.
  - Un `byte[81920]` por llamada a `VerifyAsync`; `FileStream` con `useAsync: false`.
  - `EvidenceZipEntryDiagnostics` + `EvidenceZipVerificationException.Entry`, cargado en "no coincide con su hash"
    (con los dos hashes) y en "no se puede leer" (`ActualSha256 = null`).
  - `<summary>` actualizado. Los mensajes `ErrorPrefix…` no cambian, la firma pública no cambia y `WriteAsync` no
    se toca.
- `server/src/Factum.Agent/Services/CaseZipService.cs` (B5): `internal static string ZipFailedMessage(Exception)`
  con los textos exactos de D-T4, usado en el `throw` de `zip_failed`, y un `LogError` estructurado extra cuando
  hay `Entry`. Sin contraseña ni `request`.
- `server/src/Factum.Backend/Services/Reports/ReportService.cs` (B6): `catch (Exception ex)` con el mismo log
  (`cas.Id`, `cas.NroReferencia`). El `throw;`, la limpieza y los mensajes no cambian.
- `server/tests/Factum.Agent.Tests/EvidenceZipTests.cs` y `server/tests/Factum.Backend.Tests/EvidenceZipTests.cs`:
  - T1 `SweepSizes`, T2 `Sweep_RoundTrip_AllSizes` (4 combinaciones; junta todos los tamaños que fallan en el
    mensaje), T3 `Sweep_QaSequence_Encrypted_Stored`, T4 `Sweep_DetectsCorruption_Stored_Encrypted` (offset
    calculado con `e.Offset` + cabecera local + 16 de sal + 2 del verificador), T5 (amplía
    `Verify_HashMismatch_Throws`), T6 `SevenZip_ExtractsSweepSizes` y T7
    `SharpZipLib_AesReadAsyncMemory_StillBroken_SeeUpstreamIssue`.
  - `RunSevenZipTest` ahora delega en un `RunSevenZip(exe, params args)` genérico, y `SevenZipPath()` queda
    compartido.
- Solo Agent: T3b `Sweep_QaSequence_Encrypted_Stored_WithProgress` (suma de `onProgress` = suma de tamaños).
- `server/tests/Factum.Agent.Tests/CaseZipServiceTests.cs` (B7): `ZipFailedMessage_Verification_IsD4Text` (texto
  exacto, sin "Reintentá" ni "borr…") y `ZipFailedMessage_Other_IsGeneric`.

No toqué `client/`, `agent-ui/`, Mongo, `Storage:DataDirectory`, `agent-data`, `~/Factum/Evidencia`, el Project ni
`progress/sesiones/`. Los cambios previos de `progress/sesiones/senkuch4n.md` en el working tree no son míos. No
hizo falta levantar un Tatana propio, y no detuve el :8080 ni el :8765.

## Contrato

No cruza lados (SDD §6). No hay campos nuevos, renombrados ni sacados. Solo cambia el texto de `message` de
`500 zip_failed` cuando la causa es `EvidenceZipVerificationException`:
`"La evidencia está intacta, pero el ZIP armado no pasó el control de integridad. Si se repite, contactá a soporte."`.
El cliente no lee `message` (`client/src/lib/agent-messages.ts` mapea el código).

## B8 — diff entre copias (D-T11)

`diff Agent/EvidenceZip.cs Backend/EvidenceZip.cs`: solo lo de siempre.

```
1,5d0   < cabecera "// COPIA de Factum.Backend/... Diferencia única: WriteAsync acepta un callback..."
10c5    < namespace Factum.Agent.Services.Evidence;  --- > namespace Factum.Backend.Services.Reports;
70c65   < string? password, CancellationToken ct, Action<long>? onProgress = null)  --- > string? password, CancellationToken ct)
104,117c99  < if (onProgress is null) { await src.CopyToAsync(zos, ct); } else { bucle ReadAsync/WriteAsync/onProgress }
            --- > await src.CopyToAsync(zos, ct);
```

`diff Agent/EvidenceZipTests.cs Backend/EvidenceZipTests.cs`:

```
4c4     using Factum.Agent.Services.Evidence; / using Factum.Backend.Services.Reports;
8c8     namespace Factum.Agent.Tests; / namespace Factum.Backend.Tests;
11,12d10  cabecera "COPIA de server/tests/Factum.Backend.Tests/EvidenceZipTests.cs para la gemela de Tatana"
30c28   prefijo de _root: "factum-agent-evidencezip-" / "factum-evidencezip-"
565,578d562  test T3b Sweep_QaSequence_Encrypted_Stored_WithProgress (solo Agent)
```

## C1 — Barrido amplio (una vez, fuera del repo, código nuevo)

Programa de consola que compila la `EvidenceZip.cs` de Agent linkeada. Tamaños 0..8192 de a 1 y después de a 97
hasta 199 961 (10 170 tamaños), por `.png`/`.txt` × con/sin contraseña: **40 680 corridas `WriteAsync` +
`VerifyAsync`, 0 fallos**, en 2 min 9 s. Contenido `new Random(size)`.

## C2 — Medición de D5

Ver la sección de desvío arriba. Juego: `.mkv` 732 379 184 B, `.mp4` 1 024 626 930 B, `qa_subida_larga.zip`
3 758 096 384 B (los tres Stored, de `/dev/urandom`) + `notas.txt` 237 838 B (Deflate). El ZIP se armó una vez con
`WriteAsync` (34 s). Hubo 3 corridas alternadas de cada variante en procesos separados, con
`/usr/bin/time -l`. La vieja leyó todo bien con estos tamaños ("ok"), así que el tiempo es el de la lectura
completa. Se midió en esta Mac (Apple Silicon, SSD interno). Tatana corre en Windows en producción y ahí no se midió.

## C3 — D3

No aplica: la escritura estaba bien (Rama A). No se revisó ni se tocó ningún ZIP existente.

## C5 — Verificación §9.1

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj      → 0 Error(s), 0 Warning(s)
dotnet build server/src/Factum.Backend/Factum.Backend.csproj  → 0 Error(s), 4 Warning(s) (NU1902 SharpCompress / NU1903 Snappier, preexistentes)
dotnet test  server/tests/Factum.Agent.Tests                   → Passed! Failed 0, Passed 337, Skipped 0, Total 337
dotnet test  server/tests/Factum.Backend.Tests                 → Passed! Failed 0, Passed 888, Skipped 7, Total 895
```

Los 7 omitidos son `MongoAdminIntegrationTests` (no hay variable de entorno, preexistente). Los tests de 7zz y
bsdtar corrieron con aserciones (7zz "Everything is Ok" en `SevenZip_TestsMixedZip` y
`SevenZip_ExtractsSweepSizes`, en los dos proyectos). Barrido de la suite: ~250 ms por combinación (< 10 s). La
compilación de los tests no suma warnings: los CS8714/CS8619 de `MakeEvidence` (líneas 58-59) ya estaban.

## C6 — Limpieza

Todo lo temporal estuvo en `scratchpad/zvs-backend/` (10 GB) y en un `$TMPDIR/Hym8us` que creó
`Directory.CreateTempSubdirectory()` en la repro upstream.

- Antes: `b1.py b2.py b56.py bench d5 diag out refl sweep t3b.cs tests_block.cs tins.py upstream zi.py`, y en
  `d5/src`: `grabacion_android.mkv grabacion_ios.mp4 notas.txt qa_subida_larga.zip`.
- Después: `rm -rf` por ruta exacta. `ls scratchpad | grep -c zvs-backend` → 0, y `Hym8us` → "No such file or
  directory". Los tests limpian su `_root` (no queda ningún `factum-*evidencezip*` en `$TMPDIR`). Disco libre al
  final: 55 GiB.

## Prueba manual pendiente para el usuario (SDD §9.3)

1. Tatana desde la rama, caso con contraseña que tenga una captura `.png` de 44 214 B y un video: "Generar" anda
   al primer intento.
2. Abrir el ZIP con 7-Zip/Keka: se extrae todo y el SHA-256 de la captura coincide con el del informe.
3. Si se publica el issue, pasar la URL para reemplazar `<URL cuando el usuario lo publique>` en las dos
   `EvidenceZip.cs` y la constante `UpstreamIssue` de los dos `EvidenceZipTests.cs`.

## Texto del issue upstream (D2=A), final. No se publicó.

Corrí la repro de §10 **tal cual**, con escritura síncrona: dio exactamente lo mismo que con escritura async. La
lista "Observed" queda igual; solo se ajusta la frase de la escritura.

Antes de publicarlo, buscar en https://github.com/icsharpcode/SharpZipLib/issues "ZipAESStream ReadAsync" por si ya
existe. No tiene datos de casos.

**Título:** `ZipAESStream does not override ReadAsync(Memory<byte>, CancellationToken): async reads of AES entries return wrong data or "Auth code missing"`

**Cuerpo:**

````markdown
### Version
SharpZipLib 1.4.2, .NET 10 (also expected on .NET 6+), macOS and Windows.

### Description
`ZipAESStream` (derived from `CryptoStream`) overrides `Read(byte[], int, int)` and
`ReadAsync(byte[], int, int, CancellationToken)` to strip the trailing 10-byte HMAC (AE-1/AE-2 auth code),
but it does not override `ReadAsync(Memory<byte>, CancellationToken)`. Any consumer that uses the `Memory<byte>`
overload — e.g. `SHA256.HashDataAsync(stream)`, `Stream.CopyToAsync` on modern runtimes, `PipeReader` — goes
straight to `CryptoStream.ReadAsyncCore`, bypassing the auth-code handling.

Result, for AES-256 entries written with `CompressionMethod.Stored` (with Deflate the inflater reads synchronously
on top, so it is not visible): depending on the entry size, the data read is wrong, or
`ZipException: Auth code missing from input stream` is thrown. The archive itself is valid:
`7z t` reports "Everything is Ok" (including the HMAC check) and 7-Zip extracts the original bytes.

### Repro
```csharp
using ICSharpCode.SharpZipLib.Zip;
using System.Security.Cryptography;

var dir = Directory.CreateTempSubdirectory().FullName;
var rnd = new Random(1);
foreach (var size in new[] { 4095, 4096, 44214, 44216, 44660 })
{
    var data = new byte[size]; rnd.NextBytes(data);
    var zip = Path.Combine(dir, $"{size}.zip");
    using (var zos = new ZipOutputStream(File.Create(zip)) { Password = "x123" })
    {
        zos.PutNextEntry(new ZipEntry("f.png")
            { CompressionMethod = CompressionMethod.Stored, Size = size, AESKeySize = 256 });
        zos.Write(data, 0, data.Length);
        zos.CloseEntry();
    }
    using var zf = new ZipFile(zip) { Password = "x123" };
    var expected = Convert.ToHexString(SHA256.HashData(data));
    string viaAsync;
    try { using var s = zf.GetInputStream(zf[0]); viaAsync = Convert.ToHexString(await SHA256.HashDataAsync(s)); }
    catch (Exception ex) { viaAsync = ex.Message; }
    using var s2 = zf.GetInputStream(zf[0]);
    var viaSync = Convert.ToHexString(SHA256.HashData(s2));
    Console.WriteLine($"{size}: async {(viaAsync == expected ? "ok" : "WRONG: " + viaAsync)}, sync {(viaSync == expected ? "ok" : "WRONG")}");
}
```

Observed with 1.4.2 on .NET 10.0.301 / macOS (identical results whether the archive is written synchronously,
as above, or with `PutNextEntryAsync`/`WriteAsync`/`CloseEntryAsync`):
- 4095: async → `Auth code missing from input stream`, sync ok
- 4096: ok / ok
- 44214: async → wrong hash, sync ok
- 44216: async → `Auth code missing from input stream`, sync ok
- 44660: ok / ok

### Expected
`ReadAsync(Memory<byte>, CancellationToken)` (and ideally `Read(Span<byte>)`) behave like `Read(byte[], int, int)`.

### Workaround
Read the entry stream with `Read(byte[], int, int)` (or `ReadAsync(byte[], int, int, CancellationToken)`).

### Side note (performance)
`Read(byte[], int, int)` goes through `ReadAndTransform`/`TransformAndBufferBlock`, which transform and feed the
HMAC 16 bytes at a time; on a 5.5 GB archive the synchronous path is ~1.7x slower than the (broken)
`CryptoStream` async path. Transforming larger aligned chunks per call would help.
````

(La "Side note" de performance es un agregado mío sobre el texto del arquitecto; si no se quiere mezclar temas en
el issue, se borra ese bloque y el resto queda igual.)
