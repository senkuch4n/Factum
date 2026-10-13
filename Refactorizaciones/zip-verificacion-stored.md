# SDD: El ZIP de evidencia se genera y verifica bien con cualquier tamaño de archivo

**Slug:** `zip-verificacion-stored` · **Issue:** #58 · **HU:** `docs/hu-zip-verificacion-stored.md`
**Validación del usuario (2026-10-13):** todas con la recomendada. D1=B, D2=A, D3=A, D4=A, D5=A, D6=A.
**Fuentes:** `docs/qa/2026-10-13/e2e-navegador.md` (fila C1), `docs/qa/2026-10-13/ziprepro/Program.cs`,
`Refactorizaciones/zip-evidencia-sin-recomprimir.md` (HU #51, PR #54).

## 1. Resumen funcional

Desde el PR #54 las imágenes, los videos y los archivos comprimidos van al ZIP de evidencia con **Stored**. Con
contraseña (AES-256 AE-2), `EvidenceZip.VerifyAsync` hashea cada entrada con
`SHA256.HashDataAsync(zf.GetInputStream(e), ct)`, y para ciertos tamaños da un hash distinto o tira
`ZipException: Auth code missing from input stream`. Por eso `POST /cases/{id}/zip` responde `500 zip_failed`
siempre y el perito queda trabado (en el QA solo se destrabó borrando una captura). La HU corrige la verificación
para que lea la entrada por bloques con `Read` síncrono, sin cargarla en memoria. Primero se confirma con 7-Zip que el
ZIP escrito es correcto. Además suma tests de regresión con barrido de tamaños en las dos copias, un mensaje de
`zip_failed` más claro cuando falla la verificación (solo en el backend de Tatana) con log detallado, una medición de
tiempo y el texto del issue para SharpZipLib. El formato del ZIP, el cifrado, la regla Stored/Deflate y todos los
controles de la verificación quedan como están.

## 2. Toca

- **backend (API) `server/src/Factum.Backend`:** **sí.** Solo la copia `Services/Reports/EvidenceZip.cs` y un log en
  `Services/Reports/ReportService.cs`. Sin endpoints ni DTOs nuevos.
- **backend (Tatana) `server/src/Factum.Agent`:** **sí.** `Services/Evidence/EvidenceZip.cs` y
  `Services/CaseZipService.cs` (mensaje y log de `zip_failed`).
- **tests:** **sí.** `server/tests/Factum.Agent.Tests/EvidenceZipTests.cs`,
  `server/tests/Factum.Backend.Tests/EvidenceZipTests.cs`, `server/tests/Factum.Agent.Tests/CaseZipServiceTests.cs`.
- **client:** **no** (D4=A).
- **agent-ui:** **no.**

Un solo implementador: **`implementer-backend`**.

## 3. Diagnóstico previo del arquitecto (D1, lo tiene que confirmar el implementador)

Para escribir esta SDD corrí una reproducción mínima en un directorio temporal fuera del repo, con SharpZipLib 1.4.2
y .NET 10.0.301 en macOS. Escribí el ZIP igual que `WriteAsync`: async, una entrada `.png` Stored con AES-256 y
`Random(1)` en secuencia sobre los tamaños 4095, 4096, 44214, 44216 y 44660. Leí cada entrada de tres formas:

| Tamaño | `SHA256.HashDataAsync(stream)` (hoy) | `SHA256.HashData(stream)` (sync) | `ReadAsync(byte[],int,int,ct)` | `7zz t -p` | `7zz x` + SHA-256 |
|---|---|---|---|---|---|
| 4095 | **`ZipException: Auth code missing from input stream`** | ok | ok | Everything is Ok | igual al original |
| 4096 | ok | ok | ok | Everything is Ok | igual |
| 44214 | **hash distinto** | ok | ok | Everything is Ok | igual |
| 44216 | **`Auth code missing`** | ok | ok | Everything is Ok | igual |
| 44660 | ok | ok | ok | Everything is Ok | igual |

`python3 zipfile` (estructura): método 99 (AES), flag 0x809 (cifrado + UTF-8) y `compress_size = file_size + 28`
(16 de sal, 2 del verificador y 10 del HMAC). Es lo que corresponde a Stored con AE-2.

**Causa.** Por reflexión, `ICSharpCode.SharpZipLib.Encryption.ZipAESStream` (hereda de `CryptoStream`) redefine
`Read(byte[],int,int)` y `ReadAsync(byte[],int,int,CancellationToken)`, que son los que separan los 10 bytes finales del
HMAC. **No** redefine `ReadAsync(Memory<byte>, CancellationToken)`. `SHA256.HashDataAsync` usa la sobrecarga de
`Memory<byte>`, que va directo a `CryptoStream.ReadAsyncCore` y saltea esa lógica: el HMAC se trata como dato cifrado
(o falta, según cómo caigan los bloques), y de ahí el hash distinto o el "Auth code missing". Con Deflate no se notaba
porque el inflador lee con `Read(byte[],…)` síncrono por encima.

**Conclusión preliminar:** el ZIP está **bien escrito**. Falla solo la lectura async de la verificación. Por D1=B el
implementador lo confirma él mismo en el paso 0 del checklist antes de arreglar, y deja la evidencia en su progress.

## 4. Modelo de datos

No hay cambios. No se tocan colecciones, campos ni índices. `zip_encryption` sigue en `aes256-ae2`. No se escribe en
Mongo ni en `Storage:DataDirectory`.

## 5. Endpoints / mensajes WebSocket

No hay cambios de forma. `POST /cases/{id}/zip`, `POST /cases/{id}/zip/commit` y los eventos `zip_progress` siguen
iguales: mismos campos, códigos y fases. Lo único que cambia es el **texto** de `message` en la respuesta
`500 zip_failed` cuando la causa es `EvidenceZipVerificationException` (D4, ver D-T4). El código `zip_failed` y la
forma del error no cambian.

## 6. Contrato compartido

**No cruza lados.** El cliente (`client/src/lib/agent-messages.ts:102-103`) mapea `zip_failed` a su propio texto
genérico y no lee `message`, así que el cambio de texto de D-T4 no lo afecta. No se agregan, renombran ni sacan campos
en ningún JSON.

## 7. Decisiones técnicas

**D-T1 (D1=B). Primero el diagnóstico, después el arreglo según lo que salga.** Rama de decisión explícita:

- **Rama A: el ZIP está bien** (esperado según §3: `7zz t` da "Everything is Ok" y `7zz x` da los mismos SHA-256 que
  los originales en los tamaños que fallan). Se arregla **solo la lectura** en `VerifyAsync` (D-T2). `WriteAsync` no
  se toca.
- **Rama B: el ZIP está mal** (`7zz t` da error de CRC/HMAC/datos, o algún SHA-256 extraído no coincide). Además de
  D-T2, se cambia la escritura de la entrada en `WriteAsync` a llamadas **síncronas** en las dos copias:
  `PutNextEntry`, un bucle de `src.Read(buffer,0,n)` + `zos.Write(buffer,0,n)` (+ `onProgress(n)` en Tatana) y
  `CloseEntry`/`Finish`. El `FileStream` de destino se abre con `useAsync: false`. Se cancela con
  `ct.ThrowIfCancellationRequested()` en cada bloque. Después se vuelve a correr el paso 0 con el código nuevo y
  tiene que dar todo OK. Y entra D3 (§D-T6).
- **Rama C: no es concluyente** (por ejemplo, 7zz da OK pero el `7zz x` difiere, o la escritura falla con tamaños
  distintos de los de lectura): el implementador **para y devuelve `blocked`** con los datos en su progress. No se
  improvisa.

**D-T2. Lectura síncrona por bloques en `VerifyAsync` (las dos copias, mismo código).** Se reemplazan las líneas que
hoy hacen `await SHA256.HashDataAsync(entryStream, ct)` por un helper privado:

```csharp
// SharpZipLib 1.4.2: ZipAESStream no redefine ReadAsync(Memory<byte>, …), así que la lectura async
// (p. ej. SHA256.HashDataAsync) saltea el manejo del HMAC y da basura en entradas AES Stored según el
// tamaño (zip-verificacion-stored, QA C1). Se lee con Read(byte[], int, int), que sí está redefinido.
// Issue upstream: <URL cuando el usuario lo publique>.
private static string Sha256OfEntry(ZipFile zf, ZipEntry e, byte[] buffer, CancellationToken ct)
{
    using var s = zf.GetInputStream(e);
    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    int read;
    while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
    {
        ct.ThrowIfCancellationRequested();
        sha.AppendData(buffer, 0, read);
    }
    return Convert.ToHexStringLower(sha.GetHashAndReset());
}
```

- Un solo `byte[81920]` por llamada a `VerifyAsync`, reutilizado para todas las entradas. Nada de `MemoryStream` ni
  `ReadAllBytes`, así la memoria no depende del tamaño de la entrada.
- Los `catch` actuales (`ZipException` con "password" → "la contraseña no lo abre"; `SharpZipBaseException`/
  `IOException`/`InvalidDataException` → "no se puede leer") envuelven la llamada al helper igual que hoy.
- **Decisión del arquitecto:** se usa `Read` síncrono y **no** `ReadAsync(byte[],int,int,ct)`, aunque en §3 también
  dio bien. Es lo que validó el usuario (D1=B, "lectura síncrona por bloques"), es el camino que usa el inflador de
  Deflate desde siempre y no depende de qué sobrecarga async redefina la librería.
- **Decisión del arquitecto:** el `FileStream` que abre `VerifyAsync` pasa a `useAsync: false`. `ZipFile` lee
  siempre en síncrono sobre ese stream, y en Windows una lectura síncrona sobre un handle abierto para async paga
  overlapped I/O + espera. Si la medición de D5 sale peor con este cambio que sin él, se vuelve a `useAsync: true` y
  se anota en el progress.
- La firma pública (`Task VerifyAsync(string, IReadOnlyDictionary<string,string>, string?, CancellationToken)`) **no
  cambia**: los llamadores (`CaseZipService`, `ReportService`) no se tocan por esto.

**D-T3. Diagnóstico estructurado en la excepción (para el log, D4).** `EvidenceZipVerificationException` (las dos
copias) suma una propiedad opcional, sin cambiar los mensajes de hoy:

```csharp
internal sealed record EvidenceZipEntryDiagnostics(
    string Name, long Size, string Method, bool Encrypted, string? ExpectedSha256, string? ActualSha256);

internal sealed class EvidenceZipVerificationException(
        string message, Exception? inner = null, EvidenceZipEntryDiagnostics? entry = null)
    : InvalidOperationException(message, inner)
{
    public EvidenceZipEntryDiagnostics? Entry { get; } = entry;
}
```

- Se carga en el `throw` de "no coincide con su hash" (con los dos hashes) y en "no se puede leer" (`ActualSha256 =
  null`). `Size = e.Size`, `Method = e.CompressionMethod.ToString()` (`Stored`/`Deflated`), `Encrypted = e.IsCrypted`.
- Los textos de `ErrorPrefix…` **no cambian** (los tests de hoy los usan). Ni el mensaje ni `Entry` llevan la
  contraseña.

**D-T4 (D4=A). Mensaje y log de `zip_failed` en Tatana.** En `CaseZipService.BuildAsync`, el `catch` de las líneas
214-224:

- El mensaje sale de un helper `internal static string ZipFailedMessage(Exception ex)` (testeable):
  - si `ex is EvidenceZipVerificationException` →
    **`"La evidencia está intacta, pero el ZIP armado no pasó el control de integridad. Si se repite, contactá a soporte."`**
    (texto exacto de D4; ya no se manda `ex.Message` al cliente).
  - si no → el genérico de hoy, sin cambios: `"No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta"`.
- Log: si es `EvidenceZipVerificationException` con `Entry` no nulo, **además** del `LogError` actual se loguea:
  `_log.LogError("Verificación del ZIP del caso {CaseId} falló: {Reason}. Entrada {Entry}, {Size} bytes, método {Method}, cifrada {Encrypted}, SHA-256 esperado {Expected}, obtenido {Actual}", …)`
  con `Reason = ex.Message`. Sin la contraseña ni el `request` entero.
- Este error solo puede pasar después del rehash OK (`hashing` va antes y corta con `409 evidence_changed`), así que
  "la evidencia está intacta" es cierto siempre que se muestra.

**D-T5. Backend `ReportService` (flujo viejo).** En el `catch` de `GenerateAsync` (líneas 163-177) se pasa a
`catch (Exception ex)` y, si `ex is EvidenceZipVerificationException { Entry: { } d }`, se loguea lo mismo que en
D-T4 con `logger.LogError` (con `CaseId`/número de caso si está a mano en ese scope) antes del `throw;`. El
comportamiento y los mensajes hacia el cliente **no cambian**.

**D-T6 (D3=A). ZIP ya generados.** En producción no hay (develop no se promovió). Si el paso 0 da **Rama A**, no se
revisa nada y se anota "D3: no aplica, la escritura estaba bien". Si da **Rama B**, el implementador corre
`7zz t -p<contraseña>` **solo en modo lectura** sobre los ZIP de desarrollo/QA armados desde #54 que encuentre y de
los que tenga la contraseña, y anota el resultado. **No los regenera, ni los mueve, ni los borra.** Si no tiene la
contraseña, anota cuáles quedaron sin revisar.

**D-T7. Tests de regresión (las dos copias, misma cobertura).** Ver checklist T1-T7. Criterios:

- Livianos: contenido con semilla fija, archivos ≤ 160 KiB, todo en la carpeta temporal del test (`_root`). Corren
  en cada `dotnet test`. Objetivo: < 10 s el barrido completo por proyecto.
- **Decisión del arquitecto:** no se agrega un barrido amplio marcado dentro de la suite (miles de tamaños). El
  implementador corre **una vez** un barrido amplio fuera del repo (0..200 000 B, paso 1 hasta 8 KiB y después cada
  97 B, por ejemplo) con el código nuevo y anota el resultado en su progress. Si en el futuro hace falta, se agrega.
- Se demuestra que el barrido detecta el bug: se escriben los tests **antes** del arreglo, se corren contra el
  `VerifyAsync` viejo y se anota en el progress qué tamaños fallan. Tiene que fallar al menos uno de Stored+AES. Si
  con la semilla elegida no falla ninguno, se suma la secuencia exacta de §3 (`new Random(1)` en orden sobre
  4095, 4096, 44214, 44216 y 44660), que sí falla.

**D-T8. Test canario de la librería.** Un test por proyecto,
`SharpZipLib_AesReadAsyncMemory_StillBroken_SeeUpstreamIssue`, reproduce la lectura con
`SHA256.HashDataAsync(zf.GetInputStream(e))` sobre la secuencia de §3 y **afirma que al menos un tamaño da mal**
(hash distinto o `ZipException`). Si alguien actualiza SharpZipLib y el bug está arreglado, el test falla con el
mensaje: "SharpZipLib ya lee bien con ReadAsync(Memory): ver issue <URL>; se puede borrar este test y evaluar volver
a async en VerifyAsync". **Decisión del arquitecto:** deja constancia ejecutable del motivo del workaround (D2) sin
bloquear nada hoy.

**D-T9 (D5=A). Medición.** Solo cambia la fase `verifying` (y, en la Rama B, también `zipping`). Por eso se mide
**`EvidenceZip.VerifyAsync` directo**, con un programa de consola en un directorio temporal fuera del repo que
referencia `Factum.Agent` o copia los dos métodos, antes y después. No hace falta el E2E por Tatana.
**Decisión del arquitecto:** mantiene la meta de D5 (≤ 10 % más lento) sin cargar el caso de 5,5 GB por la web.
Detalle en §9.2.

**D-T10 (D2=A). Issue upstream.** El texto está listo en §10 para que lo publique el usuario. El implementador
**no** lo publica. El comentario de D-T2 lleva `<URL cuando el usuario lo publique>` hasta que exista.

**D-T11. Copias iguales.** Después del cambio, el `diff` entre las dos `EvidenceZip.cs` tiene que mostrar solo lo de
hoy: la cabecera "COPIA de…", el `namespace` y el `onProgress` de `WriteAsync` (en la Rama B, el bucle con
`onProgress`). Lo mismo con los dos `EvidenceZipTests.cs`: solo `namespace`/`using`, la cabecera y el prefijo de
`_root`, más los tests propios del `onProgress` de Tatana (T3b).

**No necesitan al usuario.** Todas las decisiones abiertas se resolvieron con la opción conservadora y quedaron
marcadas como "decisión del arquitecto".

## 8. Checklist atómico — `implementer-backend`

**Regla dura de datos (AGENTS.md):** todo se hace en carpetas temporales propias (el `_root` de los tests o un
`mktemp -d`/scratchpad para el diagnóstico y la medición). No se tocan `agent-data`, la carpeta de evidencia de
Tatana, `Storage:DataDirectory`, Mongo ni los casos QA que quedaron en la base (`docs/qa/2026-10-13/INFORME.md`,
"Pendiente de limpieza"). Al final se borra solo lo que creó uno, por ruta exacta. No mover tarjetas del Project ni
tocar `progress/sesiones/`.

### Paso 0 — Diagnóstico de D1 (antes de tocar código)

- [ ] 0.1 En un directorio temporal fuera del repo, copiar `docs/qa/2026-10-13/ziprepro/` y adaptarlo (o escribir uno
      equivalente) para que **conserve** los ZIP. Tamaños `.png` Stored + AES-256 + contraseña: 4095, 4096, 44214,
      44216 y 44660 (secuencia de §3), más un ZIP con **varias** entradas en el orden del QA si se quiere. La escritura
      tiene que ser **idéntica** a `WriteAsync` de hoy (async, `CopyToAsync`, y una vez también con el bucle de
      `onProgress` de Tatana).
- [ ] 0.2 Por cada ZIP: SHA-256 con `HashDataAsync` (hoy), con `Read` síncrono por bloques y el esperado. Anotar
      cuáles fallan con el código de hoy.
- [ ] 0.3 `7zz t -p<contraseña> <zip>` → anotar si da "Everything is Ok" (7-Zip controla el HMAC de AE-2).
- [ ] 0.4 `7zz x -p<contraseña> -o<dir> <zip>` y `shasum -a 256` de cada extraído contra el original.
- [ ] 0.5 `python3 -I` con `zipfile`: `infolist()` (método 99, flag con bit 0, `compress_size == file_size + 28` en
      Stored). Para el contenido, repetir los mismos tamaños **sin contraseña** y extraer con `zipfile` comparando el
      SHA-256. (`zipfile` no descifra AES; el contenido cifrado se valida con `7zz x`.)
- [ ] 0.6 Escribir en `progress/impl_backend_zip-verificacion-stored.md` la tabla resultante y la rama elegida de
      D-T1 (A, B o C). Si es C → `blocked`.

### Tests primero (deben fallar con el código actual)

En `server/tests/Factum.Agent.Tests/EvidenceZipTests.cs` y, **con el mismo contenido**,
`server/tests/Factum.Backend.Tests/EvidenceZipTests.cs`:

- [ ] T1. Lista estática de tamaños del barrido (`SweepSizes`), como mínimo:
      `0, 1, 2, 15, 16, 17, 31, 32, 33, 4079, 4080, 4081, 4094, 4095, 4096, 4097, 4098, 4111, 4112, 4113, 8191, 8192,
      8193, 16383, 16384, 16385, 44208, 44210, 44212, 44214, 44216, 44218, 44660, 65535, 65536, 65537, 81919, 81920,
      81921, 163840, 163841`. Cubre los bloques AES de 16 B, los buffers de 4 KiB y 80 KiB y los tamaños del QA.
- [ ] T2. `[Theory]` `Sweep_RoundTrip_AllSizes(string ext, bool encrypted)` con `InlineData` de `(".png", true)`,
      `(".png", false)`, `(".txt", true)` y `(".txt", false)`, para tener Stored y Deflate, con y sin contraseña. Para
      cada tamaño de `SweepSizes`: archivo `f{size}{ext}` con `new Random(size).NextBytes` (semilla fija), un ZIP
      **por tamaño** con esa sola entrada, `WriteAsync` + `VerifyAsync` OK. Si falla, el mensaje del assert dice el
      tamaño. Además se comprueba `MethodFor` (Stored para `.png`, Deflated para `.txt`) leyendo `e.CompressionMethod`.
- [ ] T3. `[Fact]` `Sweep_QaSequence_Encrypted_Stored`: la secuencia exacta de §3 (`new Random(1)` en orden sobre 4095,
      4096, 44214, 44216, 44660) en **un solo ZIP** con las 5 entradas `.png` + un `.txt`, con contraseña. Debe
      verificar OK.
- [ ] T3b. **Solo Agent:** igual que T3 pero escribiendo con `onProgress` no nulo (el otro camino de `WriteAsync`).
      Se comprueba además que la suma de lo reportado sea igual a la suma de los tamaños.
- [ ] T4. `[Fact]` `Sweep_DetectsCorruption_Stored_Encrypted`: con 44214 B `.png` cifrado, se altera un byte en el
      medio del contenido (calculando el offset con `ZipFile` como el `Verify_CorruptStoredEntry_Throws` de hoy) y
      `VerifyAsync` tira `EvidenceZipVerificationException` con "no coincide con su hash" o "no se puede leer".
      Asegura que el arreglo no relaja el control.
- [ ] T5. Ampliar `Verify_HashMismatch_Throws`: además del mensaje, `ex.Entry` no es nulo y tiene `Name`, `Size`,
      `Method`, `Encrypted`, `ExpectedSha256` (el falso del test) y `ActualSha256` (el real). Ni `ex.Message` ni
      `ex.Entry.ToString()` contienen la contraseña.
- [ ] T6. Ampliar el test de 7zz (`SevenZip_TestsMixedZip` o uno nuevo `SevenZip_ExtractsSweepSizes`): ZIP con
      contraseña con entradas `.png` de 4095, 44214, 44216 y 44660 + un `.txt`, `7zz t` → "Everything is Ok", y
      `7zz x -p… -o<_root/x>` → el SHA-256 de cada extraído coincide con el original. Se saltea sin aserciones si
      `7zz` no está (como hoy).
- [ ] T7. Test canario D-T8 `SharpZipLib_AesReadAsyncMemory_StillBroken_SeeUpstreamIssue`.
- [ ] T8. Correr `dotnet test` de los dos proyectos **antes** del arreglo y anotar en el progress qué tests/tamaños
      fallan (tiene que fallar al menos T2 `(".png", true)` y T3 en los dos proyectos). Si no falla ninguno de
      T2/T3, revisar las semillas (D-T7) antes de seguir.

### Arreglo (las dos copias, mismo código)

- [ ] B1. `server/src/Factum.Agent/Services/Evidence/EvidenceZip.cs`: helper `Sha256OfEntry` de D-T2, un `byte[81920]`
      por llamada, reemplazo de las líneas 184-186, `useAsync: false` en el `FileStream` de la línea 136-137 y
      comentario con el puntero al issue upstream.
- [ ] B2. Misma clase: `EvidenceZipEntryDiagnostics` + `Entry` en `EvidenceZipVerificationException` (D-T3), cargado
      en los `throw` de "no coincide con su hash" y "no se puede leer". Actualizar el `<summary>` de `VerifyAsync`
      (lectura síncrona por bloques y el porqué).
- [ ] B3. Lo mismo (B1 y B2) en `server/src/Factum.Backend/Services/Reports/EvidenceZip.cs`.
- [ ] B4. **Solo si el paso 0 dio Rama B:** escritura síncrona en `WriteAsync` en las dos copias (D-T1, Rama B),
      manteniendo `onProgress` en Tatana, la cancelación por bloque, `Size`, `UseZip64.Dynamic`, `IsUnicodeText`,
      `AESKeySize` y la contraseña antes del primer `PutNextEntry`. Volver a correr el paso 0 con el código nuevo.
- [ ] B5. `server/src/Factum.Agent/Services/CaseZipService.cs`: `internal static string ZipFailedMessage(Exception)`
      con los textos exactos de D-T4, usarlo en el `throw` de la línea 220 y sumar el log estructurado de D-T4.
- [ ] B6. `server/src/Factum.Backend/Services/Reports/ReportService.cs`: log de D-T5 en el `catch` de `GenerateAsync`,
      sin cambiar el `throw;` ni ningún mensaje.
- [ ] B7. En `server/tests/Factum.Agent.Tests/CaseZipServiceTests.cs`: `ZipFailedMessage_Verification_IsD4Text` (con
      una `EvidenceZipVerificationException` da exactamente el texto de D4, que no contiene "Reintentá" ni sugiere
      borrar evidencia) y `ZipFailedMessage_Other_IsGeneric` (con `IOException` da el genérico de hoy).
- [ ] B8. `diff` de las dos `EvidenceZip.cs` y de los dos `EvidenceZipTests.cs`: solo las diferencias permitidas de
      D-T11. Pegar el `diff` en el progress.

### Cierre

- [ ] C1. Barrido amplio una sola vez, fuera del repo, con el código nuevo (D-T7). Anotar rango, paso, combinaciones
      y "0 fallos" (o los que haya, y en ese caso `blocked`).
- [ ] C2. Medición de D5 (§9.2), anotada en el progress.
- [ ] C3. D3 según D-T6, anotado en el progress.
- [ ] C5. Comandos de §9.1 en verde, con el resumen de `dotnet test` en el progress.
- [ ] C6. Limpieza: borrar por ruta exacta el directorio temporal del diagnóstico, del barrido y de la medición.
      Dejar el `ls` de antes y de después en el progress.
- [ ] C7. `progress/impl_backend_zip-verificacion-stored.md` completo (diagnóstico, rama, tests que fallaban antes,
      archivos tocados, diff de copias, medición, D3 y limpieza) y devolver `done -> progress/impl_backend_zip-verificacion-stored.md`.

## 9. Verificación

### 9.1 Comandos obligatorios antes del `done`

```bash
dotnet build server/src/Factum.Agent/Factum.Agent.csproj
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet test  server/tests/Factum.Agent.Tests/Factum.Agent.Tests.csproj
dotnet test  server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
```

Suites **completas**, no solo `EvidenceZip`. Los tests de `bsdtar`/`7zz` se saltean si la herramienta no está; en
esta Mac están las dos (`/usr/bin/bsdtar`, `/opt/homebrew/bin/7zz`) y tienen que correr con aserciones.

### 9.2 Medición de D5

- Juego: si hay ≥ 15 GB libres (`df -h`), los tamaños del QA de #51 (`.mkv` 732 379 184 B, `.mp4` 1 024 626 930 B y
  `qa_subida_larga.zip` 3 758 096 384 B, con `head -c`/`dd` desde `/dev/urandom`, ver
  `Refactorizaciones/zip-evidencia-sin-recomprimir.md` §11.2). Si no, un solo `.mp4` de 1 GiB + un `.txt` de 220 KB.
  Todo en un `mktemp -d`.
- Un ZIP con contraseña armado **una vez** con `WriteAsync`. Después, 3 corridas de `VerifyAsync` **viejo** (con
  `HashDataAsync` y `useAsync: true`, copiado en el programa de medición) y 3 del **nuevo**, alternadas. Se anota la
  mediana de cada uno y la RSS máxima del proceso (`/usr/bin/time -l` o `ps -o rss=` cada 2 s).
- Ojo: el viejo puede tirar "Auth code missing" o dar otro hash con estos tamaños. Igual vale el tiempo que tarda en
  leer la entrada hasta el final. Si corta antes de leer todo, se mide con `ReadAsync(byte[],int,int,ct)` como
  referencia async y se aclara.
- Aceptación: mediana nueva ≤ 1,10 × la vieja, y RSS sin crecer con el tamaño de la entrada (del orden de decenas de
  MB, no de GB). Si no se cumple: probar con `useAsync: true` (D-T2). Si igual no se cumple, `done` con el desvío
  explicado **al principio** del progress, para que el orquestador lo plantee al usuario.
- Se mide en esta Mac. Tatana corre en Windows en producción, y eso se aclara en el progress.

### 9.3 Prueba manual que queda para el usuario

1. Con Tatana levantado desde la rama, generar desde la web un caso con contraseña que tenga una captura `.png` de
   **44 214 B** (o la del hallazgo C1 si todavía existe en una carpeta propia del usuario) y algún video. El paso
   "Generar" termina bien al primer intento.
2. Abrir el ZIP resultante con 7-Zip/WinRAR (Windows) o Keka/The Unarchiver (macOS) con la contraseña: se extrae todo
   y el SHA-256 de la captura coincide con el del informe.
3. (Opcional) Publicar el issue de §10 en SharpZipLib y pasar la URL para el comentario del código y el test canario.

## 10. Texto del issue upstream (D2=A), para que lo publique el usuario

Antes de publicarlo, buscar en https://github.com/icsharpcode/SharpZipLib/issues por "ZipAESStream ReadAsync" por si
ya existe (en ese caso, comentar ahí con la repro). No contiene datos de casos.

**Título:** `ZipAESStream does not override ReadAsync(Memory<byte>, CancellationToken): async reads of AES entries return wrong data or "Auth code missing"`

**Cuerpo:**

```markdown
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

Observed with 1.4.2 (async write via `PutNextEntryAsync`/`WriteAsync`/`CloseEntryAsync`; same pattern expected
with sync write):
- 4095: async → `Auth code missing from input stream`, sync ok
- 4096: ok / ok
- 44214: async → wrong hash, sync ok
- 44216: async → `Auth code missing from input stream`, sync ok
- 44660: ok / ok

### Expected
`ReadAsync(Memory<byte>, CancellationToken)` (and ideally `Read(Span<byte>)`) behave like `Read(byte[], int, int)`.

### Workaround
Read the entry stream with `Read(byte[], int, int)` (or `ReadAsync(byte[], int, int, CancellationToken)`).
```

> Nota para el implementador: antes de dejar este texto como definitivo, correr la repro **tal cual** (escritura
> síncrona, como está arriba) y ajustar la lista "Observed" con lo que dé. Si con escritura síncrona los fallos caen
> en otros tamaños, se ponen esos. Si no falla ninguno, se cambia la repro a escritura async, que es la de §3. El
> texto final queda en el progress. No se publica.

## 11. Riesgos y notas

- **Rendimiento:** la lectura síncrona es la misma que ya usa el inflador para Deflate, así que no se espera que
  empeore. `useAsync: false` debería ayudar en Windows. Lo confirma D5.
- **Cancelación:** pasa a controlarse por bloque de 80 KiB (antes la manejaba `HashDataAsync`). La latencia es
  despreciable.
- **HMAC:** sigue sin validarse en nuestra verificación (fuera de alcance de la HU). El SHA-256 por entrada lo cubre,
  y T4 lo demuestra para Stored+AES.
- **Contraseñas vacías o largas (T-08/T-09):** no comparten el arreglo. Quedan fuera.
- **Cliente:** sigue mostrando el texto genérico de `zip_failed` ("… Reintentá."). Si se quiere el texto de D4 en
  pantalla, va en `wizard-concurrencia-pestanas` (D4).
