# impl_backend — zip-evidencia-sin-recomprimir (#51)

**Estado:** done · **Rama:** `feat/zip-evidencia-sin-recomprimir` (sin commit, como pidió el orquestador) · **Base:** `5320ca6`
**SDD:** `Refactorizaciones/zip-evidencia-sin-recomprimir.md` · **HU:** `docs/hu-zip-evidencia-sin-recomprimir.md`

**Resultado de la meta D3 (DA2):** M1 tardó **54,3 s** de punta a punta (meta ≤ 270 s). Contra develop en esta misma
PC (M-base, 190,5 s) es 3,5 veces más rápido. RSS máxima de Tatana: 158 MiB en M1 y 185 MiB en M1B (< 200 MB).

## Archivos tocados

| Archivo | Cambio |
|---|---|
| `server/src/Factum.Agent/Services/Evidence/EvidenceZip.cs` | `using System.Collections.Frozen;`, `StoredExtensions` (las 26 extensiones de §8.1, en el mismo orden y con los mismos comentarios) y `MethodFor` (los dos `internal`). `CompressionMethod = MethodFor(path)` en el `ZipEntry`. Comentarios actualizados: constante `CompressionLevel`, `<summary>` de la clase, `WriteAsync` y `VerifyAsync`. |
| `server/src/Factum.Backend/Services/Reports/EvidenceZip.cs` | Lo mismo, idéntico. |
| `server/tests/Factum.Agent.Tests/EvidenceZipTests.cs` | T0 a T6 (ver abajo). |
| `server/tests/Factum.Backend.Tests/EvidenceZipTests.cs` | Lo mismo, idéntico. |

No se tocaron `SetLevel`, `UseZip64`, `Password`, `Size`, `IsUnicodeText`, `AESKeySize`, los buffers, `VerifyAsync`
(salvo el comentario), `CaseZipService`, `ReportService`, ningún DTO ni ningún evento. Tampoco `client/` ni `agent-ui/`.

**B5:** el `diff` entre las dos copias de `EvidenceZip.cs` da las mismas 24 líneas que en `5320ca6`: la cabecera
"COPIA de…", el `namespace` y el callback `onProgress`. Los dos `EvidenceZipTests.cs` difieren solo en lo que ya
difería antes: el `using`, el `namespace`, el comentario "COPIA de" y el prefijo de `_root`.

## Tests

- **T0:** `ReadAesExtra` devuelve `(VendorVersion, Strength, Method)`, con el método en `data[5] | (data[6] << 8)`.
  `RoundTrip_Encrypted` descarta el método (`var (vendorVersion, strength, _)`) y no cambia lo que ya afirmaba.
- **T1:** `MethodFor_CompressedFormats_Stored` (`[Theory]`) cubre las 26 extensiones, `VIDEO.MP4`, `IMG_0001.JPG`,
  `foto.HEIC` y `backup.tar.gz`, cada una también con ruta completa. `MethodFor_OtherFormats_Deflated` cubre los
  9 casos de Deflated. Se agregó `StoredExtensions_HasTheD2List` (Count == 26), para que un cambio en la lista no
  pase sin que nadie lo note.
- **T2:** `RoundTrip_Encrypted_MixedMethods`.
- **T3:** `RoundTrip_Plain_MixedMethods`.
- **T4:** `Deflate_StillCompresses(true/false)`.
- **T5:** `Verify_CorruptStoredEntry_Throws(true/false)`.
- **T6:** `SevenZip_TestsMixedZip`. En esta PC `7zz` está en `/opt/homebrew/bin`, así que el test **corrió de verdad**:
  exit 0 y "Everything is Ok" con la contraseña, y exit distinto de 0 con la incorrecta.
- **T7/T8:** las aserciones de los tests anteriores no cambiaron. Todos los tests trabajan en el `_root` temporal que
  ya existía y lo borran en `Dispose`.

### Decisión no obvia (T2): las entradas vacías salen Stored igual

`MakeEvidence()` incluye `vacio.txt` de 0 bytes. SharpZipLib guarda las entradas vacías con Stored aunque se le pida
Deflated, así que la primera corrida de T2 falló en esa entrada. Esto ya pasaba en develop y no lo introduce esta
HU. T2 lo deja fijado:

- si `e.Size == 0`, se espera `vacio.txt` con Stored;
- la `.txt` con contenido (`device_pull_canción ñ.txt`) se sigue afirmando `Deflated` con método real 8, como pide
  la SDD.

`MethodFor` sigue devolviendo `Deflated` para `.txt`, y eso lo cubre T1.

## Build y tests

```
dotnet build server/src/Factum.Agent/Factum.Agent.csproj     -> 0 Warning(s), 0 Error(s)
dotnet build server/src/Factum.Backend/Factum.Backend.csproj -> 0 Error(s); 2 warnings NU1902/NU1903 (SharpCompress 0.30.1,
                                                                Snappier 1.0.0), avisos de NuGet que ya estaban, no son nuevos
dotnet test  server/tests/Factum.Agent.Tests    -> Passed!  Failed: 0, Passed: 301, Skipped: 0, Total: 301
dotnet test  server/tests/Factum.Backend.Tests  -> Passed!  Failed: 0, Passed: 880, Skipped: 7, Total: 887
```

Los 7 omitidos del backend son tests de integración de Mongo (`MongoAdminIntegrationTests`), que se omiten por
diseño y ya se omitían antes.

## Medición manual (§11): M-base, M1, M1B y M2

**Entorno:** esta PC (macOS arm64), con 58 GiB libres al empezar (M0).

**Juego de prueba:** generado con `head -c` / `dd` desde `/dev/urandom` (no con ffmpeg) en un directorio temporal
propio dentro del scratchpad de la sesión. Tamaños exactos del QA: `.mkv` 732 379 184 B, `.mp4` 1 024 626 930 B y el
grande 3 758 096 384 B (`dd bs=4m count=896`).

**Cómo se levantó Tatana:**

- Una instancia propia por corrida: `dotnet <bin>/Factum.Agent.dll --port 879x --data <tmp>/data --mock --local-config <tmp>/no-local.json`
  con `Agent__EvidenceDirectory=<tmp>/evid`.
- Se compiló en Release con `-o <tmp>/bin-new` (rama) y `-o <tmp>/bin-base` (un `git worktree` temporal de
  `origin/develop` = `5320ca6`).
- Así, tanto `agent-data` como `EvidenceDirectory` quedaron dentro del directorio temporal, y la `~/Factum/Evidencia`
  real y el `agent-data` del repo no se usaron. Los logs de arranque confirmaron las dos carpetas.

**Procedimiento de cada corrida:**

- Un caso nuevo con un GUID inventado y `case_ref` `QA-sin-recomprimir`. Antes de cada corrida se comprobó que la
  carpeta del caso no existiera.
- Los 3 archivos entraron con `POST /cases/{id}/evidence/import`, que hace un rename desde la raíz de `--data`.
- Después, `POST /cases/{id}/zip` con contraseña.
- Las fases se tomaron de un cliente WebSocket (`/ws`) que anota el timestamp de cada cambio de `phase` en
  `zip_progress`.
- La RSS se muestreó con `ps -o rss=` cada 2 s.
- El ZIP quedó pendiente, sin commit.

| Corrida | Código | Archivo grande (orden) | hashing | zipping | verifying (hash del ZIP + Verify) | **Total** | RSS máx. | Métodos (7zz) |
|---|---|---|---|---|---|---|---|---|
| M-base | develop `5320ca6` | `qa_subida_larga.zip` (QA) | 2,1 s | 134,8 s | 53,6 s | **190,5 s** | 188 MiB | 3× AES-256 Deflate |
| **M1** | rama | `qa_subida_larga.zip` (QA) | 2,1 s | 24,5 s | 27,6 s | **54,3 s** | 158 MiB | 3× AES-256 Store |
| M1B | rama | `qa_subida_larga.zip` (el grande primero) | 2,3 s | 22,4 s | 28,4 s | **53,2 s** | 185 MiB | 3× AES-256 Store |
| M2 (informativa) | rama | `qa_subida_larga.bin` (QA) | 2,5 s | 96,5 s | 45,8 s | **144,9 s** | 191 MiB | Store, Store, Deflate |

- **Base de comparación:** en esta PC develop tarda 190,5 s, no los 534,7 s del QA. La diferencia es de máquina o de
  build (acá es Release y SSD local). La comparación justa es M-base contra M1, en la misma PC y con el mismo juego.
- **La RSS no cambia con esta HU.** M-base ya llegaba a 188 MiB, así que ese consumo es del runtime y no viene del
  cambio.
  - M1 (158 MiB = 166 MB) y M1B (185 MiB = 194 MB) cumplen el criterio de < 200 MB.
  - M2 (191 MiB = 200,4 MB), que no tiene meta, queda en el límite, igual que develop.
- **M1B** se agregó porque, con el orden del QA, la 3.ª entrada empieza en el offset 1 757 006 328 (< 4 GiB). La
  SDD pedía comprobar Zip64 con una entrada que empiece a más de 4 GiB. Con el archivo grande primero, la 3.ª entrada
  (`grabacion_ios_qa.mp4`) empieza en el offset **4 490 475 781 (> 4 GiB)**.

### Validación de los ZIP pendientes

| Corrida | Validación | Resultado |
|---|---|---|
| M1 | `7zz t -p…` | `Characteristics = Zip64`, "Everything is Ok". |
| M1 | `7zz t` con contraseña incorrecta | "ERROR: Wrong password" en las 3 entradas. |
| M1 | `python3 zipfile` (`infolist`) | 3 entradas con `compress_type 99`. `csize = size + 28` en las tres. |
| M1 | `shasum -a 256` del ZIP | Igual al `zip_hash` de la respuesta (`c69b049c…`). |
| M1B | `7zz t` | Zip64 OK, "Everything is Ok". |
| M1B | `python3 zipfile` | Lee bien la entrada que empieza en el offset > 4 GiB. |
| M1B | `bsdtar --passphrase … -x` de `grabacion_ios_qa.mp4` (la entrada > 4 GiB) | SHA-256 igual al original (`af4e7255…`). |
| M2 | `7zz t` | Zip64 OK, "Everything is Ok". |
| M2 | `python3 zipfile` | El `.bin` con Deflate tiene `csize` 3 759 242 712: los datos aleatorios crecen un poco al pasar por Deflate. |
| Todas | Verificación propia de Tatana | Pasó (respuesta 200 con `zip_hash`). Sin excepciones en los logs. |

### Incidente menor durante la medición (sin impacto fuera del temporal)

Mi script de reseteo entre corridas tenía un error de parseo y movió la carpeta del caso de prueba a otra ruta,
siempre dentro de mi directorio temporal. Lo detecté y devolví los 3 archivos a su lugar. Después verifiqué los
SHA-256 contra los originales con `shasum -c`, y los tres dieron OK. No tocó nada fuera de `<tmp>`.

### Limpieza (§11.4)

- Cada ZIP pendiente se borró por su ruta exacta al terminar su corrida.
- Al final se borraron el directorio temporal completo (juego, binarios, logs y carpetas de casos) y el worktree
  (`git worktree remove --force` + `prune`; `git worktree list` muestra solo el repo).
- También se borraron mis tres scripts de edición del scratchpad. No toqué los otros archivos que había en el
  scratchpad, porque no son míos.
- No se detuvo el backend de :8080. Las instancias de Tatana propias quedaron detenidas (`pgrep` no muestra
  ninguna).
- No se usaron ni Mongo ni `Storage:DataDirectory`.

`ls` de antes y de después, idénticos:

```
== ~/Factum/Evidencia
.DS_Store  1213123124_5319421b  1234_21fef484  1234_69343b50  1234577_fd72f592  4321_71fe0fd0  CIFSALXXX_698ec7cd
   (mismas fechas: carpeta "." Oct 12 21:37)
== server/src/Factum.Agent/agent-data/cases
(vacía; "." Oct 12 21:37)
```

## Contrato

Esta HU no cruza lados (§5). No se agregó, renombró ni quitó ningún campo JSON, DTO ni evento WebSocket.
`BuildZipResponse` y `zip_progress` salen iguales: los mismos campos, y `encryption` sigue en `"aes256-ae2"`.

## Pendiente para el usuario (§11.5)

- Generar un caso real desde la web y comprobar las fases.
- Abrir el ZIP con WinRAR / 7-Zip en Windows y con Keka / The Unarchiver en macOS, con la contraseña y sin ella.
- Comparar el SHA-256 del informe con `shasum` / `certutil`.
