# impl_backend — marca-comercial-sin-mpf-gfd

**Implementador:** implementer-backend (Claude Opus 5.5) · **Fecha:** 2026-10-01 · **Rama:** `feat/marca-comercial-sin-mpf-gfd` (sin commit, como pidió el orquestador)
**SDD:** `Refactorizaciones/marca-comercial-sin-mpf-gfd.md`, sección 11.1, con la "Resolución de decisiones pendientes" aplicada: `B-PLANTILLA` (v4) **no** se implementa; `B-PLANTILLA-INTERINA` **sí**; la atribución va en el pie de todas las páginas, **sin versión**.

## Estado

**done, salvo B9 (borrados), que queda pendiente:** el sistema de permisos rechazó el `rm` de los archivos de B9 ("Irreversible Local Destruction"). No intenté otra forma de borrarlos. Para cerrarlo hace falta que el usuario los borre o autorice el borrado (la lista exacta está abajo). Todos los demás ítems de 11.1 están hechos y verificados.

| Ítem | Estado |
|---|---|
| B1 `BrandingOptions` | hecho |
| B2 `BrandingService` (+ `BrandingSnapshot`, `BrandingLogo`) | hecho |
| B3 `ImageProbe.TryDetect` | hecho (`GetImageSize` no se tocó) |
| B4 `Program.cs` (`appsettings.Local.json`, DI, resolución al arrancar) | hecho |
| B5 `appsettings.json` → `Branding` vacía | hecho (`appsettings.Development.json` sin tocar) |
| B6 `.gitignore`, `.dockerignore`, `.csproj` | hecho, con `git check-ignore` OK |
| B7 `ConfigDtos.cs` + `ConfigController` | hecho |
| B8a–B8f `ReportService` | hecho |
| **B9 borrados** | **PENDIENTE: permiso denegado** (ver abajo) |
| B10 comentarios del server | hecho |
| B11 `.gitlab-ci.yml` | hecho |
| B12 `README.md` | hecho |
| B13 `B-PLANTILLA-INTERINA` (parche de la v3) | hecho y verificado con LibreOffice |
| B-PLANTILLA (v4) | **no se implementa** (pasa a la HU `informe-pericial-de-parte`) |
| B14 arnés de verificación | hecho (en el scratchpad, nada en el repo) |
| B15 este archivo | hecho |

## Archivos tocados

**Nuevos**
- `server/src/Factum.Backend/Services/Branding/BrandingOptions.cs`
- `server/src/Factum.Backend/Services/Branding/BrandingService.cs`: `IBrandingService`, `BrandingService`, `BrandingSnapshot` y `BrandingLogo` (records)
- `server/src/Factum.Backend/Services/Branding/ImageProbe.cs`
- `server/src/Factum.Backend/DTOs/ConfigDtos.cs`: `PublicConfigResponse`
- `server/src/Factum.Backend/Controllers/ConfigController.cs`
- `server/src/Factum.Backend/.dockerignore`

**Modificados**
- `server/src/Factum.Backend/Program.cs`: inserta la fuente `appsettings.Local.json` (con `optional` y sin `reloadOnChange`) justo después del último `appsettings*.json` y antes de las variables de entorno y la línea de comandos; agrega `Configure<BrandingOptions>`, `AddSingleton<IBrandingService, BrandingService>` y `GetRequiredService<IBrandingService>()` después de `Build()`.
- `server/src/Factum.Backend/appsettings.json`: suma la sección `Branding` vacía.
- `server/src/Factum.Backend/Factum.Backend.csproj`: `Content Remove` y `None Remove` para `appsettings.Local.json` y `branding\**`.
- `server/src/Factum.Backend/Services/Reports/ReportService.cs`: B8a–B8f.
- `server/src/Factum.Backend/Templates/plantilla_informe_v3.docx`: parche interino (B13).
- `server/src/Factum.Backend/Controllers/AgentAuditController.cs`, `Controllers/SupportController.cs`, `Services/Support/ISupportService.cs`, `Services/Support/SupportService.cs` y `Services/Updates/TatanaUpdatesService.cs`: solo comentarios (B10).
- `.gitignore`: `**/appsettings.Local.json` y `server/src/Factum.Backend/branding/`.
- `.gitlab-ci.yml`: L48 → `https://factum.example.com`.
- `README.md`: B12.

No toqué `client/`, `agent-ui/`, `ops/brand/`, `server/src/Factum.Agent/`, `MpfAuthProvider`, `GET /api/auth/mode`, la config ni el código de Faro (solo los comentarios que pide B10), `backlog.json` ni `progress/current.md`. Tampoco abrí los `docs/INFORME*`.

## B9: pendiente (permiso denegado)

Hay que borrarlos; todos están versionados en git, así que se pueden recuperar del historial:

```
server/src/Factum.Backend/Templates/mpfs.png
server/src/Factum.Backend/Templates/GFD_logo.png
server/src/Factum.Backend/Templates/header-fondo.png
server/src/Factum.Backend/Templates/footer-fondo.png
server/src/Factum.Backend/Templates/body-fondo.png
server/src/Factum.Backend/Templates/portada-fondo.png
server/src/Factum.Backend/Templates/plantilla-inspeccion-tecnica.docx
server/src/Factum.Backend/Templates/plantilla-inspeccion-tecnica-v2.docx
server/src/Factum.Backend/Templates/plantilla-inspeccion-tecnica.pdf
server/src/Factum.Backend/Templates/plantilla-inspeccion-tecnica.docx.orig-backup
server/src/Factum.Backend/Services/Reports/Factum-GFD.code-workspace
server/tools/ReportTemplateBuilder/          (carpeta entera; después server/tools/ queda vacía y también se borra)
```

Ningún `.cs` referencia esos archivos. Como el `.csproj` copia `Templates\**\*` con un glob, borrarlos no requiere tocar el `.csproj`. Mientras sigan en el repo, el grep de aceptación da una sola coincidencia fuera de T13: `Services/Reports/Factum-GFD.code-workspace:4` (`Factum-GFD`). Los PNG/PDF/DOCX son binarios y `grep -I` no los lista.

## Decisiones no obvias

- **Contrato (sección 7):** coincide exactamente. `PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl)` se serializa con `SnakeCaseLower` → `organization_name` y `organization_logo_url`. Los `null` viajan como `null` y la URL es relativa: `/api/config/branding/logo?v=<12 hex>`. `ContactLines` no se expone.
- **`GET /api/config/branding/logo` también acepta `HEAD`** (`[HttpHead]`): sin eso, el `curl -I` de la sección 12 devolvía 405. El `304` se responde cuando `If-None-Match` coincide con el ETag (también acepta `W/"..."` y `*`).
- **`BrandingService` recibe `IHostEnvironment`** (no `IWebHostEnvironment`): `ContentRootPath` es la misma propiedad y así el servicio se puede instanciar fuera de ASP.NET (lo usa el arnés).
- **Los warnings no loguean datos del cliente**, solo motivos y límites (p. ej. "supera 150 caracteres", "no es un PNG ni un JPEG válido"). El log de info del logo dice tipo, tamaño y versión.
- **`{CONTACTO}`:** el párrafo se parte por el placeholder y los demás placeholders se reemplazan pieza por pieza. Así, un `\n` dentro de otro valor (por ejemplo `{OBSERVACIONES}`) no se convierte en `<w:br/>`. Las líneas quedan en el primer run, con su `rPr`.
- **`{LOGO_ORGANIZACION}`:** usa el `ParagraphProperties` del párrafo original, clonado; si no tenía, va un `pPr` vacío. Clonarlo también preserva un `sectPr` si el placeholder estaba en un párrafo de corte de sección. La caja se acepta si mide entre 0 y 100 cm por lado; si no, se usa la caja por defecto de 5 × 1.5 cm.
- **Pie de atribución (8.2), herencia entre secciones:** si a una sección le falta el footer de un tipo, se le pone una `FooterReference` explícita al footer **heredado** de la sección anterior (como haría Word). Solo si ninguna sección anterior tiene footer de ese tipo se apunta al `FooterPart` nuevo. Esto evita que una sección que heredaba el pie de la plantilla quede con un pie que solo tiene la atribución. El Sello va en un run con `w:position=-3` (1.5 pt hacia abajo) para centrarlo con el texto de 8 pt.
- **`drawId`** pasó a ser un contador único en `GenerateDocxAsync`: arranca en 100 000, como antes, y lo comparten el logo, las fotos, las capturas y el Sello. Lo verifiqué: no hay `docPr id` duplicados.
- **Plantilla interina:** en la banda, `{ORGANIZACION}` queda sin los espacios de relleno que tenía el texto viejo, así que el nombre arranca alineado a la izquierda de la caja. Los runs de las 3 imágenes se borraron enteros. Cada uno tenía un único `w:drawing` anclado, sin texto (lo verifica el script).
- **`Templates/factum-sello.png` todavía no existe:** lo genera `implementer-frontend` con `ops/brand/` (F2). Mientras no esté, el informe sale con la atribución solo en texto, más un warning. Lo probé (corrida `a-nosello`). Las demás corridas del arnés usaron un Sello de reemplazo dibujado en el scratchpad, solo dentro del `bin/` del arnés.

## Verificación

### Builds

```
$ dotnet build server/src/Factum.Backend/Factum.Backend.csproj
  ... warning NU1902: SharpCompress 0.30.1 ...   (preexistente)
  ... warning NU1903: Snappier 1.0.0 ...         (preexistente)
    4 Advertencia(s)      <- las mismas 4 NU190x de antes del cambio (baseline medido al empezar)
    0 Errores

$ dotnet build server/src/Factum.Agent/Factum.Agent.csproj
    0 Advertencia(s)
    0 Errores
```

No aparecen warnings `CS` nuevos.

### Ignorados (B6)

```
$ git check-ignore -v server/src/Factum.Backend/appsettings.Local.json server/src/Factum.Backend/branding/logo.png
.gitignore:38:**/appsettings.Local.json	server/src/Factum.Backend/appsettings.Local.json
.gitignore:39:server/src/Factum.Backend/branding/	server/src/Factum.Backend/branding/logo.png
```

### Endpoints

El backend compilado corrió con `--contentRoot` apuntando a carpetas del scratchpad y en puertos 1808x. No se creó ningún `appsettings.Local.json` dentro del repo. Estos endpoints no tocan Mongo.

```
# sin Branding
GET /api/config/public          -> {"organization_name":null,"organization_logo_url":null}
GET /api/config/branding/logo   -> 404 {"error":"Sin logo configurado"}

# con appsettings.Local.json de placeholders (nombre + branding/logo.png 800x200)
GET /api/config/public          -> {"organization_name":"Dr. Nombre Apellido · Dra. Nombre Apellido","organization_logo_url":"/api/config/branding/logo?v=07e6cad725ba"}
HEAD /api/config/branding/logo?v=07e6cad725ba
  HTTP/1.1 200 OK
  Content-Length: 1416
  Content-Type: image/png
  Cache-Control: public, max-age=3600
  ETag: "07e6cad725ba"
  X-Content-Type-Options: nosniff
GET con If-None-Match: "07e6cad725ba" -> 304 ; con otro ETag -> 200
sha256 de la respuesta == sha256 del archivo en disco

# precedencia: Branding__OrganizationName en el entorno + el mismo appsettings.Local.json
GET /api/config/public          -> {"organization_name":"Gana la variable de entorno", ...}   (env > Local, OK)

# logo inválido (texto con extensión .png) + nombre en blanco
log de arranque: "Branding: logo ignorado (no es un PNG ni un JPEG válido)"
GET /api/config/public          -> {"organization_name":null,"organization_logo_url":null}
GET /api/config/branding/logo   -> 404

GET /api/auth/mode              -> {"mode":"dev"}   (sin cambios)
```

### B13: parche de la plantilla v3

El script descartable (`patch_v3.py`, Python `zipfile`) quedó en el scratchpad y no se versiona. Hace esto:

1. Borra los runs de "Imagen 9" (rId6), "Imagen 3" (rId7) y "Gráfico 5" (rId8), con assertions: un solo `w:drawing`, sin `w:t` y con el `r:embed` esperado.
2. Borra las `Relationship` rId6, rId7 y rId8, más `word/media/image1-3.png` (y la entrada de carpeta `word/media/`, que queda vacía).
3. Reemplaza el texto de la banda por `{ORGANIZACION}` en **las 2 copias** (Choice y Fallback), dentro del mismo run, conservando Arial y el gris D9D9D9.

El ZIP se reempaquetó con `[Content_Types].xml` primero.

- Después del parche, el grep de `evidentia|gabinete|ministerio|mpf|gfd|CIF` sobre todo el contenido descomprimido no da resultados, y `{ORGANIZACION}` aparece 2 veces. `document.xml` y sus `.rels` parsean bien.
- **Render con LibreOffice** (`soffice --headless --convert-to pdf` + `pdftoppm`): comparé la página 1 de la v3 original contra la parcheada y son **idénticas salvo la banda**. La original muestra los 3 logos más "Evidentia – Web | Gabinete Forense Digital – CIF | Ministerio Publico Fiscal"; la parcheada solo `{ORGANIZACION}` (en el informe generado, el nombre configurado o nada).
- SHA-256: v3 original `b3fc91ed…ba7d` (52 460 bytes); v3 parcheada `5c9ecfc2…22ad` (20 632 bytes).

### B14: arnés del informe (scratchpad, nada en el repo, sin Mongo ni Storage)

Es un proyecto de consola en el scratchpad que referencia `Factum.Backend.csproj`. Arma un `Case` en memoria, con datos ficticios (`REF-PRUEBA-001`, "Persona Ejemplo", "Operador Ejemplo"), y llama a `ReportService.GenerateAsync` con un `caseDir` dentro del scratchpad. Usa 3 PNG de prueba generados (foto del funcionario, foto del denunciante y una captura).

| Corrida | Branding | Resultado |
|---|---|---|
| a-nosello | vacío; sin `factum-sello.png` | warning "falta …factum-sello.png; la atribución va solo con texto"; se crea el footer con "Realizado con Factum" (solo texto); banda vacía |
| a | vacío; Sello de reemplazo | footer nuevo con Sello + " Realizado con Factum"; banda vacía |
| b | nombre "  Estudio Jurídico Ejemplo  " (con trim), 3 líneas de contacto (una en blanco, se descarta → 2), logo PNG 800×200 | banda "Estudio Jurídico Ejemplo" en Choice y Fallback; pie en las 2 páginas |
| c | igual que b, con un logo inválido (texto con extensión `.png`) | warning "Branding: logo ignorado (no es un PNG ni un JPEG válido)"; sale igual, sin logo |
| d-b | b, con una **copia** de la plantilla en el `bin/` del arnés: `{LOGO_ORGANIZACION}` en un párrafo `jc=right`; `{LOGO_ORGAN`+`IZACION:4x1,2}` partido en 2 runs y con `jc=center`; `Contacto: {CONTACTO} (fin) {NROREF}` en negrita roja; `{CONTACTO_EN_LINEA}`; un footer Default propio con `{LOGO_ORGANIZACION:2x0.6}` y `{ORGANIZACION} — {CONTACTO}`; `titlePg` | logos ajustados sin recortar: 5×1.25 cm (caja por defecto) y 4×1.0 cm (caja 4×1,2), con la alineación original; `{CONTACTO}` como `Calle…<w:br/>Cel.…` en el mismo run, con el formato del run y `{NROREF}` resuelto; en línea "Calle Ejemplo 123, Ciudad · Cel. …"; el footer de la plantilla queda con su logo (ImagePart propio del footer), sus textos **y** la atribución agregada al final; se crea `footerReference type=first`, que reutiliza el mismo footer; 3 páginas, con el pie en todas |
| d-a | la misma copia de la plantilla, sin Branding | los párrafos de logo quedan vacíos, `{CONTACTO}` y `{CONTACTO_EN_LINEA}` quedan en "" y el footer queda "Pie de plantilla:  — " + atribución |

- **Validación OpenXML** (`OpenXmlValidator`, Office2019): las 6 salidas, la v3 original y la v3 parcheada dan exactamente los mismos **29 errores, todos en `word/styles.xml`** (`uiPriority`, que la plantilla ya traía). El código no agrega ningún error nuevo.
- **`docPr id` duplicados:** 0 en todas las corridas.
- **Render:** revisé a ojo las páginas 1-2 de b y las páginas 1-3 de d-b. El Sello y "Realizado con Factum" salen centrados en el pie de **todas** las páginas, la banda muestra el nombre y no hay desarmado del layout.
- Lo que escribió el arnés quedó solo en el scratchpad (`…/scratchpad/be/`).

### Grep de aceptación (mis carpetas)

```
$ grep -rnIiE "\bmpf|\bgfd\b|ministerio p[uú]blico|gabinete forense|mpfsalta|evidentia" server/src README.md .gitlab-ci.yml --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules --exclude-dir=agent-data
```

Solo quedan las excepciones de T13:

- `Program.cs`: `Configure<MpfOptions>`, el comentario del auth provider, `authMode == "mpf"` y `MpfAuthProvider`;
- `appsettings.json`: `Mpf*`;
- `MpfAuthProvider.cs` (el archivo entero);
- `README.md`: el modo `mpf`, `Auth:Mpf*` y el árbol `(Dev/Mpf)`.

Fuera de T13 queda **una sola coincidencia**, `Services/Reports/Factum-GFD.code-workspace`, que desaparece con B9. `\bGIF\b` no da resultados. (Excluí `agent-data/` del agente porque son capturas de desarrollo, no código.)

## Pendientes para el usuario

1. **B9:** borrar los archivos listados arriba (el permiso me lo denegó).
2. **Sello real:** cuando `implementer-frontend` genere `server/src/Factum.Backend/Templates/factum-sello.png`, el pie pasa a llevar la marca automáticamente, sin cambios de código.
3. **Prueba manual** (punto 3 de la sección 12 de la SDD): con datos reales del estudio en `appsettings.Local.json` (ignorado), generar el informe de un caso **de prueba nuevo** y revisarlo en Word/WPS: la banda tiene que mostrar el nombre del estudio, sin MPF, GFD ni Evidentia, y el Sello con "Realizado con Factum" tiene que salir en el pie de cada página.

## B9: cerrado por el orquestador (2026-10-01)

Con autorización explícita del usuario, el orquestador ejecutó `git rm -r` de la lista de arriba. `server/tools/` quedó vacía y se eliminó. En `Templates/` quedan `plantilla_informe_v3.docx` y `sin-foto-placeholder.png`. `dotnet build` del backend da 0 errores.
