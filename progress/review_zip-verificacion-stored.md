# Review — zip-verificacion-stored (HU #58)

**Veredicto:** APROBADA

Verificado por mí: `dotnet build` de Agent (0 errores) y Backend (0 errores, 4 warnings NU1902/NU1903 preexistentes);
`dotnet test` Agent 337/337, Backend 888 OK + 7 omitidos (MongoAdminIntegrationTests, sin env var, preexistente).
No hay cambios de client/agent-ui: el checkpoint de skills UX no aplica.

## Checkpoints
- Archivos tocados = checklist de la SDD (2 EvidenceZip.cs, CaseZipService.cs, ReportService.cs, 3 archivos de tests): [x]
- Lectura de verificación sin camino async defectuoso en ambas copias (`Sha256OfEntry` con `Read(byte[],0,n)` + `IncrementalHash`; ya no hay `HashDataAsync` en `src`): [x]
- Copias coherentes (diff Agent vs Backend = solo cabecera, namespace, `onProgress` en WriteAsync; tests: solo namespace/cabecera/_root + T3b de Tatana): [x]
- Buffer único de 80 KiB por llamada, sin cargar la entrada en memoria; cancelación por bloque; `useAsync: false`: [x]
- Barrido de tamaños Stored y Deflate, con/sin contraseña (`Sweep_RoundTrip_AllSizes` 4 combinaciones, `SweepSizes` completa), secuencia QA, corrupción Stored+AES, 7zz t/x, canario: [x]
- D1: ZIP bien escrito (Rama A) documentado con 7zz t/x y zipfile; `WriteAsync` sin cambios en el diff: [x]
- D4: `ZipFailedMessage` con el texto exacto de la SDD, genérico para otras causas, log estructurado sin contraseña; tests B7: [x]
- D2: texto del issue upstream sin datos de casos (repro con datos aleatorios, contraseña "x123"): [x]
- Formato, cifrado (AES-256 AE-2), regla Stored/Deflate y controles de verificación sin cambios; firma pública igual: [x]
- Mongo / Storage / modo Mock: no tocados: [x]
- Seguridad: ni mensaje ni `Entry` llevan la contraseña (T5 lo comprueba): [x]

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- **D5 (decisión del usuario):** la medición es creíble: 3 corridas alternadas por variante en procesos separados, medianas 27,78 s vs 47,88 s (1,72x), dispersión baja (±1 s), RSS 47-59 MB, y causa explicada con evidencia (ZipAESStream.Read transforma de a 16 B; el camino async roto es más rápido justamente por saltear eso). El fallback `useAsync: true` quedó empatado en macOS. Medido solo en macOS; Windows sin medir. No pude re-medir 5,5 GB, pero el resultado es consistente con el mecanismo. Queda para que el usuario acepte el costo o pida alternativa (verificación paralela por entrada).
- El hallazgo extra es positivo: con `Read` síncrono SharpZipLib ahora valida el HMAC de AE-2; el `<summary>` de VerifyAsync se actualizó en ambas copias y T4 acepta ambos mensajes.
- El issue upstream lleva un agregado "Side note (performance)" no pedido por la SDD; el usuario decide si lo deja.
- Quedan placeholders `<URL cuando el usuario lo publique>` en las dos EvidenceZip.cs y en `UpstreamIssue` de los tests, a completar cuando se publique.
- Barrido amplio reportado (40 680 corridas, 0 fallos) no lo re-corrí; los tests de la suite cubren el barrido liviano y pasan.
