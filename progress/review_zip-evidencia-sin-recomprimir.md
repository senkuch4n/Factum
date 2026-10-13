# Review — zip-evidencia-sin-recomprimir (#51)

**Veredicto:** APROBADA

## Checkpoints
- C1 (Fase en_revision / verify.sh): [ ] no verificado por mí (cambios sin commitear, fuera del alcance pedido; lo cubre el orquestador).
- C2: [x] HU, SDD y progress existen. Sin contrato compartido (no cruza lados, ningún campo JSON cambia).
- C3: [x] Solo server/src/Factum.Agent y Factum.Backend (EvidenceZip.cs x2) + sus tests. Sin Mongo, sin logs nuevos.
- C4: [x] Corrí yo: build Agent 0 warnings/0 errores; build Backend 0 errores (warnings NuGet preexistentes); tests Agent 301/301, Backend 880 OK + 7 omitidos (Mongo, por diseño).
- C5: [x] impl_backend existe y es detallado; no hay scripts temporales en el árbol. Skills UX: no aplica.

## Verificaciones específicas
- Copias coherentes: `diff` entre las dos EvidenceZip.cs muestra solo cabecera "COPIA de", namespace y callback onProgress (igual que antes). StoredExtensions (26) y MethodFor idénticos.
- Formato/cifrado: el único cambio funcional es `CompressionMethod = MethodFor(path)`; AES-256 AE-2, Zip64 Dynamic, Size, UTF-8, CreateNew, VerifyAsync (solo comentario), rehash y SHA-256 del ZIP intactos. Stored con AES validado con 7zz, bsdtar y python (informe), incl. entrada con offset > 4 GiB (M1B).
- Cadena de custodia: Verify sigue comparando SHA-256 por entrada; test T5 cubre entrada Stored corrupta.
- Meta D3: M1 54,3 s (meta 270 s), M-base 190,5 s en la misma PC (3,5x), con tablas por fase, RSS y validaciones; la diferencia con los 534 s del QA está explicada (máquina/Release/SSD) y la comparación justa es en la misma PC. Corrida .bin (M2, 144,9 s) informada aparte como pide DA2. Números creíbles y coherentes (1 GiB spike: 49 s vs 8 s).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- Las medidas se hicieron con datos aleatorios generados (no los videos reales); el usuario debe confirmar con un caso real, ya anotado en el reporte.
- Entradas vacías salen Stored (comportamiento previo de SharpZipLib), documentado en T2.
- `agent-ui/tsconfig.*.tsbuildinfo` aparecen modificados/sin trackear: no incluirlos en el commit.
- Backend build mostró 4 warnings NuGet en mi corrida (reporte dice 2); son avisos de paquetes, no del código tocado.
