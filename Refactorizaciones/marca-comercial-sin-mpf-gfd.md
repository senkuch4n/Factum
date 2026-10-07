# SDD: Factum como producto comercial, sin marca MPF/GFD, con el logo "Sello" y la identidad del cliente en el informe

**HU:** `docs/hu-marca-comercial-sin-mpf-gfd.md` (validada el 2026-10-01; manda la sección "Validación del usuario" más la "Actualización del usuario", y dos ampliaciones posteriores que pasó el orquestador: D4 → C con logo, atribución obligatoria "Realizado con Factum" y `Branding:ContactLines`)
**Slug:** `marca-comercial-sin-mpf-gfd`
**Implementan:** `implementer-backend` (`server/`, README, `.gitignore`, `.gitlab-ci.yml`) e `implementer-frontend` (`client/`, `agent-ui/` y el script de marca `ops/brand/`). El orquestador edita `AGENTS.md`.
**Estado de la SDD:** completa. Hay **5 decisiones pendientes del usuario** (sección 10). Solo **DP1** bloquea un ítem (la plantilla nueva, `B-PLANTILLA`), y **DP2** define si la HU se puede cerrar antes de que llegue el informe de referencia. El resto del checklist no espera a nadie.

> **Regla de datos del cliente (dura, para todos):** los datos reales del primer cliente (nombres de los profesionales, domicilio, teléfonos, logo) **no van en ningún archivo versionado**: ni en esta SDD, ni en `appsettings*.json` versionados, ni en ejemplos, ni en tests, ni en los `progress/*.md`. En ejemplos se usan placeholders: "Dr. Nombre Apellido", "Calle Ejemplo 123, Ciudad", "+54 9 000 000-0000". La sección 4.4 dice dónde vive esa config.

---

## 1. Resumen funcional

Factum deja de mostrar la marca del MPF de Salta, el GFD, el GIF y el viejo "Evidentia" en la web, en el agente Tatana, en la documentación y en los informes nuevos, y adopta el logo "Sello" (marca verde con una "F" maciza con la esquina cortada, más el texto "Factum" en Sora 700 convertido a trazos). Los assets de marca (SVG, favicon, PNG, `.ico`, `.icns`, íconos de bandeja) salen de un script versionado y reproducible. El emisor del informe pasa a ser la organización cliente (el primer cliente es un estudio jurídico), configurable en el backend con nombre, logo y líneas de contacto (`Branding:*`), todo vacío por defecto y fuera del repo. Factum figura siempre en el informe con la leyenda "Realizado con Factum" en el pie de página. La web muestra el nombre y el logo de la organización en el login, el UserMenu y el SiteFooter a través de un endpoint público nuevo, `GET /api/config/public`. El login contra el MPF (`MpfAuthProvider`), el contrato `GET /api/auth/mode` y la integración con Faro **no se tocan**: son de la HU `auth-e-integraciones-sin-mpf`. Los casos, informes y ZIP ya generados no se modifican (D6 A).

## 2. Toca

| Lado | ¿Toca? | Qué |
|---|---|---|
| backend (API) `server/src/Factum.Backend` | **sí** | `Branding` (opciones, servicio, endpoint público), `ReportService` (placeholders nuevos y pie de atribución), limpieza de `Templates/`, comentarios |
| backend (Tatana) `server/src/Factum.Agent` | **no** | Sin referencias institucionales (el grep de la HU marca `IosService.cs` por un falso positivo) |
| client `client/` | **sí** | Logo, favicon, metadata, login, navbar, UserMenu, SiteFooter, línea de estado, textos sueltos, `getPublicConfig` |
| agent-ui `agent-ui/` | **sí** | Marca en la sidebar, pie "Factum · Tatana v<versión>", íconos de app y de bandeja, copyright, placeholder de URL |
| otros | **sí** | `ops/brand/` (script nuevo), `README.md`, `.gitignore`, `.gitlab-ci.yml`, `server/tools/` (se borra), `AGENTS.md` (orquestador) |

---

## 3. Hallazgos de la verificación técnica

Revisé el código, la plantilla y los assets reales el 2026-10-01. Algunas cosas corrigen supuestos de la HU:

1. **El informe hoy es un DOCX, no un PDF.** `ReportService.GenerateAsync` entrega el DOCX (`pdf_filename` apunta al `.docx`; el comentario de la "Fase 2" explica que se dejó de convertir con LibreOffice). Cuando la HU dice "PDF", esta SDD entiende "el informe". El nombre del campo `pdf_filename` no cambia.
2. **`plantilla_informe_v3.docx` no tiene header ni footer de Word.** La marca vive en el **cuerpo de la página 1**: una banda negra (`wps` "Rectángulo 3", anclada a la página) con una caja de texto con dos párrafos, `INFORME DE EXTRACCION` y `            Evidentia – Web  |             Gabinete Forense Digital – CIF    |          Ministerio Publico Fiscal`, más tres imágenes ancladas encima:
   - `rId6` → `word/media/image1.png` (93×93), docPr "Imagen 9": **logo MPF** (chevron azul);
   - `rId7` → `image2.png` (84×84), docPr "Imagen 3": **sello GFD** ("Cuerpo de Investigaciones Fiscales");
   - `rId8` → `image3.png` (79×79), docPr "Gráfico 5": **logo Evidentia** (lupa con ojo).

   El texto de la banda aparece **dos veces** (`mc:Choice` y `mc:Fallback` VML). Los blobs `o:gfxdata` del fallback no contienen texto institucional. No hay más marca en el DOCX (`docProps` dice `Application = WPS Office`). La sección mide A4 con `pgMar bottom=1417 footer=708`, así que hay lugar para un pie.
3. **La plantilla se armó con WPS Office** (no con Google Docs ni Canva, como suponía la HU) y WPS está instalado en esta Mac. Editarla "en la herramienta original" (D5 A) es abrir el mismo `.docx` en WPS.
4. **Los placeholders `{DEPENDENCIA}` y `{FECHA_HORA}` no están en la v3.** Los comentarios de `ReportService` que dicen que "viven en el header" están desactualizados. No es marca, pero la plantilla nueva puede volver a usarlos.
5. **Los restos de `Templates/` son de Evidentia/GFD.** `portada-fondo.png` dice "EVIDENTIA WEB – INSPECCION TECNICA / GABINETE FORENSE DIGITAL" y tiene un correo `@mpublico.gov.ar`. `header-`, `body-` y `footer-fondo.png` son recortes de ese fondo. `server/tools/ReportTemplateBuilder/` **solo** genera `plantilla-inspeccion-tecnica.docx` a partir de esos fondos, no está en `Factum.sln` y su README no existe. Ningún `.cs` del backend referencia nada de eso. El `.csproj` copia `Templates\**\*` con un glob, así que borrar archivos no pide cambios en el `.csproj`.
6. **El JSON viaja en `snake_case_lower`**, no en camelCase: `Program.cs` L31-40 configura `JsonNamingPolicy.SnakeCaseLower` tanto en `ConfigureHttpJsonOptions` como en `AddControllers().AddJsonOptions`. No hay `DefaultIgnoreCondition`, así que los `null` se serializan como `null`.
7. **No hay autorización global.** Los controladores sin `[Authorize]` (por ejemplo `AuthController`, `TatanaUpdatesController`) son anónimos. El endpoint nuevo lleva `[AllowAnonymous]` explícito igual, porque el login lo usa antes de autenticarse.
8. **Riesgo de fuga de config del cliente por el build:**
   - `Dockerfile` hace `COPY . .` del directorio del proyecto y **no hay `.dockerignore`**.
   - El SDK Web publica los `*.json` del proyecto como Content.

   Un `appsettings.Local.json` o una carpeta de logo dentro del proyecto terminaría en la imagen y en `publish/`. Se cubre en B6.
9. **La bandeja de agent-ui no encuentra su ícono cuando la app está empaquetada.** `createTray` busca `join(__dirname, '../../resources/tray-icon.png')`, pero `build.files` solo incluye `out/**/*`, así que fuera de `dev` el path no existe y queda el ícono vacío. Además hace `resize(16)` en macOS, lo que rompe las imágenes `@2x`. Se corrige en F17.
10. **El logo actual (`logo-theme-*.svg`) es cuadrado** (export de CorelDRAW, viewBox 882×882), y `AppNavbar` y el login le ponen al lado un `<span>Factum</span>` en HTML. Con el logo horizontal nuevo, ese texto se duplicaría. Hay que sacarlo (F6 y F7).
11. **La referencia visual del artboard "A · Sello" sale del generador del artifact:**
    - logo horizontal: marca de 60 px, separación de 18 px y "Factum" en Sora 700 de 52 px, `letter-spacing: -0.02em`, `line-height: 1`, centrado vertical (`align-items: center`), color `fg`;
    - en la navbar: marca de 28 px, separación de 12 px y texto de 20 px;
    - el "ícono de app" es la marca sola, sin otro fondo.

    De ahí salen las proporciones de la sección 6.

---

## 4. Modelo de datos y configuración

### 4.1 MongoDB

**No se crean ni se modifican colecciones, documentos ni índices.** El nombre, el logo y el contacto de la organización **no se guardan en el caso**: se leen de la config en el momento de generar el informe y quedan "congelados" dentro del DOCX y el ZIP de ese caso (que tiene hash). Los informes ya generados no cambian (D6 A). Si cambia la config, solo cambian los informes que se generen después.

### 4.2 Sección `Branding` (nueva)

| Clave | Tipo | Default | Uso |
|---|---|---|---|
| `Branding:OrganizationName` | `string` | `""` | Nombre del emisor (p. ej. "Estudio Jurídico Ejemplo" o "Dr. Nombre Apellido · Dra. Nombre Apellido"). Se recorta con trim. Si queda vacío, equivale a "no configurado". Máximo 150 caracteres: si es más largo, se trunca y se loguea un warning. |
| `Branding:OrganizationLogo` | `string` (ruta a un archivo) | `""` | Logo del emisor. Ver 4.3. |
| `Branding:ContactLines` | `string[]` | `[]` | Líneas libres para domicilio, teléfonos, correo, matrícula, etc. A cada una se le aplica trim y se descartan las vacías. Máximo **6** líneas de **150** caracteres: lo que sobra se descarta o se trunca, con un warning. |

En el `appsettings.json` versionado la sección va **vacía**:

```json
"Branding": {
  "OrganizationName": "",
  "OrganizationLogo": "",
  "ContactLines": []
}
```

### 4.3 Logo de la organización: carga segura

- **Solo lo configura un administrador**, por config o variable de entorno. Nunca viene de un request HTTP: no hay subida desde la UI (sigue fuera de alcance).
- **Ruta:** absoluta, o relativa a `IWebHostEnvironment.ContentRootPath` (el directorio del proyecto con `dotnet run`, `/app` en Docker). Se resuelve una sola vez con `Path.GetFullPath`.
- **Formatos:** **PNG o JPEG**, validados por **magic bytes** (no por la extensión): PNG `89 50 4E 47 0D 0A 1A 0A`, JPEG `FF D8 FF`. **SVG no**: puede traer scripts si se sirve al navegador y el DOCX necesita un raster de respaldo igual. Para un logo con fondo transparente, se recomienda PNG.
- **Tamaño:** como máximo **1 MiB** (1 048 576 bytes) y entre **16 y 4096 px** por lado. Las dimensiones se leen con un helper estricto nuevo (B3), que no cae al `(800, 600)` por defecto de `GetImageSize`.
- **Cuándo se carga:** **una sola vez, al arrancar**, en memoria: bytes, content-type, ancho, alto y `Version` = los primeros 12 hex del SHA-256. Para cambiar el logo hay que reiniciar el backend (se documenta en el README).
- **Si es inválido** (no existe, es demasiado grande, el formato está mal o las dimensiones no entran): se loguea **un** warning con el motivo (`Branding: logo ignorado (<motivo>)`) y el sistema sigue como si no hubiera logo. **El arranque no falla y la generación de informes tampoco.**
- **Al navegador se sirve desde memoria** (B7): no hay acceso a disco por request, así que no hay path traversal posible. Lleva `Content-Type` exacto y `X-Content-Type-Options: nosniff`.

### 4.4 Dónde vive la config real del cliente (fuera del repo)

Hoy el `.gitignore` ignora `.env`, `.env.local`, `*.env.local`, `**/dev-data/`, `**/bin/` y `**/obj/`. **`appsettings.Development.json` está versionado** (`git ls-files` lo lista), así que **no sirve** para datos del cliente. Decisión (T3):

| Entorno | Mecanismo | Detalle |
|---|---|---|
| Desarrollo local | **`server/src/Factum.Backend/appsettings.Local.json`**, ignorado por git | Se carga con `optional: true` después de `appsettings.{Environment}.json` y **antes** de las variables de entorno y la línea de comandos, para que estas sigan ganando (B4). El logo de prueba va en `server/src/Factum.Backend/branding/` (carpeta ignorada) y en la config se pone `"OrganizationLogo": "branding/logo.png"`. |
| Docker / producción | **Variables de entorno** (`Branding__OrganizationName`, `Branding__OrganizationLogo`, `Branding__ContactLines__0`, `Branding__ContactLines__1`, …) **o** un `appsettings.Local.json` montado como volumen de solo lectura en `/app/appsettings.Local.json` | El logo se monta como volumen de solo lectura (p. ej. `/app/branding/logo.png`). `docker-compose.yml` **no cambia**: el README trae el ejemplo con placeholders. |
| user-secrets | **No se usa** | Solo funciona en Development, pide `UserSecretsId` y no cubre producción. |

Lo que asegura que no se filtre (B6):

- `.gitignore` suma `**/appsettings.Local.json` y `server/src/Factum.Backend/branding/`.
- Nuevo `server/src/Factum.Backend/.dockerignore` con `appsettings.Local.json`, `branding/`, `bin/`, `obj/` y `dev-data/`.
- En el `.csproj`, `<Content Remove="appsettings.Local.json" />` y `<Content Remove="branding\**" />` (más los `<None Remove>` equivalentes), para que no se copien a `bin/` ni a `publish/`.

Ejemplo **ficticio** de `appsettings.Local.json` (el que va en el README; solo placeholders):

```json
{
  "Branding": {
    "OrganizationName": "Dr. Nombre Apellido · Dra. Nombre Apellido",
    "OrganizationLogo": "branding/logo.png",
    "ContactLines": [
      "Calle Ejemplo 123, Ciudad",
      "Cel. +54 9 000 000-0000 · +54 9 000 000-0000"
    ]
  }
}
```

---

## 5. Endpoints

| Método | Ruta | Auth | Entrada | Respuesta |
|---|---|---|---|---|
| `GET` | `/api/config/public` | anónimo (`[AllowAnonymous]`) | — | `200` `PublicConfigResponse` (ver 7) |
| `GET` | `/api/config/branding/logo` | anónimo | query `v` opcional (cache-busting, se ignora del lado del server) | `200` con los bytes, `Content-Type: image/png` o `image/jpeg`, `X-Content-Type-Options: nosniff`, `Cache-Control: public, max-age=3600`, `ETag: "<Version>"`. Si llega `If-None-Match` igual al ETag, `304`. Si no hay logo válido, `404` con `{ "error": "Sin logo configurado" }`. |
| `GET` | `/api/auth/mode` | — | — | **Sin cambios** (`{ "mode": "dev" \| "mpf" }`). Su rediseño es de la HU 2. |

- **`ContactLines` no se expone** al frontend: hoy ninguna pantalla lo necesita, y menos datos públicos es mejor.
- `/health` no cambia.
- **No hay mensajes de WebSocket nuevos** ni cambios en Tatana.
- **Compatibilidad hacia adelante:** la HU 2 puede sumar a `PublicConfigResponse` campos como `support_enabled` o `auth_mode` sin romper el cliente, que ignora los campos que no conoce.

---

## 6. Logo "Sello" y assets generados

### 6.1 Geometría (fija, sale de la HU y del artboard A)

- **Marca** (viewBox `0 0 64 64`): `<rect width="64" height="64" rx="14" fill="{acento}"/>` + `<path d="M20 14H40L46 20V23H30V29H40V37H30V50H20Z" fill="{glifo}"/>`.
  - Oscura: acento `#7fd34e`, glifo `#0b0c0e`.
  - Clara: acento `#2f6f12`, glifo `#ffffff`.
- **Logo horizontal** (proporciones del artboard: marca 60, separación 18, texto 52):
  - alto del viewBox **64**, marca de 64×64 en `x=0`;
  - texto desde `x = 64 + 19.2` (18/60·64), con `font-size = 55.47` (52/60·64), Sora Bold, `letter-spacing = -0.02em`, kerning de la fuente activo;
  - **línea base** calculada como en CSS con `line-height: 1` centrado en la marca: `top = 32 − fs/2`, `baseline = top + (fs − (A + D))/2 + A`, donde `A` y `D` son `hhea.ascender` y `|hhea.descender|` escalados a `fs`;
  - ancho del viewBox = fin del último glifo (sin el tracking final), redondeado hacia arriba a 0.5;
  - color del texto: `#f3f5f7` (oscuro) / `#0e1013` (claro).

  El texto va **convertido a trazos** (un `<path>` por glifo o uno combinado), **sin `<text>`, sin `<style>` y sin `@font-face`**.
- **Variante canónica para íconos de app:** la **oscura** (`#7fd34e` / `#0b0c0e`), según DP4 (no bloquea).

### 6.2 Archivos generados (todos versionados)

| Archivo | Contenido | Consumidor |
|---|---|---|
| `client/public/logo-theme-dark.svg` | Logo horizontal oscuro (**reemplaza** el actual, mismo nombre) | AppNavbar, SiteFooter, 404, login |
| `client/public/logo-theme-white.svg` | Logo horizontal claro (mismo nombre) | ídem |
| `client/public/logo-mark-dark.svg` / `logo-mark-white.svg` | Solo la marca | AppNavbar en `< sm` |
| `client/public/icon.svg` | Marca canónica | `metadata.icons.icon` (favicon SVG para navegadores modernos) |
| `client/public/logo-app.ico` | ICO 16/32/48 (PNG embebidos) de la marca canónica (mismo nombre) | `metadata.icons.icon` (respaldo) |
| `client/public/icon-512.png` | PNG 512×512 de la marca canónica con esquinas transparentes | `metadata.icons.icon` |
| `client/public/apple-icon.png` | PNG 180×180 a **sangre completa** (cuadrado de acento sin `rx`, porque iOS redondea solo y las esquinas transparentes quedan negras). Glifo con la misma proporción respecto del lado. | `metadata.icons.apple` |
| `agent-ui/resources/icon.png` | PNG 1024×1024 a sangre completa con `rx` (Linux) | `build.linux.icon` |
| `agent-ui/resources/icon.ico` | ICO 16/24/32/48/64/128/256 | `build.win.icon` |
| `agent-ui/resources/icon.icns` | ICNS con entradas PNG `icp4`(16), `icp5`(32), `icp6`(64), `ic07`(128), `ic08`(256), `ic09`(512), `ic10`(1024), `ic11`(32), `ic12`(64), `ic13`(256), `ic14`(512). Marca escalada a **824/1024** y centrada (grilla de íconos de macOS: margen transparente). | `build.mac.icon` |
| `agent-ui/resources/tray/trayTemplate.png` + `trayTemplate@2x.png` | 16 y 32 px, **template monocromo** para macOS: la silueta del cuadrado redondeado en `#000000` con la "F" **calada** (transparente). Un relleno negro con alfa es lo que pide macOS. | Bandeja en macOS (D9 B) |
| `agent-ui/resources/tray/tray.png` + `tray@2x.png` | 32 y 64 px a color (canónica) | Bandeja en Linux |
| `agent-ui/resources/tray/tray.ico` | ICO 16/24/32/48 a color | Bandeja en Windows |
| `server/src/Factum.Backend/Templates/factum-sello.png` | PNG 256×256 de la marca **clara** (`#2f6f12` / `#ffffff`, porque se imprime sobre papel blanco) | Pie de atribución del informe (B8) |

Se **borran**:

- `client/public/MPF_logo.png`, `mpfs.png` y `GFD_logo.png`;
- `agent-ui/resources/tray-icon.png`: no existe, así que no hay nada que borrar. Solo se deja de referenciar.

### 6.3 Script reproducible (versionado)

- **Ubicación:** `ops/brand/` (al lado de `ops/harness/`), **versionado**:
  - `ops/brand/package.json`: `private`, `"type": "module"`, dependencias **fijadas** `opentype.js` (≥ 1.3.4) y `@resvg/resvg-js` (2.6.x); `package-lock.json` versionado; `node_modules/` ya lo ignora la regla `**/node_modules/`.
  - `ops/brand/build-brand.mjs`: genera todo lo de 6.2 y se corre con `cd ops/brand && npm ci && node build-brand.mjs`.
  - `ops/brand/fonts/Sora-Bold.ttf` + `ops/brand/fonts/OFL.txt`: TTF **estático** oficial del repo de Sora (`github.com/sora-xyz/Sora-Font`, carpeta `fonts/ttf`). La OFL permite redistribuirla junto con su licencia. El README del script registra la URL exacta y el SHA-256 del TTF. Si solo se consigue la variable, se instancia una vez a `wght=700` con `fontTools.varLib.instancer` y se commitea el estático resultante. En ese caso hay que revisar `OFL.txt`: si declara "Reserved Font Name", el archivo derivado se renombra (p. ej. `FactumWordmark-Bold.ttf`).
  - `ops/brand/README.md`: qué genera, cómo correrlo y de dónde sale la fuente.
- **Cómo lo hace:**
  - **Texto a trazos:** `font.getPath("Factum", x, baseline, fs, { kerning: true, letterSpacing: -0.02 })`, o aplicando el tracking a mano entre glifos si la versión de opentype.js no soporta la opción. Después, `path.toPathData(2)`, con 2 decimales fijos para que la salida sea determinista.
  - **Rasterizado:** `@resvg/resvg-js`, a partir del SVG de la marca, sin dependencias del sistema.
  - **Contenedores:** **ICO** e **ICNS** se escriben a mano con PNG embebidos (cabecera ICONDIR de 6 bytes + entradas de 16 bytes + datos; `icns` + longitud + bloques `tipo/longitud/png`), sin dependencias extra.
  - **Prohibido:** `rsvg-convert`, `iconutil` o ImageMagick como requisito (no son portables a Windows ni a CI).
- **Determinismo:** dos corridas seguidas tienen que dar los mismos bytes (`shasum` igual, `git status` limpio después de la segunda). Sin timestamps ni metadata variable en PNG ni ICO.

---

## 7. Contrato compartido (client ↔ server)

Casing **real**: `snake_case_lower` (ver hallazgo 6).

### `GET /api/config/public`

```json
{
  "organization_name": "Estudio Jurídico Ejemplo",
  "organization_logo_url": "/api/config/branding/logo?v=1a2b3c4d5e6f"
}
```

Cuando no hay nada configurado: `{ "organization_name": null, "organization_logo_url": null }`.

| Campo JSON | Tipo | Regla | C# (`server/src/Factum.Backend/DTOs/ConfigDtos.cs`) | TS (`client/src/lib/api.ts`, re-export en `client/src/types/index.ts`) |
|---|---|---|---|---|
| `organization_name` | `string \| null` | `null` si `Branding:OrganizationName` queda vacío después del trim | `string? OrganizationName` | `organization_name: string \| null` |
| `organization_logo_url` | `string \| null` | **Ruta relativa a la raíz del backend**, empieza con `/`. `null` si no hay logo válido. Incluye `?v=<Version>` para invalidar caché. | `string? OrganizationLogoUrl` | `organization_logo_url: string \| null` |

- **C#:** `public sealed record PublicConfigResponse(string? OrganizationName, string? OrganizationLogoUrl);`
- **TS:** `export interface PublicConfig { organization_name: string | null; organization_logo_url: string | null; }`, `api.getPublicConfig(): Promise<PublicConfig>` (usa `request`; el token es opcional y el endpoint lo ignora) y `api.brandingLogoURL(path: string): string` → `` `${BACKEND_URL}${path}` ``.

### `GET /api/config/branding/logo`

Imagen binaria. Del lado del cliente solo se usa como `src` de `<img>`. No hay tipo TS.

### Sin cambios

- `GET /api/auth/mode` → `{ "mode": "dev" | "mpf" }`: `api.getMode()`, `SystemStatusLine` y `DevModeBanner` no cambian.
- `User { dni, name, sigla }` y `Case` no cambian.
- Tatana (API local + WebSocket) no cambia.

---

## 8. Informe: placeholders, atribución y plantilla

### 8.1 Placeholders nuevos (`ReportService`)

Se buscan en **el cuerpo, todos los `HeaderPart` y todos los `FooterPart`**, en **todas** las copias (`mc:Choice` y `mc:Fallback`), igual que hoy con las fotos.

| Placeholder | Tipo | Reemplazo |
|---|---|---|
| `{ORGANIZACION}` | texto | `OrganizationName` o `""`. Va en su propio run, **sin separadores literales pegados** en la plantilla (si el nombre está vacío, no queda un " \| " huérfano). |
| `{CONTACTO}` | texto multilínea | Las `ContactLines` en el **mismo run**, separadas por `<w:br/>`, con el formato del run original. Si no hay líneas, `""`. |
| `{CONTACTO_EN_LINEA}` | texto | Las `ContactLines` unidas con `" · "` (para una banda de una sola línea). Si no hay líneas, `""`. |
| `{LOGO_ORGANIZACION}` o `{LOGO_ORGANIZACION:<ancho>x<alto>}` | imagen, a nivel de párrafo | Con logo: el párrafo se reemplaza por la imagen **ajustada sin recortar** (fit) dentro de la caja. La caja por defecto mide **5.0 × 1.5 cm**; el sufijo opcional va en cm, con punto o coma decimal (p. ej. `{LOGO_ORGANIZACION:4x1.2}`). Se **conservan las `ParagraphProperties` del párrafo original** (alineación) y la imagen se agrega como `ImagePart` **de la parte que contiene el párrafo** (Main, Header o Footer). Sin logo: se borra solo el texto del placeholder y queda el párrafo vacío, para no romper el layout de la caja. Regex: `\{LOGO_ORGANIZACION(?::(\d+(?:[.,]\d+)?)x(\d+(?:[.,]\d+)?))?\}`. |

Los placeholders actuales (`{NROREF}`, `{MARCA}`, …, `{DEPENDENCIA}`, `{FECHA_HORA}`) siguen funcionando igual. El reemplazo de texto pasa a aplicarse también a los header y footer que existan (hoy ya lo hace).

### 8.2 Atribución obligatoria "Realizado con Factum"

Recomendación (DP3, no bloquea; se implementa así salvo que el usuario diga otra cosa):

- **Pie de página de todas las páginas**, inyectado **por código**. Así no depende de que la plantilla lo traiga y ninguna plantilla de cliente lo puede sacar.
- **Contenido:** un párrafo centrado con la marca "Sello" clara (`Templates/factum-sello.png`) **inline a 0.4 × 0.4 cm** (144 000 EMU), un espacio y el texto `Realizado con Factum` en Arial 8 pt, color `#5C656E`.
- **Si `factum-sello.png` falta o no se puede leer:** queda solo el texto y se loguea un warning. El informe **nunca** falla por esto.
- **Algoritmo:**
  1. Para cada `SectionProperties` del documento (la del final del body y las que estén dentro de párrafos), se miran sus `FooterReference`.
  2. Si hay footer `Default`, se **agrega** el párrafo de atribución al final de ese `FooterPart`. Si varias secciones comparten la parte, se agrega una sola vez.
  3. Si no hay footer `Default`, se crea **un** `FooterPart` con el párrafo y se le agrega a cada `sectPr` un `FooterReference { Type = Default }` en el orden que pide el esquema (después de los `HeaderReference`, antes del resto).
  4. Si la sección tiene `TitlePage` (`titlePg`), lo mismo con el footer `First`: se reutiliza la misma parte si no existe.
  5. Si `settings.xml` tiene `evenAndOddHeaders`, lo mismo con `Even`.
  6. Los `docPr id` siguen el contador `drawId` (únicos en todo el documento).

### 8.3 Plantilla

- **`B-PLANTILLA` (bloqueado por DP1 hasta que llegue el informe de referencia):**
  - nace `server/src/Factum.Backend/Templates/plantilla_informe_v4.docx` a partir de la referencia;
  - se borra la v3 y se cambia el nombre en `ReportService` (una línea);
  - la plantilla tiene que cumplir el contrato de 8.1: `{LOGO_ORGANIZACION…}` y `{ORGANIZACION}` en la portada y/o el encabezado, `{CONTACTO}` o `{CONTACTO_EN_LINEA}` donde corresponda, y los placeholders de datos que quiera conservar;
  - **sin** logos ni textos de Factum, MPF, GFD ni Evidentia en el arte: la atribución la pone el código;
  - los ajustes finos de layout de `ReportService` (`MoveContentTableUp`, reordenamiento del panel de fotos, `PhotoMaxW/H`, L139-255 y L416-418) están calibrados para la v3. Cuando llegue la referencia, **el `architect` agrega un anexo a esta SDD** con los ajustes concretos, antes de implementar este ítem.
- **`B-PLANTILLA-INTERINA` (si DP2 = sí, recomendado; no está bloqueado):** parche quirúrgico de la v3 para que **desde ya** ningún informe nuevo salga con la marca MPF/GFD/Evidentia (es el fallback B de D5). Sobre `word/document.xml` y las relaciones:
  1. Borrar los runs que contienen los `w:drawing` con docPr "Imagen 9" (`rId6`), "Imagen 3" (`rId7`) y "Gráfico 5" (`rId8`).
  2. Borrar las `Relationship` `rId6`, `rId7` y `rId8` de `word/_rels/document.xml.rels` y los archivos `word/media/image1.png`, `image2.png` e `image3.png`.
  3. En **las dos** copias de la caja de texto de "Rectángulo 3" (Choice y Fallback), reemplazar el texto `            Evidentia – Web  |             Gabinete Forense Digital – CIF    |          Ministerio Publico Fiscal` por `{ORGANIZACION}`, conservando el run y su formato (Arial, gris D9D9D9).
  4. `INFORME DE EXTRACCION` y el resto quedan igual. La v3 interina **no** lleva `{LOGO_ORGANIZACION}`: un logo de cliente sobre la banda negra puede no verse, así que el logo llega con la v4.
  5. Se hace con un script descartable en el scratchpad (Python `zipfile` o .NET OpenXML); el script no se versiona. Hay que reempaquetar el ZIP respetando `[Content_Types].xml` primero.
  6. Verificar que el DOCX abre en LibreOffice (`soffice --headless --convert-to pdf`) y que la página 1 se ve igual que antes, salvo la banda.

---

## 9. Decisiones técnicas

> Las D1-D10 de la HU son de producto. Acá van las técnicas (T*). Las que necesitan al usuario están en la sección 10.

- **T1. Endpoint público nuevo `GET /api/config/public`** en vez de tocar `/api/auth/mode`: respeta el corte con la HU 2 y la HU 2 lo puede ampliar.
- **T2. El logo se sirve desde memoria** y no se pasa como data URL en el JSON, para que el JSON sea liviano y la imagen se pueda cachear con ETag.
- **T3. La config del cliente vive en `appsettings.Local.json` (ignorado por git) o en variables de entorno** (sección 4.4). `appsettings.Development.json` está versionado y no sirve para esto. Se cierran las fugas por Docker y por publish.
- **T4. Validación del logo:** PNG o JPEG por magic bytes, máximo 1 MiB, de 16 a 4096 px. Si es inválido, se loguea un warning y se sigue sin logo.
- **T5. La atribución se inyecta por código en el pie** (8.2): queda garantizada aunque la plantilla sea del cliente.
- **T6. Los informes no guardan la identidad de la organización en Mongo:** queda solo dentro del DOCX y el ZIP con hash. Es coherente con D6 y con la integridad de la evidencia.
- **T7. Se mantienen los nombres de archivo `logo-theme-*.svg` y `logo-app.ico`**, que ahora pasan a ser el logo horizontal. Se suman `logo-mark-*.svg`, `icon.svg`, `icon-512.png` y `apple-icon.png`. Se usa la **metadata explícita** de `layout.tsx` (la HU pide que el PNG esté "referenciado desde la metadata") y **no** los archivos `app/icon.*` por convención. Además, no puede haber un `app/favicon.ico` que compita.
- **T8. Login:**
  - **Arriba a la izquierda:** el bloque de organización solo aparece si hay nombre o logo (logo a `h-6`, `max-w-[160px]`, `object-contain`, y el nombre al lado).
  - **Abajo:** el logo horizontal según el tema (como hoy, con `isDark`) y **sin** el `<span>Factum</span>`. El glow cian `rgba(0,204,255,.25)` es del branding viejo: pasa a `rgba(127,211,78,.22)` en oscuro y la sombra neutra queda en claro.
  - **No se toca** el resto del hero: los colores `#fff` del tagline son de la HU `rediseno-pagina-inicio`.
- **T9. `UserMenu` consume `usePublicConfig()` directamente.** Es un client component y así se evita pasar la prop por varios niveles. `SiteFooter` sigue sin hooks y recibe la prop `organization`.
- **T10. Pie de la sidebar de agent-ui:** `Factum · Tatana v{versión}`, donde la versión es la de `agent-ui/package.json` (la misma que compara electron-updater), inyectada con `define` de Vite. No se usa la `APP_VERSION` de la web porque son versiones distintas.
- **T11. Bandeja:**
  - En macOS: `trayTemplate.png` + `@2x`, con `setTemplateImage(true)` y sin `resize`.
  - En Windows: `tray.ico`. En Linux: `tray.png` + `@2x`.
  - Los íconos viajan con `extraResources` → `tray/`, y la ruta se arma con `app.isPackaged ? join(process.resourcesPath, 'tray') : join(__dirname, '../../resources/tray')`.
- **T12. Se borra `server/tools/ReportTemplateBuilder/`** con todos sus insumos (`*-fondo.png`, `plantilla-inspeccion-tecnica*`): solo sirve para regenerar el arte de Evidentia/GFD. Es fácil de recuperar del historial de git si hiciera falta.
- **T13. Excepciones permitidas al grep de verificación** (son de la HU 2; no se tocan acá):
  1. `server/src/Factum.Backend/Services/Auth/MpfAuthProvider.cs` (el archivo entero);
  2. en `server/src/Factum.Backend/Program.cs`: `Configure<MpfOptions>`, el comentario `// ── Auth provider (dev o MPF según config)`, `authMode == "mpf"` y `MpfAuthProvider`;
  3. en `server/src/Factum.Backend/appsettings.json`: `MpfBaseUrl`, `MpfLoginPath` y `MpfTimeoutSeconds`;
  4. en `client/src/lib/api.ts`: el literal `"mpf"` de `getMode` (2 apariciones);
  5. en `client/src/components/DevModeBanner.tsx`: el literal `"mpf"`;
  6. en `README.md`: las menciones al **modo** `mpf` y a las claves `Auth:Mpf*`, en la tabla de config, en la sección de login y en el árbol (`Auth/ # Proveedores de identidad (Dev/Mpf)`). Van con prosa neutra ("proveedor HTTP externo"), sin "Ministerio".

  Fuera de esa lista: **cero coincidencias**. Los comentarios del código de Faro **sí** se neutralizan (editar un comentario no es renombrar).
- **T14. Faro conserva su nombre** ("Faro - Sistema de tokens", "Ver todo en Faro"): es un producto del usuario (D3). En `SoporteModal` solo se neutraliza "el equipo del GFD" → "el equipo de soporte".

---

## 10. Decisiones pendientes del usuario

- **DP1. Informe de referencia (bloquea solo `B-PLANTILLA`).** ¿En qué formato llega: `.docx` editable (idealmente WPS o Word) o PDF/imagen? ¿Quién arma la `plantilla_informe_v4.docx`?
  - **Recomendado:** si es un `.docx`, lo entregás y `implementer-backend` le pone los placeholders de 8.1 (después del anexo del `architect`).
  - Si es un PDF, conviene que lo rearmes vos (o un diseñador) en WPS siguiendo el contrato de 8.1: recrear un layout fijo por código es lo más riesgoso para la paginación.
- **DP2. ¿Parche interino de la v3 mientras no llega la referencia?**
  - **Recomendado: sí** (`B-PLANTILLA-INTERINA`). Saca la marca MPF, GFD y Evidentia de los informes nuevos ya mismo y deja `{ORGANIZACION}` en la banda. Con eso la HU puede pasar a `aprobada` sin esperar la referencia, y la v4 queda como una tarea de seguimiento.
  - Si no se hace, la HU no puede cumplir el Gherkin "Informe nuevo sin marca institucional" hasta que se cierre `B-PLANTILLA`.
- **DP3. Atribución (no bloquea; se implementa lo recomendado salvo otra indicación).**
  - **Recomendado:** pie de página en **todas** las páginas, con la marca "Sello" a 0.4 cm + "Realizado con Factum" en 8 pt gris (8.2).
  - Alternativas: un colofón solo al final del informe, o el pie sin el logo. ¿Agregar la versión ("Realizado con Factum v2.0.0") para tener trazabilidad forense?
- **DP4. Variante de color de los íconos de app (no bloquea).**
  - **Recomendado:** la oscura (`#7fd34e` + "F" `#0b0c0e`) para el favicon, `icon-512`, `apple-icon` y los íconos de Tatana: es la del arranque de la app y se distingue en pestañas claras y oscuras.
  - La alternativa es la clara (`#2f6f12` + "F" blanca).
- **DP5. Riesgo de vocabulario para el primer cliente (fuera de alcance por D8 A; solo se registra).**
  - **En el wizard y la web:** "Fiscal", "DNI Fiscal", "Foto del fiscal", "Buscar por fiscal o sigla…". **En el informe:** "Funcionario / Empleado" y "Denunciante / Involucrado". Nada de eso encaja con un estudio jurídico.
  - Las etiquetas del informe son **texto fijo de la plantilla**, así que la v4 (DP1) las puede adaptar **sin código**. El wizard y la web, no.
  - **Recomendado:** abrir la HU `terminologia-configurable` antes de entregar al primer cliente.

---

## 11. Checklist atómico

### 11.1 `implementer-backend` (`server/`, README, `.gitignore`, `.gitlab-ci.yml`)

> **Regla dura:** esta HU **no escribe en la base**. Ningún paso crea, modifica ni borra documentos de Mongo, ni archivos de `Storage:DataDirectory`. Las pruebas del informe se hacen con un arnés descartable en el scratchpad (B14). Los datos reales del cliente no van en ningún archivo versionado ni en `progress/`.

- [ ] **B1.** Crear `server/src/Factum.Backend/Services/Branding/BrandingOptions.cs` con `OrganizationName` (`string`, `""`), `OrganizationLogo` (`string`, `""`) y `ContactLines` (`List<string>`, `[]`).
- [ ] **B2.** Crear `Services/Branding/BrandingService.cs` con:
  - `IBrandingService` y la implementación `BrandingService` (singleton);
  - `BrandingSnapshot { string? OrganizationName; IReadOnlyList<string> ContactLines; BrandingLogo? Logo }` y `BrandingLogo { byte[] Data; string ContentType; string Extension; int Width; int Height; string Version }`;
  - el cálculo, **una vez en el constructor**, con las reglas de 4.2 y 4.3 (trim, límites, warnings con `ILogger`, ruta relativa a `ContentRootPath`).
- [ ] **B3.** Crear un helper estricto `Services/Branding/ImageProbe.cs`: `TryDetect(byte[] data, out string contentType, out int w, out int h)`, para PNG (IHDR) y JPEG (SOF), que devuelve `false` si algo no cierra. **No** cambiar `ReportService.GetImageSize`.
- [ ] **B4.** En `Program.cs`:
  - después de `CreateBuilder`, agregar `appsettings.Local.json` (`optional: true`, `reloadOnChange: false`) **antes** de las variables de entorno y la línea de comandos. Por ejemplo, insertar el `JsonConfigurationSource` justo después del último `appsettings*.json` de `builder.Configuration.Sources`, o agregarlo y después volver a llamar a `AddEnvironmentVariables()` y `AddCommandLine(args)`. Dejar un comentario explicando el orden;
  - `Configure<BrandingOptions>(GetSection("Branding"))` y `AddSingleton<IBrandingService, BrandingService>()`;
  - después de `Build()`, resolver `IBrandingService` una vez, para que los warnings salgan al arrancar.
- [ ] **B5.** Agregar a `appsettings.json` la sección `Branding` vacía (4.2). No tocar `appsettings.Development.json`.
- [ ] **B6.** Cerrar las fugas:
  - `.gitignore` (raíz): sumar `**/appsettings.Local.json` y `server/src/Factum.Backend/branding/`;
  - crear `server/src/Factum.Backend/.dockerignore` con `appsettings.Local.json`, `branding/`, `bin/`, `obj/` y `dev-data/`;
  - `Factum.Backend.csproj`: `<Content Remove="appsettings.Local.json" />`, `<None Remove="appsettings.Local.json" />`, `<Content Remove="branding\**" />` y `<None Remove="branding\**" />`;
  - verificar con `git check-ignore -v` que los dos paths quedan ignorados.
- [ ] **B7.** Crear `DTOs/ConfigDtos.cs` (`PublicConfigResponse`, sección 7) y `Controllers/ConfigController.cs` (`[ApiController]`, `[Route("api/config")]`, `[AllowAnonymous]`) con:
  - `GET public` → `organization_name` y `organization_logo_url` = `/api/config/branding/logo?v={Version}`, o `null`;
  - `GET branding/logo` → `File(bytes, contentType)` + `nosniff`, `Cache-Control` y `ETag`; `304` si coincide `If-None-Match`; `404 { error }` si no hay logo.
- [ ] **B8.** Cambios en `ReportService`:
  - [ ] **B8a.** Inyectar `IBrandingService` y `ILogger<ReportService>` por constructor primario, y pasar el snapshot a `GenerateDocxAsync`. La firma de `IReportService` y el registro singleton no cambian.
  - [ ] **B8b.** Sumar `{ORGANIZACION}`, `{CONTACTO_EN_LINEA}` y `{CONTACTO}` (este último con `<w:br/>`; implementarlo como un paso especial antes de `ReplaceTextInBody`, porque ese método hoy junta todo en un solo `Text`) en el body, los headers y los footers.
  - [ ] **B8c.** Refactorizar `BuildImageParagraph` y `ReplacePlaceholderWithImage` para que reciban la **parte dueña** (`OpenXmlPart`: `MainDocumentPart`, `HeaderPart` o `FooterPart`) y una fuente de bytes (ruta **o** `byte[]` + tipo), sin cambiar el comportamiento actual de fotos y capturas.
  - [ ] **B8d.** Implementar `{LOGO_ORGANIZACION[:WxH]}` (8.1) en el body, los headers y los footers, en todas las copias (Choice y Fallback).
  - [ ] **B8e.** Implementar el pie de atribución (8.2) con `Templates/factum-sello.png` y el texto como respaldo si falta la imagen.
  - [ ] **B8f.** Actualizar los comentarios desactualizados: el de L416 (`server/tools/ReportTemplateBuilder` → "paneles de foto de la plantilla v3") y los que dicen que `{DEPENDENCIA}`/`{FECHA_HORA}` viven en el header.
- [ ] **B9.** Borrar:
  - de `Templates/`: `mpfs.png`, `GFD_logo.png`, `header-fondo.png`, `footer-fondo.png`, `body-fondo.png`, `portada-fondo.png`, `plantilla-inspeccion-tecnica.docx`, `plantilla-inspeccion-tecnica-v2.docx`, `plantilla-inspeccion-tecnica.pdf` y `plantilla-inspeccion-tecnica.docx.orig-backup`;
  - `Services/Reports/Factum-GFD.code-workspace`;
  - la carpeta `server/tools/ReportTemplateBuilder/` entera (y `server/tools/` si queda vacía).

  Se **conservan** `plantilla_informe_v3.docx` y `sin-foto-placeholder.png`.
- [ ] **B10.** Neutralizar comentarios del server, sin renombrar símbolos:
  - `AgentAuditController.cs` L11: "login de MPF/Faro" → "del proveedor de identidad";
  - `ISupportService.cs` L18, `SupportController.cs` L43 y `SupportService.cs` L24-25: "identidad de MPF" → "la misma identidad (DNI)", "soporte del GFD" → "mesa de soporte";
  - `TatanaUpdatesService.cs` L14: `https://gitlab.com/mpf/factum` → `https://gitlab.com/<grupo>/factum`.
- [ ] **B11.** `.gitlab-ci.yml` L48: `https://factum.mpf.gob.ar` → `https://factum.example.com`.
- [ ] **B12.** `README.md`:
  - descripción del producto sin GFD ni MPF (L4-9);
  - Faro como "mesa de ayuda (sistema de tokens)", sin "del GFD" (L11-14, L45, diagrama);
  - modo `mpf` descrito como "proveedor HTTP externo" (excepción T13.6);
  - sección nueva **"Identidad de la organización (Branding)"**: claves, formatos y límites del logo, `appsettings.Local.json` ignorado, variables de entorno, volumen Docker de solo lectura, reinicio para aplicar cambios, ejemplo **con placeholders** (4.4) y la atribución fija "Realizado con Factum";
  - placeholders de la plantilla (8.1).
- [ ] **B13. `B-PLANTILLA-INTERINA`** (solo si DP2 = sí): el parche de 8.3 sobre `plantilla_informe_v3.docx`.
- [ ] **B-PLANTILLA. ⛔ BLOQUEADO** hasta DP1, la llegada del informe de referencia y el anexo del `architect`: `plantilla_informe_v4.docx` según 8.3.
- [ ] **B14. Verificación del informe sin tocar la base ni Storage:**
  - arnés descartable en el scratchpad: un proyecto de consola que referencia `Factum.Backend.csproj`, arma un `Case` en memoria y llama a `ReportService.GenerateAsync` con un `caseDir` dentro del scratchpad, con un par de PNG de prueba;
  - tres corridas: (a) sin `Branding`; (b) con nombre, dos líneas de contacto con placeholders y un logo de prueba generado (p. ej. un PNG de un color); (c) con un logo inválido (un `.png` que en realidad es texto) → warning y sin logo;
  - para probar `{LOGO_ORGANIZACION}`, `{CONTACTO}` y el sufijo de tamaño, usar una **copia** de la plantilla en el scratchpad con esos placeholders agregados (no se commitea);
  - renderizar con `soffice --headless --convert-to pdf` y revisar la página 1 y el pie;
  - nada del arnés entra al repo.
- [ ] **B15.** Escribir `progress/impl_backend_marca-comercial-sin-mpf-gfd.md` (archivos tocados, verificación, estado de B13/B-PLANTILLA), **sin datos reales del cliente**.

### 11.2 `implementer-frontend` (`client/`, `agent-ui/`, `ops/brand/`)

> Skills obligatorias: `ui-ux-pro-max` (antes del JSX final), `senior-frontend`, `3d-web-experience` (como criterio, sin agregar 3D) y `web-design-guidelines` (autochequeo al final); `ui-styling` si corresponde. Antes de tocar la metadata, leer `client/AGENTS.md` y `client/node_modules/next/dist/docs/01-app/03-api-reference/03-file-conventions/01-metadata/app-icons.md` y `.../04-functions/generate-metadata.md` (sección `icons`).

**Marca y assets**

- [ ] **F1.** Crear `ops/brand/` (6.3): `package.json` + lock, `build-brand.mjs`, `fonts/Sora-Bold.ttf` + `fonts/OFL.txt` y `README.md` (URL de la fuente + SHA-256).
- [ ] **F2.** Correr el script y generar **todos** los archivos de 6.2, incluido `server/src/Factum.Backend/Templates/factum-sello.png` (es un asset generado; es la única escritura en `server/` de este implementador). Correrlo dos veces y verificar que los bytes son iguales.
- [ ] **F3.** Verificar los SVG: sin `<text>`, `<style>` ni `font-family` (`grep -L "<text" client/public/logo-*.svg`), el viewBox de la marca es `0 0 64 64` y los colores son exactamente los de 6.1.
- [ ] **F4.** Borrar `client/public/MPF_logo.png`, `mpfs.png` y `GFD_logo.png`.

**client/**

- [ ] **F5.** `client/src/app/layout.tsx`:
  - `title: "Factum"`;
  - `description: "Adquisición forense de evidencia digital en dispositivos móviles"`;
  - `icons: { icon: [{ url: "/logo-app.ico", sizes: "any" }, { url: "/icon.svg", type: "image/svg+xml" }, { url: "/icon-512.png", type: "image/png", sizes: "512x512" }], apple: [{ url: "/apple-icon.png", sizes: "180x180" }] }`;
  - confirmar que no existe `src/app/favicon.ico`.
- [ ] **F6.** `client/src/components/shell/AppNavbar.tsx` (`Brand`): en `≥ sm`, el logo horizontal (`logo-theme-dark/white.svg`, `h-6`); en `< sm`, solo la marca (`logo-mark-dark/white.svg`, `h-6 w-6`). Se **elimina** el `<span>Factum</span>`. Las imágenes siguen con `alt=""` + `aria-hidden` y el contenedor conserva `aria-label="Factum"` (o "Factum, ir al inicio" en el link).
- [ ] **F7.** `client/src/app/page.tsx` (login):
  - sacar el bloque "Institución" (L143-152) y, en su lugar, el bloque de organización condicional (T8): `<img src={logoSrc} alt={organizationName ?? "Logo de la organización"}>` y el nombre en un `<span>`. Si falla la carga (`onError`), se oculta el `<img>`. Si no hay nada, no se renderiza;
  - logo inferior: horizontal según `isDark`, sin `<span>Factum</span>`, glow según T8;
  - subtítulo L233: "Ingresá con tus credenciales.".
- [ ] **F8.** Crear `client/src/hooks/usePublicConfig.ts`:
  - una promesa cacheada a nivel de módulo (un solo fetch por carga de la app);
  - devuelve `{ organizationName: string | null; organizationLogoSrc: string | null }`, donde `organizationLogoSrc = api.brandingLogoURL(organization_logo_url)` o `null`;
  - si hay error, todo en `null` y sin toast.
- [ ] **F9.** `client/src/lib/api.ts`:
  - `PublicConfig`, `getPublicConfig()` y `brandingLogoURL()` (sección 7), con re-export del tipo en `client/src/types/index.ts`;
  - comentarios de L151 ("token de soporte (vía Faro)") y L174 ("misma identidad (DNI), sin loguearse de nuevo ahí");
  - **no** tocar `getMode`.
- [ ] **F10.** `client/src/components/UserMenu.tsx`: reemplazar el ítem estático MPF por uno de organización, **solo si** hay nombre o logo (logo `h-4` + nombre, mismas clases de estilo). Si no hay nada, el menú queda con identidad, separador y "Cerrar sesión". Usar `usePublicConfig()`. Actualizar el comentario de `client/src/lib/prime/pt/menu.ts` L7 si dice "institución".
- [ ] **F11.** `client/src/components/shell/SiteFooter.tsx`:
  - prop opcional `organization?: { name: string | null; logoSrc: string | null }`;
  - la columna "Institución" pasa a llamarse **"Organización"** y solo se renderiza si hay nombre o logo;
  - copyright `© {year} Factum`;
  - actualizar el JSDoc.

  `client/src/components/design-system/DesignSystemShowcase.tsx` le pasa `organization` desde `usePublicConfig()` y cambia `DEMO_USER.sigla` de `"GFD"` a `"DEMO"`.
- [ ] **F12.** `client/src/components/shell/SystemStatusLine.tsx`: sacar `· MPF Salta – GIF` y actualizar el JSDoc ("versión + badge Dev").
- [ ] **F13.** `client/src/components/dashboard/SoporteModal.tsx`:
  - L170 → "El equipo de soporte lo va a atender pronto. Podés seguir el estado en "Mis reportes".";
  - el comentario de L113 → "con la misma identidad (DNI)";
  - el resto de Faro **no cambia** (T14).

  `client/src/components/FaroIcon.tsx` L1: "el sistema de tokens de soporte".
- [ ] **F14.** `client/src/components/usb-guide/AndroidGuide.tsx`: comentario L17 y texto L117 → "Guía rápida — Depuración por USB".
- [ ] **F15.** `client/src/components/CaseFormStep.tsx` L274: placeholder `"Ej: EXP-001-2025"`.

**agent-ui/**

- [ ] **F16.** Crear `agent-ui/src/renderer/src/components/SelloMark.tsx`: un SVG inline de la marca (6.1) con props `size` y `className`. Los colores salen de variables nuevas en `agent-ui/src/renderer/src/styles/globals.css`: `--sello-bg: #2f6f12; --sello-fg: #ffffff` en `:root`, y `#7fd34e` / `#0b0c0e` en `.dark`.
- [ ] **F17.** Cambios en el proceso principal y la sidebar:
  - **`Sidebar.tsx`:**
    - en el header, `SelloMark` de 28 px en lugar del cuadrado con degradé y la "T" (con `aria-hidden`, porque el texto "Tatana / Agente Factum" queda al lado);
    - en el pie, sacar el bloque MPF y poner la línea `Factum · Tatana v{__TATANA_VERSION__}` con el mismo estilo `text-[0.58rem]` y `var(--text-muted)`.
  - **`electron.vite.config.ts`:** `renderer.define: { __TATANA_VERSION__: JSON.stringify(<version leída de package.json con readFileSync>) }`.
  - **`src/renderer/src/env.d.ts`:** `declare const __TATANA_VERSION__: string`.
  - **`src/main/index.ts` (`createTray`):** según T11. Con `app.isPackaged` (importar `app` si hace falta), la ruta a `tray/`, el archivo según la plataforma y `setTemplateImage(true)` en darwin. Sin `resize`, conservando el `try/catch` y el fallback vacío.
- [ ] **F18.** `agent-ui/package.json`:
  - `build.copyright: "© Factum"`;
  - sumar a `build.extraResources` `{ "from": "resources/tray", "to": "tray", "filter": ["**/*"] }`;
  - `mac.icon`, `win.icon` y `linux.icon` ya apuntan a los archivos que ahora existen;
  - correr `npm install` solo si cambian las dependencias (no deberían).
- [ ] **F19.** `agent-ui/src/renderer/src/components/Settings.tsx` L72: placeholder `"https://factum.example.com"`.
- [ ] **F20.** Escribir `progress/impl_frontend_marca-comercial-sin-mpf-gfd.md`: archivos tocados, constancia de las skills (o "sin hallazgos aplicables"), capturas de la verificación visual y checksums del determinismo.

### 11.3 Orquestador

- [ ] **O1.** `AGENTS.md`: reescribir L3-4 (descripción del producto sin GFD) y L14 ("cliente HTTP de Faro (mesa de ayuda)"). L72 ("Replicado desde Evidentia-GFD") **queda** como nota histórica (D10), y es la única mención permitida en `AGENTS.md`.
- [ ] **O2.** Corregir en `AGENTS.md` (sección "Contrato client ↔ server") que el backend serializa en **snake_case_lower** (`Program.cs`), no en camelCase.
- [ ] **O3.** Anotar en el backlog la tarea de seguimiento de la v4 (`B-PLANTILLA`) si DP2 = sí, y la HU sugerida `terminologia-configurable` (DP5).

---

## 12. Verificación

### Cada implementador, antes de declararse `done`

**`implementer-backend`:**

```bash
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet build server/src/Factum.Agent/Factum.Agent.csproj        # no se toca, pero tiene que seguir compilando
git check-ignore -v server/src/Factum.Backend/appsettings.Local.json server/src/Factum.Backend/branding/logo.png
```

Además:

- el arnés de B14 (tres corridas renderizadas);
- con el backend levantado, `curl -s localhost:8080/api/config/public` sin config → `{"organization_name":null,"organization_logo_url":null}`, y con un `appsettings.Local.json` de placeholders → los valores y la URL con `?v=`;
- `curl -I` al logo → `200` con `content-type` y `nosniff`;
- sin logo → `404`.

**`implementer-frontend`:**

```bash
cd client && npx tsc --noEmit
cd agent-ui && npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json
cd ops/brand && npm ci && node build-brand.mjs && node build-brand.mjs && git status --porcelain   # sin cambios en la 2.ª corrida
```

Además, la verificación visual en `npm run dev`:

- login, `/dashboard` (navbar y UserMenu), `/design-system` (SiteFooter) y una ruta 404, en modo oscuro y claro, con y sin `Branding` configurado en el backend;
- la pestaña muestra el favicon nuevo;
- la sidebar de agent-ui en oscuro y claro.

**Grep de aceptación** (lo corren los dos, cada uno sobre sus carpetas, y el reviewer sobre todo):

```bash
grep -rnIiE "\bmpf|\bgfd\b|ministerio p[uú]blico|gabinete forense|mpfsalta|evidentia" \
  client/src agent-ui/src agent-ui/package.json server/src README.md .gitlab-ci.yml \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules
grep -rnIE "\bGIF\b" client/src agent-ui/src agent-ui/package.json server/src README.md .gitlab-ci.yml \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules
ls client/public | grep -iE "mpf|gfd"     # vacío
```

Solo pueden quedar las coincidencias de T13. Se suma `evidentia` al patrón (es la marca vieja), con excepción de los comentarios de `ReportService` que hablen de la historia del layout, si los hay. El reviewer los evalúa.

### Prueba manual para el usuario

1. Abrir la web en oscuro y en claro: logo "Sello" en el login, la navbar, `/design-system` (footer) y la 404; favicon nuevo; sin MPF, GFD ni GIF en ningún lado; la línea de estado dice "Factum v2.0.0".
2. Crear `server/src/Factum.Backend/appsettings.Local.json` con los datos reales del estudio y su logo en `server/src/Factum.Backend/branding/` (los dos quedan fuera de git; confirmarlo con `git status`). Reiniciar el backend y ver el nombre y el logo en el login y en el UserMenu.
3. Generar el informe de un caso **de prueba nuevo** y revisar:
   - la banda sin MPF, GFD ni Evidentia, con el nombre del estudio (v3 interina) o la portada y el encabezado con logo y contacto (v4);
   - "Realizado con Factum" con la marca en el pie de cada página;
   - el informe de un caso viejo sigue siendo el mismo archivo, con el mismo hash.
4. Empaquetar Tatana (`npm run package:mac` o el de Windows): los íconos de la app y del instalador son el "Sello"; la bandeja en macOS es monocroma y se adapta a la barra clara u oscura; el pie de la sidebar dice "Factum · Tatana v1.0.0"; el copyright del instalador es "© Factum".
5. El login, una inspección Android y una iOS de punta a punta funcionan igual que antes.

## Resolución de decisiones pendientes (2026-10-01)

Contexto nuevo del usuario: el **emisor del informe es el estudio jurídico** (membrete con `Branding`). Las referencias que entregó son informes periciales de parte (.docx) y abren la HU nueva `informe-pericial-de-parte`.

- **DP1 → fuera de esta HU.** La `plantilla_informe_v4.docx` (`B-PLANTILLA`) y su anexo de layout pasan a `informe-pericial-de-parte`. En esta HU `B-PLANTILLA` **no se implementa**.
- **DP2 → sí.** Se implementa `B-PLANTILLA-INTERINA` (parche de la v3).
- **DP3 → recomendada:** pie en todas las páginas con el Sello a 0.4 cm + "Realizado con Factum", **sin versión**.
- **DP4 → variante CLARA** para los íconos de app (favicon, `icon-512`, `apple-icon`, íconos de Tatana): fondo `#2f6f12` + "F" `#ffffff`. (El tray monocromo de macOS no cambia: D9 B.)
- **DP5 → absorbida por `informe-pericial-de-parte`** (terminología del wizard y del informe), no por una HU `terminologia-configurable` separada.
- **O1, O2, O3:** hechos por el orquestador.
