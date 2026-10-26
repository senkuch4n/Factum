# Review — extraccion-logica-android (#100) — Reintento 2 (H1 ACTA veraz)

**Veredicto:** APROBADO

Verificado: `dotnet build` Agent 0 warnings/0 errores; `dotnet test` Agent 1068/1068. No se ejecutó nada contra dispositivo.

## Bloqueante anterior: RESUELTO
- `ActaVerification` reemplaza el flag de capacidad. `ExtractionActaWriter.VerificationLines` (líneas ~298-330) deriva §6
  de lo ocurrido: (a) `FilesComputed>0` y 0 discordancias → "coincide con el de la PC en los N archivos"; (b)
  discordancias → las declara, remite a `coincide=no` del manifiesto y aclara que el hash se toma antes de copiar; (c)
  `FilesComputed==0` → "No se calculó…" con motivo correcto (desactivado por el operador / sin sha256sum / la lectura
  no produjo hashes) y cuántos quedaron `no_calculado`. El mismo objeto alimenta el json (`verification{requested,
  device_sha256_available, binary, files_computed_on_device, mismatches, files_not_computed}`).
- El binario sale de `DetectDeviceSha256` (comando real en el equipo, `ExtractionService.cs:413`), y se omite si es
  null. `FilesComputed` cuenta archivos efectivamente comparados (`DeviceHashedCount`), no la capacidad.
- Recuperación (`ExtractionRecovery.cs:93-101`): `FilesComputed: 0`, sin binario → "No se calculó", nunca afirma lo
  que no se completó.
- Tests de los tres casos y de la recuperación presentes y verdes.

## Observaciones no bloqueantes
- En recuperación, con verificación pedida y capacidad presente, el motivo cae en "la lectura no produjo hashes";
  es veraz pero no nombra la interrupción (la sección 8 del ACTA ya la declara).
- Archivos ajenos sin trackear en el working tree (docs/mp-informatica, etc.): no incluirlos en el commit.
