# Fixtures RFC 3161 (sellado-tiempo §11.1)

Respuestas reales de dos autoridades de sellado de tiempo sobre un dato de prueba fijo
(`SHA-256("factum-fixture-sellado-tiempo")`). No contienen datos de ningún caso real. Las usan los tests de
`Timestamping/` para validar respuestas sin salir a la red (CI nunca le pega a una TSA).

| Archivo | Qué es |
|---|---|
| `freetsa.tsr` | `TimeStampResp` DER de `https://freetsa.org/tsr` (`certReq = true`) |
| `digicert.tsr` | `TimeStampResp` DER de `http://timestamp.digicert.com` (`certReq = true`) |
| `fixtures.json` | dato, hash, nonce usado en cada pedido, y número de serie, fecha y política esperados |

## Regenerarlas

Solo hace falta si cambia el formato esperado (los tests no se rompen cuando vence el certificado de la TSA:
`VerifySignatureForHash` no valida vigencia ni cadena). Con red, desde la raíz del repo:

```bash
FACTUM_TSA_FIXTURES_DIR=/tmp/tsa-fixtures \
  dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj --filter "FullyQualifiedName~TsaFixtures"
cp /tmp/tsa-fixtures/{freetsa.tsr,digicert.tsr,fixtures.json} server/tests/Factum.Backend.Tests/Fixtures/Tsa/
```

Sin la variable, el test `TsaFixtures.Generar_SiHayCarpeta` no hace nada.

Verificación independiente (opcional):

```bash
openssl ts -reply -in freetsa.tsr -text
openssl ts -verify -digest <sha256 de fixtures.json> -in freetsa.tsr -CAfile ca-freetsa.pem   # raíz de https://freetsa.org/files/cacert.pem
```
