# impl_backend — formulario-caso-catalogos

**Estado:** done (sin commit, como pidió el orquestador)
**Rama:** `feat/formulario-caso-catalogos`
**SDD:** `Refactorizaciones/formulario-caso-catalogos.md` (D1–D15 recomendadas, DP1 A: conector "e" delante de /i/)
**Proyecto:** `server/src/Factum.Backend` + `server/tests/Factum.Backend.Tests`. No se tocó `client/`, `agent-ui/`, `Factum.Agent`, `backlog.json` ni `progress/current.md`.

## Checklist 10.1

| # | Estado | Archivo(s) |
|---|---|---|
| B1 | ok | `Models/Case.cs`: `List<string>? Integrantes` (anulable, sin default) |
| B2 | ok | `Models/CatalogEntry.cs` (nuevo): `CatalogEntry`, `CatalogSeed`, ambos `[BsonIgnoreExtraElements]` |
| B3 | ok | `Services/Catalogs/CatalogText.cs` (nuevo): `CleanValue`, `NormalizeKey`, `MaxValue = 300` |
| B4 | ok | `Services/Catalogs/CatalogDefinitions.cs` (nuevo), igual que la SDD §3.4 |
| B5 | ok | `Services/Cases/IntegrantesFormatter.cs` (nuevo): `Clean`, `Join` |
| B6 | ok | `DTOs/CaseDtos.cs`: `List<string>? Integrantes = null` al final de `CreateCaseRequest` y `UpdateCaseRequest` |
| B7 | ok | `Services/Cases/CaseValidation.cs` |
| B8 | ok | `Infrastructure/MongoRepository.cs`: `CaseDataUpdate.Integrantes`, `$set`/`$unset` |
| B9 | ok | `Infrastructure/MongoRepository.cs`: `ICaseRepository.ListCatalogSourcesAsync` (solo lectura, con proyección) |
| B10 | ok | `DTOs/CatalogDtos.cs` (nuevo) |
| B11 | ok | `Infrastructure/CatalogRepository.cs` (nuevo) |
| B12 | ok | `Services/Catalogs/CatalogService.cs` (nuevo): `CatalogService` + `CatalogLogic` estático (lógica pura testeable) |
| B13 | ok | `Controllers/CatalogsController.cs` (nuevo) |
| B14 | ok | `Services/Cases/CaseService.cs` |
| B15 | ok | `Program.cs`: `AddSingleton<ICatalogRepository, CatalogRepository>()`, `AddScoped<ICatalogService, CatalogService>()` |
| B16 | ok | `Services/Reports/ReportValues.cs`: solo un comentario, sin cambio funcional |
| B17 | ok | 5 archivos de tests nuevos (abajo) |
| B18 | ok | build y tests en verde (abajo) |
| B19 | ok | prueba manual contra `factum_dev` (abajo) |
| B20 | ok | este archivo |

## Contrato compartido: comparación con la SDD §4

- `Case.integrantes`: `string[] | null`. En `GET`/`POST`/`PUT` sale `null` en casos viejos, o cuando un PUT sin lista hizo `$unset`. Verificado en la prueba manual.
- Request `integrantes` (opcional) en `POST /api/cases` y `PUT /api/cases/{id}`. Si viene, `integrantes_tribunal` del request se ignora y el servidor lo deriva con `IntegrantesFormatter.Join`.
- Errores: `400 { error: "Se pueden cargar hasta 20 integrantes" }` y `400 { error: "El campo integrantes supera los 300 caracteres" }`. El derivado no pasa por el límite de 500. Sin lista, `integrantes_tribunal` sigue con el máximo de 500.
- `GET /api/catalogs` → `{ "catalogs": { "destinatarios": [], "partes": [], "profesiones": [], "tipos_dispositivo": [] } }`. Las cuatro claves están siempre, porque son las claves del diccionario y no las transforma la naming policy. Cada lista va ordenada por `last_used_at` desc, `use_count` desc y `value` (OrdinalIgnoreCase), con un tope de 500.
- `CatalogEntry` JSON: `id`, `value`, `use_count`, `last_used_at` (ISO UTC). Verificado en la respuesta real.
- `PUT /api/catalogs/{catalog}/entries/{id}` con `{ "value" }` → `200 CatalogEntry`. Los errores son los de la SDD, con el texto exacto y verificados uno por uno:
  - `404 "Catálogo desconocido"`.
  - `404 "No se encontró el valor"` (también si la entrada es de otro perito).
  - `400 "Ingresá un valor"`.
  - `400 "El valor supera los 300 caracteres"`.
  - `409 "Ya tenés «<valor existente>» en tus sugerencias"`.
- `DELETE /api/catalogs/{catalog}/entries/{id}` → `204` sin body. Los 404 son los mismos que en el PUT.
- `401` sin token.

## Decisiones no obvias

1. **`UpdateValueAsync` usa `UpdateOne` + `Find` y no `FindOneAndUpdate`.** `findAndModify` informa el E11000 como `MongoCommandException`, no como `MongoWriteException`. Con `UpdateOne`, el choque con el índice único sale como `MongoWriteException` con `Category = DuplicateKey`, que es lo que la SDD dice que atrapa el servicio. Verificado: da 409.
2. **Agregué `ICatalogRepository.FindByKeyAsync`**, que no está en la lista de §5.2. Sirve para armar el mensaje del 409 con la grafía de la entrada existente ("busca la entrada que choca para el mensaje", §5.3).
3. **`Count` de la siembra = cantidad de casos que usan el valor.** Dentro de un caso, los valores se deduplican por `(catalog, key)`, igual que en el alta automática (§5.4). Si la misma persona es denunciante y proponente en un caso, cuenta 1. La SDD dice "ocurrencias"; elegí el criterio que coincide con el de §5.4.
4. **"Teléfono celular" en la siembra:** solo se agrega como uso propio (`Count 0`, `UsedAt = UnixEpoch`) si su clave no apareció ya en los casos. Si apareció, queda la grafía y el conteo del caso. Es equivalente a lo que pide la SDD ("el upsert no duplica").
5. **`ValuesForUpdate` compara campo por campo y por posición** dentro de cada catálogo, con `NormalizeKey`. Por ejemplo, para `partes` se comparan denunciante con denunciante y denunciada con denunciada. Solo se registran los campos cuya clave cambió (D12). Un cambio de solo mayúsculas o espacios no se registra.
6. **`UsagesFor`** descarta vacíos, catálogos desconocidos y valores de más de 300 caracteres (defensivo: `CaseValidation` ya limita a 300), y conserva la primera grafía.
7. **Siembra y cancelación:** si el request se cancela, la `OperationCanceledException` se propaga. Cualquier otra excepción de la siembra se loguea como warning y el GET sigue con el listado (§5.3 paso 5). `RecordUsageAsync` atrapa todo, sin excepción.
8. **Índices:** `CreateManyAsync` en el constructor con `CancellationToken.None` y reintento si falló, como `CaseRepository`. `UpsertUsageAsync` y `UpdateValueAsync` hacen `await` del índice antes de escribir, porque el índice único es lo que hace segura la deduplicación concurrente.
9. **`IntegrantesFormatter.Join`, regla "e"/"y":** se calcula sobre `CatalogText.NormalizeKey(último)` (sin tildes y en minúsculas). Da `" e "` si empieza con `i`, o con `hi` seguido de algo que no sea vocal `a/e/i/o/u` (o de nada). En cualquier otro caso da `" y "`. Agregué un vector extra: `"Íñigo Ruiz"` → `" e "`. El frontend tiene que replicar la regla en `joinIntegrantes` (`client/src/lib/pericial.ts`).
10. **`CaseFields`:** sumé el parámetro posicional opcional `IReadOnlyList<string>? Integrantes = null` al final del record. `ValidateForGenerate` pasa `Integrantes: null` explícito. La validación de la lista (20 ítems, 300 por ítem) corre antes que el resto de los chequeos de largo.

## Tests (B17)

Nuevos en `server/tests/Factum.Backend.Tests/`, todos puros (sin Mongo ni Storage):

- `CatalogTextTests.cs`: los vectores de §3.5, "teléfono celular" sin duplicar y las tres variantes de "el Sr. Juan Pérez".
- `IntegrantesFormatterTests.cs`: los vectores de §4.2 ("e" con Ignacio, Hilda e Íñigo; "y" con Hielo) y `Clean` (sin vacíos, en orden, con null).
- `CaseFieldsIntegrantesTests.cs`: casos (a) a (d) de §10.3, más lista vacía, 20 ítems exactos, crear y el límite de 500 sin lista.
- `ReportValuesIntegrantesTests.cs`: caso viejo, lista de 3 y sin integrantes. `{fraseIntegracion}` da lo esperado.
- `CatalogLogicTests.cs`: definiciones, dedupe de usos, `ValuesForUpdate` (solo cambios), siembra (primera grafía del caso más viejo, conteo, último uso, default "Teléfono celular", sin duplicar si ya está), y `BuildResponse` (4 claves, orden, catálogos desconocidos ignorados, tope de 500).

## Verificación (B18)

```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj
    0 Errores
    4 Advertencia(s)   ← solo NU1902 (SharpCompress) y NU1903 (Snappier), que ya estaban antes del cambio
                          (las comprobé con un build --no-incremental antes de tocar nada). Ninguna CS nueva.

$ dotnet test server/tests/Factum.Backend.Tests/Factum.Backend.Tests.csproj
Correctas! - Con error: 0, Superado: 48, Omitido: 0, Total: 48
```

Las dos advertencias CS8714/CS8619 del build de tests son de `EvidenceZipTests.cs`, que ya existía y no toqué.

## Prueba manual (B19), contra `factum_dev` en el Mongo local (contenedor `evidentia-v2-mongo-1`)

Levanté el backend con `PORT=8091 ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --no-launch-profile` y lo maté al terminar. Hay otro proceso `Factum.Backend` (PID 52165, iniciado a las 20:30, antes de esta tarea) que es del usuario y lo dejé corriendo. **Para ver los endpoints nuevos hay que reiniciarlo.**

1. **Antes:** `factum_dev` tenía `agent_events`, `cases` y `expert_profiles`, con un solo caso (`e661dce0-…`, perito `45115309`, sin campo `Integrantes`). Tomé el hash de `EJSON.stringify(db.cases.find().toArray())`: `005bd4816fb0e68531b68f6d4ad2175b8fdf6393`.
2. `GET /api/catalogs` sin token → 401.
3. `GET /api/catalogs` como `45115309` sembró los valores de su caso:
   - destinatarios: "NOmbre del tribunal".
   - partes: las 3.
   - profesiones: "Profesion de quien propone".
   - tipos_dispositivo: "Tipo de dispositivo" (use_count 1) y "Teléfono celular" (use_count 0, last_used_at 1970).

   Un segundo GET no volvió a sembrar: los conteos quedaron igual.
4. Se crearon `catalog_entries`, con los índices `_id_`, `owner_catalog_key_unique` (`{OwnerDni:1, Catalog:1, NormalizedKey:1}`, unique) y `owner_lastused` (`{OwnerDni:1, LastUsedAt:-1}`), y `catalog_seeds`.
5. Las escrituras las hice con un DNI de prueba (`99999931`):
   - PUT y DELETE con todos los códigos de error de §4.4 (lista arriba). Una entrada del usuario usada desde el DNI de prueba da 404 en PUT y en DELETE.
   - Un PUT con la misma clave ("  TELEFONO   celular ") → 200. Un DELETE → 204, y el GET siguiente **no** volvió a sembrar.
   - Creé un perfil y un caso de prueba con `integrantes: ["Dr. Juan Pérez"," ","Dra. María Gómez","Ignacio Díaz"]` e `integrantes_tribunal: "IGNORAR"`. Respuesta: lista de 3 y `"Dr. Juan Pérez, Dra. María Gómez e Ignacio Díaz"`.
   - Alta automática: "Juan Pérez" y "  juan  perez " (denunciante y denunciada) quedaron como **una sola** entrada, y "  teléfono celular " también quedó como una.
   - PUT con 21 integrantes → 400 "Se pueden cargar hasta 20 integrantes".
   - PUT de cliente viejo (sin `integrantes`, cambiando solo la profesión) → `integrantes: null`. En Mongo, `Integrantes` quedó con `$unset`. Al catálogo solo se sumó "Contadora" (D12).
   - PUT de "Contadora" → "  ABOGADA " → 409 "Ya tenés «Abogada» en tus sugerencias".
   - PUT con `["Juan Pérez","Hilda Gómez"]` → "Juan Pérez e Hilda Gómez".
6. **Limpieza solo por `_id`:**
   - las 6 `catalog_entries` del DNI de prueba (por la lista explícita de `_id`);
   - `catalog_seeds` `_id: "99999931"`;
   - `expert_profiles` `_id: "99999931"`;
   - el caso de prueba `7d248bbc-c6f1-4851-baad-7f3fc31961a7`;
   - su carpeta vacía `dev-data/cases/7d248bbc-…`.

   No quedó nada del DNI de prueba.
7. **Después:** `cases` tiene 1 documento y el hash dio `005bd4816fb0e68531b68f6d4ad2175b8fdf6393`, **idéntico** al de antes. El caso del usuario no cambió. Nunca se ejecutó `PUT /api/cases` sobre su caso.

**Lo que queda en la base por esta prueba (a propósito, es lo que pide B19):** la siembra de `45115309`, que son 7 `catalog_entries` y 1 `catalog_seeds`. Es lo mismo que haría su primer `GET /api/catalogs`. Si el usuario quiere probar la siembra desde cero, puede borrar `catalog_seeds` `_id: "45115309"` y las `catalog_entries` con `OwnerDni: "45115309"`. Ninguna de las dos es un documento de negocio preexistente.

## Pendiente para el usuario

- Reiniciar su backend (PID 52165 en :8080) para que tome los endpoints nuevos.
- Prueba manual de punta a punta con el client (SDD §11) cuando esté el frontend.
