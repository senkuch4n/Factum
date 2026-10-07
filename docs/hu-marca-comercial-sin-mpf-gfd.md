# HU: Factum como producto comercial, sin marca ni dependencias del MPF y del GFD, con el logo nuevo

**Slug:** `marca-comercial-sin-mpf-gfd`
**Apps afectadas:** `client/` (web), `agent-ui/` (Electron), `server/src/Factum.Backend`
(API) y documentación (`README.md`). `server/src/Factum.Agent` (Tatana) **no tiene
referencias institucionales** (ver inventario).
**Pedido del usuario (2026-10-01):** "el MPF y el GFD son instituciones que me
gustaría dejar afuera del proyecto comercial Factum", y adoptar un logo nuevo.

**Como** dueño del producto comercial Factum
**quiero** que la aplicación web, el agente Tatana, los informes y la
documentación dejen de mostrar la marca del Ministerio Público Fiscal de Salta
(MPF) y del Gabinete Forense Digital (GFD), dejen de depender de sus sistemas
para funcionar y usen el logo nuevo de Factum
**para que** Factum se pueda ofrecer como producto propio a cualquier cliente,
sin usar marcas ajenas ni necesitar servicios de esas instituciones.

---

## Contexto

### De dónde viene

Factum nació de Evidentia, el sistema del MPF de Salta y su GFD. El código
heredó logos, textos, un proveedor de login contra la API del MPF y una
integración con Faro. La HU `rediseno-base-primereact` (ya implementada en la
rama `feat/rediseno-base-primereact`) creó el shell nuevo (`AppNavbar`,
`UserMenu` sobre PrimeReact, `SiteFooter`, `SystemStatusLine`, 404,
`/design-system`) y **conservó a propósito** la atribución MPF-GFD, porque el
contenido institucional quedó pendiente (D8 de esa HU).

### Inventario real (arqueología del 2026-10-01)

Búsqueda en `client/src`, `agent-ui/src`, `server/src` (sin `bin/`/`obj/`),
`README.md`, `AGENTS.md`, `AGENTE_TATANA.md`, `appsettings*.json`,
`agent-ui/package.json` y `.gitlab-ci.yml`.

#### Grupo 1: marca y texto visibles

| # | Dónde | Qué hay hoy |
|---|---|---|
| M1 | `client/public/MPF_logo.png`, `mpfs.png`, `GFD_logo.png` | Logos institucionales. Los usan M3, M4 y M5. |
| M2 | `client/public/logo-theme-dark.svg`, `logo-theme-white.svg`, `logo-app.ico` | Logo y favicon **actuales de Factum**. Se reemplazan por el logo nuevo, "Sello". Los consumen `AppNavbar`, `SiteFooter`, `not-found.tsx`, el login (`page.tsx:163`) y `layout.tsx` (`icons: "/logo-app.ico"`). |
| M3 | `client/src/app/page.tsx` (login) | L149-151: `mpfs.png` + `GFD_logo.png` arriba a la izquierda, en el bloque "Institución". L233: "Ingresá con tus credenciales del Ministerio Público Fiscal." |
| M4 | `client/src/components/UserMenu.tsx` | L39-50: ítem estático con `mpfs.png` (`alt="MPF"`) y "Min. Público Fiscal · Salta". |
| M5 | `client/src/components/shell/SiteFooter.tsx` | L39-48: columna "Institución" con `MPF_logo.png` y `GFD_logo.png` y el texto "Ministerio Público Fiscal de Salta · Gabinete Forense Digital". L66: "© {year} MPF Salta – GFD". Hoy solo se monta en `/design-system`. |
| M6 | `client/src/components/shell/SystemStatusLine.tsx` | L29: "· MPF Salta – GIF" (línea de estado global, en todas las páginas). |
| M7 | `client/src/app/layout.tsx` | L20-21: metadata `title: "Factum - Gabinete Forense Digital"`, `description: "Ministerio Público Fiscal · Inspecciones…"`. |
| M8 | `client/src/components/dashboard/SoporteModal.tsx` | L170: "El equipo del GFD lo va a atender pronto." Título "Faro - Sistema de tokens" y "Ver todo en Faro". |
| M9 | `client/src/components/usb-guide/AndroidGuide.tsx` | L17 y L117: "Guía rápida del GIF — Depuración por USB" (GIF es una sigla institucional, la misma de M6). |
| M10 | `client/src/components/CaseFormStep.tsx` | L274: placeholder `"Ej: MPF-001-2025"`. |
| M11 | `client/src/components/design-system/DesignSystemShowcase.tsx` | L18: usuario demo con `sigla: "GFD"`. |
| M12 | Comentarios en `client/src/lib/api.ts` (L151, L174) | "token de soporte del GFD", "misma identidad de MPF". No son visibles, pero describen el producto. |
| M13 | `agent-ui/src/renderer/src/components/Sidebar.tsx` | L30-35: cuadrado con degradé azul y la letra "T" como logo. L101-112: bloque "MPF" (fondo `#003366`) + "Min. Público Fiscal / Salta". |
| M14 | `agent-ui/src/renderer/src/components/Settings.tsx` | L72: placeholder `http://factum.mpfsalta.gob.ar`. |
| M15 | `agent-ui/package.json` | `build.copyright: "Ministerio Público Fiscal — Salta"`. Los íconos `resources/icon.icns`, `icon.ico` e `icon.png` **no existen en el repo** y `resources/tray-icon.png` tampoco (`main/index.ts:56`, con fallback a ícono vacío). |
| M16 | Informe PDF: `server/src/Factum.Backend/Templates/plantilla_informe_v3.docx` | `ReportService` lo usa como plantilla (L94). Es un DOCX exportado de Google Docs/Canva, con imágenes embebidas en header, footer y portada. **No se puede inspeccionar con grep:** hay que abrirlo para ver si muestra marca MPF/GFD (es muy probable). `{DEPENDENCIA}` se llena con `Officer.Sigla` (L279). |
| M17 | `server/src/Factum.Backend/Templates/` | `mpfs.png`, `GFD_logo.png`, `header-fondo.png`, `footer-fondo.png`, `body-fondo.png`, `portada-fondo.png`, `plantilla-inspeccion-tecnica.docx`, `-v2.docx`, `.pdf` y `.docx.orig-backup`. **Ningún `.cs` los referencia por nombre** (solo `plantilla_informe_v3.docx` y `sin-foto-placeholder.png`). Parecen restos de Evidentia. Además hay un `Factum-GFD.code-workspace` suelto en `Services/Reports/`. |
| M18 | `README.md` | L4: "desarrollado para el **Gabinete Forense Digital (GFD)** del **Ministerio…**". Faro aparece como "sistema de mesa de ayuda del GFD" (L11-14, L45). También se documentan el modo `mpf` (L70, L171-172, L202-211) y la sección "Integración con Faro" (L213-228). |
| M19 | `AGENTS.md` | L3-4: "…del Gabinete Forense Digital (GFD)"; L14: "cliente HTTP de Faro"; L72: "Replicado desde Evidentia-GFD" (nota histórica). |
| M20 | `.gitlab-ci.yml` L48 y `TatanaUpdatesService.cs` L14 | Comentarios con ejemplos de URL: `https://factum.mpf.gob.ar`, `https://gitlab.com/mpf/factum`. |
| — | `AGENTE_TATANA.md` | Sin referencias a MPF, GFD ni Faro. |
| — | `server/src/Factum.Agent` (incluido `IosService.cs`) | **Sin referencias reales.** El grep marca `IosService.cs` por un falso positivo: "Te**mpF**iles" en `CleanupAirplayShotTempFiles` y "saltar" contiene "salta". El agente no habla con el MPF, el GFD ni Faro. |

#### Grupo 2: dependencias funcionales

| # | Dónde | Qué hace hoy |
|---|---|---|
| F1 | `server/src/Factum.Backend/Services/Auth/MpfAuthProvider.cs` | Login real: hace `POST {MpfBaseUrl}{MpfLoginPath}` con `{ dni, user, password }` y lee `name` y `sigla` de la respuesta. Los errores dicen "API MPF…". `Mode => "mpf"`. |
| F2 | `Program.cs` L25, L68-77 + `appsettings.json` `Auth` | `Configure<MpfOptions>(GetSection("Auth"))`. Si `Auth:Mode == "mpf"` registra `MpfAuthProvider`; si no, `DevAuthProvider`. Claves `Auth:MpfBaseUrl`, `MpfLoginPath` y `MpfTimeoutSeconds`. `/health` expone `auth_mode`. |
| F3 | `DevAuthProvider.cs` | El modo por defecto (`Auth:Mode = "dev"`) **acepta cualquier usuario y contraseña** con DNI de 7 u 8 dígitos y pone `Sigla = "-"`. **Factum no tiene un almacén de usuarios propio:** la identidad (`User { Dni, Name, Sigla }`) viene entera del proveedor. |
| F4 | Contrato `GET /api/auth/mode` (`AuthController.cs` L12-13) ↔ `client/src/lib/api.ts` L146-148 (`getMode(): Promise<"dev" \| "mpf">`) | Lo consumen `SystemStatusLine.tsx` (badge "Dev") y `DevModeBanner.tsx` (tipo `"dev" \| "mpf"`). Respuesta: `{ "mode": "dev" \| "mpf" }`. |
| F5 | Faro: `Services/Support/SupportService.cs`, `ISupportService.cs`, `SupportController.cs`, `DTOs/SupportDtos.cs`, `appsettings.json` `FaroIntegration` | **Faro es la mesa de ayuda (sistema de tokens) del GFD** (README L11-14). Comparte la identidad MPF por DNI. Endpoints: `POST/GET /api/support/tokens`, `POST /api/support/tokens/{id}/calificacion` y `GET /api/support/faro-sso`. Hay una `ServiceKey` commiteada. El repo de Faro está en `gitlab.com/joelserrudo/faro-sistema-de-tokens`. |
| F6 | Faro en `client/` | `FaroIcon.tsx`, `SoporteModal.tsx` y `dashboard/page.tsx` (L17-18 importan, L317 monta el modal, L329 tiene el ítem del dock con `FaroIcon`). Métodos `api.reportarProblema`, `listarMisReportes`, `calificarReporte` y `obtenerLinkFaro`. |
| F7 | Informes: `ReportService.cs` | Depende de la plantilla M16. No llama a sistemas del MPF. La dependencia es solo de marca y plantilla. |
| F8 | Auditoría: `AgentAuditController.cs` + `AuditOptions` (`Audit:AdminDnis`) | No llama al MPF. El comentario L11 explica que no hay roles porque la identidad "viene tal cual del login de MPF/Faro". La lectura se habilita por una lista de DNIs en la config. Depende **indirectamente** de F1/F3: el DNI que audita es el que devuelve el proveedor. `AgentEvent.Mode` (`installed`/`portable`) **no tiene relación** con el modo `mpf`. |
| F9 | `DevAuthProvider.cs` L28 | Comentario "Convención de prueba compartida con Faro". |

**Nota de contrato para el `architect`:** `AGENTS.md` dice que ASP.NET
serializa en camelCase, pero `Program.cs` L31-40 fuerza `SnakeCaseLower`. El
campo `mode` no cambia con ninguno de los dos, pero cualquier campo nuevo
(por ejemplo `organization_name` o `support_enabled`) viaja en snake_case.

### Qué es lo nuevo

1. **Logo nuevo de Factum, "Sello"** (decisión del usuario, ver "Diseño UX/UI").
   Reemplaza M2 y se aplica en la web (favicon, navbar, footer, 404, login) y
   en `agent-ui/` (sidebar, íconos de app y tray).
2. **Cero marca MPF/GFD/GIF visible** en la web, el agente, los informes nuevos y
   la documentación del producto.
3. **Cero dependencia funcional obligatoria** de sistemas del MPF o del GFD: el
   login y el soporte quedan resueltos según D1-D3.

---

## Criterios de aceptación

```gherkin
Feature: Factum sin marca ni dependencias del MPF y del GFD, con el logo "Sello"

  # ── Logo nuevo ─────────────────────────────────────────────────────
  Scenario: El logo "Sello" reemplaza al logo anterior en la web
    Given la app client/ en modo oscuro
    When el usuario ve el login "/", la navbar de "/dashboard", el SiteFooter en "/design-system" y la página 404
    Then en todos aparece el logo horizontal "Sello": marca con fondo #7fd34e, glifo "F" #0b0c0e y el texto "Factum"
    And al pasar a modo claro la marca usa fondo #2f6f12 y glifo #ffffff, y el texto "Factum" usa el color de --fx-text del modo claro
    And el texto "Factum" del SVG está convertido a trazos (el SVG no contiene <text> ni depende de la fuente Sora)

  Scenario: Favicon y ícono de app
    Given el navegador abre cualquier página de client/
    Then la pestaña muestra el favicon nuevo (marca "Sello" sin texto)
    And existe un PNG de app de la marca, referenciado desde la metadata de layout.tsx

  Scenario: Identidad del agente Tatana
    Given agent-ui abierto
    Then la sidebar muestra la marca "Sello" en lugar del cuadrado con degradé y la letra "T"
    And el instalador empaquetado (mac/win/linux) y el ícono de la bandeja usan la marca "Sello"

  # ── Sin marca institucional ────────────────────────────────────────
  Scenario: Login sin institución
    Given un usuario no autenticado en "/"
    Then no ve los logos del MPF ni del GFD
    And el subtítulo del formulario no menciona al Ministerio Público Fiscal ni a ninguna institución (salvo el nombre de cliente configurado, si D4 lo aprueba)

  Scenario: UserMenu sin sello institucional
    Given un usuario autenticado que abre el UserMenu
    Then ve su nombre, "DNI <dni> · <sigla>" y "Cerrar sesión"
    And no ve el ítem "Min. Público Fiscal · Salta" ni el logo mpfs.png

  Scenario: Footer y línea de estado sin institución
    Given cualquier página
    Then la línea de estado muestra "Factum v<APP_VERSION>" (más el badge "Dev" si corresponde) sin "MPF Salta – GIF"
    And el SiteFooter no tiene columna "Institución" con MPF/GFD y su línea de copyright no dice "MPF Salta – GFD"

  Scenario: Metadata y textos sueltos
    Then el <title> y la description de la app no mencionan al Gabinete Forense Digital ni al Ministerio Público Fiscal
    And la guía USB de Android no dice "del GIF"
    And el placeholder del número de expediente no empieza con "MPF-"
    And el usuario demo de /design-system no tiene sigla "GFD"

  Scenario: agent-ui sin institución
    Given agent-ui abierto
    Then la sidebar no muestra el bloque "MPF / Min. Público Fiscal Salta"
    And el placeholder de "URL del servidor" no apunta a un dominio del MPF
    And el copyright del instalador no es "Ministerio Público Fiscal — Salta"

  Scenario: Verificación objetiva por búsqueda
    When se busca "(?i)\bmpf|\bgfd\b|ministerio p[uú]blico|gabinete forense|mpfsalta" y, sensible a mayúsculas, "\bGIF\b" (para no pegarle a la extensión de archivo .gif), en client/src, agent-ui/src, agent-ui/package.json, server/src (sin bin/ ni obj/), README.md, .gitlab-ci.yml y appsettings*.json
    Then no hay coincidencias, salvo las que la SDD liste en forma explícita como permitidas (por ejemplo la nota histórica de AGENTS.md, si D10 la conserva)
    And client/public/ ya no contiene MPF_logo.png, mpfs.png ni GFD_logo.png

  # ── Informes ───────────────────────────────────────────────────────
  Scenario: Informe nuevo sin marca institucional
    Given un caso nuevo con evidencia capturada
    When el oficial genera el informe
    Then el PDF no muestra logos, nombres ni siglas del MPF, el GFD o el GIF en la portada, el header, el footer ni el cuerpo
    And muestra la marca de Factum según la plantilla aprobada en D5

  Scenario: Los informes ya generados no se tocan
    Given casos e informes (PDF/ZIP) ya generados en la base y en Storage:DataDirectory
    When se despliega esta HU
    Then esos documentos y archivos quedan byte a byte iguales y sus hashes siguen validando

  # ── Login (sujeto a D1) ────────────────────────────────────────────
  Scenario: El backend no depende de la API del MPF para autenticar
    Given el backend configurado con el proveedor que defina D1
    When un usuario inicia sesión
    Then no se hace ninguna llamada a un servicio del MPF
    And no existen en el código clases, opciones de configuración ni mensajes de error con "Mpf" en el nombre

  Scenario: Contrato de modo de autenticación
    Given el endpoint GET /api/auth/mode
    Then responde uno de los modos que defina D2 (ninguno se llama "mpf")
    And client/src/lib/api.ts, SystemStatusLine y DevModeBanner tipan exactamente esos mismos valores
    And el badge "Dev" se sigue mostrando solo en modo "dev"

  # ── Soporte / Faro (sujeto a D3) ───────────────────────────────────
  Scenario: Soporte desactivado por defecto
    Given el backend con la configuración por defecto (integración de soporte desactivada)
    When el usuario entra a "/dashboard"
    Then el FloatingDock no muestra el ítem de soporte con el ícono de Faro
    And el backend no intenta contactar a Faro
    And los endpoints /api/support/* responden de forma controlada (lo que defina la SDD), sin error 500

  Scenario: Soporte activado explícitamente
    Given la integración de soporte activada por configuración
    When el usuario reporta un problema
    Then el flujo funciona igual que hoy
    And los textos dicen "el equipo de soporte" en lugar de "el equipo del GFD"

  # ── Regresión ──────────────────────────────────────────────────────
  Scenario: Nada más cambia
    Given la HU implementada
    When el usuario inicia sesión y completa una inspección de punta a punta (Android e iOS)
    Then todo funciona igual que antes, salvo los cambios de marca, login y soporte descritos
    And "dotnet build" de Backend y Agent, y "npx tsc --noEmit" en client/ y agent-ui/, terminan sin errores
```

---

## Datos que se registran

La HU **no modifica documentos existentes** (ver D6). Configuración nueva o
renombrada (los nombres son tentativos; los define la SDD según D1-D4):

| Dato | Obligatorio | Uso |
|---|---|---|
| `Auth:Mode` (valores nuevos, sin `mpf`) | Sí | Elige el proveedor de identidad (D1/D2). |
| Opciones del proveedor que reemplaza a `Auth:Mpf*` (p. ej. `Auth:External:BaseUrl`, `LoginPath`, `TimeoutSeconds`) | Solo si se usa ese proveedor | Reemplazo genérico de `MpfOptions` (D1). |
| `Integrations:Support:Enabled` (o equivalente) | No, `false` por defecto | Activa la integración opcional con la mesa de ayuda externa (Faro), D3. |
| `Branding:OrganizationName` (o equivalente) | No, vacío por defecto | Nombre del cliente que aparece en la UI y el informe si D4 lo aprueba. Si está vacío, no se muestra nada institucional. |
| `localStorage['ev-theme']` | — | Sin cambios. |

Si D2 o D4 exponen datos nuevos al frontend, la SDD tiene que incluir una
sección **Contrato compartido** con los nombres en snake_case.

---

## Diseño UX/UI

### Logo "Sello" (decisión del usuario, 2026-10-01; no es una duda)

Referencia visual: https://claude.ai/artifact/PDnFXZ7T3qcS99VoZMdVCu, artboard
**"A · Sello"**.

- **Marca** (viewBox `0 0 64 64`): `<rect width="64" height="64" rx="14">`, con
  fill de acento.
  - Modo oscuro: `#7fd34e` (= `--fx-accent` oscuro en `globals.css` L108).
  - Modo claro: `#2f6f12` (= `--fx-accent` claro, L47).
- **Glifo "F"**: `<path d="M20 14H40L46 20V23H30V29H40V37H30V50H20Z">`. Es una F maciza
  con la esquina superior derecha cortada en diagonal, como un sello.
  - Sobre el acento oscuro: `#0b0c0e`.
  - Sobre el acento claro: `#ffffff`.
- **Logo horizontal**: marca + "Factum" en Sora 700, letter-spacing `-0.02em`,
  color de `--fx-text` (`#f3f5f7` oscuro / `#0e1013` claro). **En los SVG finales
  el texto va convertido a trazos**: la app no carga Sora y no tiene que
  hacerlo.

**Archivos a generar** (los nombres existentes se conservan para no tocar a
los consumidores, salvo que la SDD justifique otra cosa):

| Archivo | App | Contenido | Dónde se ve |
|---|---|---|---|
| `client/public/logo-theme-dark.svg` | web | Logo horizontal, variante oscura | Navbar, SiteFooter, 404, login (modo oscuro) |
| `client/public/logo-theme-white.svg` | web | Logo horizontal, variante clara | Lo mismo, en modo claro |
| `client/public/logo-mark-dark.svg` / `logo-mark-white.svg` (nombre a definir) | web | Solo marca | Lugares chicos (avatar de app, login en mobile, etc.) |
| `client/public/logo-app.ico` | web | Favicon multitamaño (16/32/48) de la marca | Pestaña del navegador (`layout.tsx` `icons`) |
| PNG de app (p. ej. `client/public/icon-512.png` + `apple-icon` 180) | web | Marca | Metadata `icons`/`apple` |
| `agent-ui/resources/icon.icns`, `icon.ico`, `icon.png` | agent-ui | Marca | Instalador y app empaquetada (hoy **faltan**) |
| `agent-ui/resources/tray-icon.png` | agent-ui | Marca simplificada para la bandeja | Tray (hoy **falta**, ver D9) |
| Componente o asset de marca en `agent-ui/src/renderer` | agent-ui | Marca | Sidebar (reemplaza la "T", M13) |

### Cambios por pantalla

**`client/` (web):**
- **Login (`/`)**: se saca el bloque "Institución" (mpfs + GFD) de la esquina
  superior. Si D4 aprueba el nombre de cliente, ahí va ese texto (solo texto,
  sin logo). El logo inferior pasa a ser el "Sello". El subtítulo cambia, por
  ejemplo a "Ingresá con tus credenciales." (el texto final depende de D1).
- **Navbar (`AppNavbar`)**: el logo horizontal nuevo. Sin otros cambios.
- **UserMenu**: se elimina el ítem institucional. Quedan identidad y
  "Cerrar sesión". Si D4 aprueba el nombre de cliente, puede ocupar ese ítem
  (solo texto).
- **SiteFooter**: la columna "Institución" se elimina (o pasa a "Cliente" según
  D4). El copyright usa el titular que defina D10.
- **SystemStatusLine**: "Factum v<versión>" + badge "Dev".
- **404**: el logo nuevo, sin otros cambios.
- **SoporteModal y dock**: según D3. Si queda activo, el texto neutro es "el
  equipo de soporte".
- **Metadata**: `title: "Factum"`, con una description de producto sin
  institución.

**`agent-ui/` (Electron):**
- **Sidebar**: la marca "Sello" en el header (con "Tatana / Agente Factum"
  como hoy). Se elimina el bloque MPF del pie (ver D9 para lo que lo reemplaza).
- **Settings**: un placeholder de URL neutro.

**Informe PDF:** según D5.

**Estados de error y feedback:** los mensajes del backend que hoy dicen "API
MPF…" pasan a ser neutros, por ejemplo "No se pudo conectar al servicio de
autenticación". Si la integración de soporte está desactivada, no hay botón;
no se muestra un botón que después falla.

**Accesibilidad:**
- Los `alt` de los logos son "Factum" o `alt=""` + `aria-hidden` si hay texto
  al lado, como hoy en `AppNavbar`.
- Contraste de la marca: el glifo `#0b0c0e` sobre `#7fd34e` y `#ffffff` sobre
  `#2f6f12` superan 3:1 (gráfico no textual). Igual el implementador lo
  verifica.

---

## Fuera de alcance

- Reescribir la historia de git o borrar el nombre "Evidentia" de commits viejos.
- Cambios en el repo de Faro.
- Un sistema de usuarios propio de Factum con alta/baja/roles y pantalla de
  administración, salvo que D1 elija esa opción (en ese caso se recomienda una
  HU aparte).
- Subir o configurar el **logo del cliente** desde la UI (D4 recomienda solo
  nombre por configuración).
- Cambiar la terminología del dominio forense-judicial ("fiscal",
  "denunciante", "sigla", "expediente"): ver D8.
- Modificar casos, informes, ZIP o PDF ya generados en la base de desarrollo o
  en `Storage:DataDirectory` (D6).
- Rotar la `FaroIntegration:ServiceKey` y el `Jwt:Secret` commiteados (es
  deuda de seguridad conocida; conviene una tarea aparte).
- Rediseñar el contenido de `/` y `/dashboard`: eso es de las HU
  `rediseno-pagina-inicio` y `rediseno-dashboard`. Esta HU solo saca la marca
  institucional y pone el logo.
- Cargar la fuente Sora en la app.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- **Archivos a borrar** si la SDD confirma que no tienen uso:
  - en `client/public/`: `MPF_logo.png`, `mpfs.png`, `GFD_logo.png`;
  - en `server/.../Templates/`: `mpfs.png`, `GFD_logo.png`,
    `plantilla-inspeccion-tecnica*` y `*.orig-backup`;
  - `Services/Reports/Factum-GFD.code-workspace`.

  Revisar si el `.csproj` los copia con un glob.
- Los fondos `header-fondo.png`, `footer-fondo.png`, `body-fondo.png` y
  `portada-fondo.png` no están referenciados en el código. Puede que sean
  copias de lo que está embebido en el DOCX: hay que abrir el DOCX para
  confirmarlo (D5).
- `MpfOptions` hoy se bindea a la sección `Auth` entera. Al renombrar, cuidar
  que `Auth:Mode` siga leyéndose donde lo lee `Program.cs`.
- Si se agrega un endpoint público de configuración (D2/D3/D4), no puede exigir
  JWT, porque el login lo necesita antes de autenticarse.
- `agent-ui`: generar `.icns` e `.ico` multitamaño desde el SVG de la marca. El
  tray de macOS idealmente usa una imagen *template* monocroma (D9).
- `client/AGENTS.md`: leer la doc de Next 16 sobre la metadata de `icons` y los
  archivos `icon`/`apple-icon` en `app/` antes de tocar el favicon.
- El orquestador edita `AGENTS.md` (archivo del arnés), no un implementador.
- Recordatorios del arnés: skills de frontend obligatorias. Regla dura de
  datos: esta HU **no** escribe en la base.

---

## Dudas para validar con el usuario

### D1. Qué reemplaza al login contra el MPF (`MpfAuthProvider`)
Hoy Factum no tiene usuarios propios. Hay dos modos: el MPF valida
credenciales y devuelve nombre y sigla, o `dev` acepta cualquier cosa.
- **A) Proveedor HTTP externo genérico y configurable**: el mismo mecanismo
  que hoy (POST con `dni`/`user`/`password`, que responde `name`/`sigla`), con
  nombres neutros (`ExternalHttpAuthProvider`, `Auth:External:*`) y los campos
  de la respuesta configurables. Un cliente como el MPF se conecta solo por
  configuración, sin marca en el código.
- **B) Usuarios propios de Factum**: una colección `users` con contraseñas
  hasheadas, alta por un administrador y, eventualmente, roles. Factum queda
  autónomo, pero es una feature grande (pantalla de administración,
  recuperación de contraseña, primer usuario).
- **C) Estándar de la industria (OIDC/SAML)**: los clientes integran su propio
  IdP (Keycloak, Azure AD, etc.). Es lo más "enterprise", pero cambia el
  formulario de login (redirección) y el modelo de identidad (DNI y sigla
  pasan a ser claims).
- **D) Solo borrar `MpfAuthProvider`** y dejar `dev`. **No es viable** para un
  producto, porque `dev` acepta cualquier contraseña.
- **Recomendada: A en esta HU**, y B o C como HU aparte cuando haya un primer
  cliente que no tenga su propia API de login. A resuelve el pedido ("que no
  dependa ni lleve la marca") sin romper ningún despliegue actual y es un
  cambio acotado. B y C son decisiones de producto con mucho alcance que
  merecen su propia HU.

### D2. Qué pasa con el modo `"mpf"` del contrato `GET /api/auth/mode`
- **A) Renombrarlo** al modo del proveedor elegido en D1 (por ejemplo
  `"external"`). El contrato queda `{ mode: "dev" | "external" }` en
  `AuthController`, `api.ts`, `DevModeBanner` y `/health` (`auth_mode`).
- **B) Cambiar el contrato** a algo más rico (`{ mode, support_enabled,
  organization_name }`) en un endpoint público de configuración y absorber D3
  y D4.
- **Recomendada: A**, y si D3 o D4 necesitan exponer flags al frontend, que la
  SDD decida si los suma a ese mismo endpoint o crea `/api/config/public`. Lo
  único que el frontend usa de este campo es "¿es dev?", así que renombrar
  `mpf` no tiene impacto funcional.

### D3. Integración con Faro
Confirmado en la arqueología: **Faro es la mesa de ayuda (sistema de tokens)
del GFD** y comparte la identidad MPF por DNI. Pero su repo está en
`gitlab.com/joelserrudo/…`, así que puede ser también un producto tuyo.
- **A) Eliminarla** por completo (backend, `SoporteModal`, `FaroIcon`, ítem del
  dock y config).
- **B) Integración opcional desactivada por defecto**: se conserva el código
  con nombres neutros en la UI ("Soporte"); se activa con
  `Integrations:Support:Enabled=true` y su URL/clave. Si está apagada, el dock
  no muestra el ítem y el backend no contacta a Faro.
- **C) Dejarla como está**, activa por defecto.
- **Recomendada: B**. Es un flujo terminado y probado (crear, listar y
  calificar tokens, y SSO) y puede ser un diferencial comercial si Faro también
  es tuyo. Apagarla por defecto elimina la dependencia. **Pregunta asociada:**
  ¿Faro es tuyo y se ofrece junto con Factum, o es del GFD? Si es del GFD,
  conviene A.

### D4. Qué texto institucional va en lugar de "MPF / GFD"
- **A) Nada**: Factum solo muestra su propia marca. Se eliminan la columna
  "Institución" del footer, el ítem del UserMenu y el bloque del login.
- **B) Nombre del cliente configurable (solo texto)**: `Branding:OrganizationName`,
  vacío por defecto (y entonces no se ve nada). Si está cargado, aparece en el
  login, el UserMenu, el footer y el informe.
- **C) B + logo del cliente configurable.**
- **Recomendada: B**. Un informe forense normalmente tiene que decir qué
  organismo lo emite, y un cliente comercial va a querer ver su nombre. Vacío
  por defecto, equivale a A. El logo del cliente (C) suma carga y
  validación de archivos y el riesgo de volver a meter marcas ajenas en el
  repo. Mejor dejarlo para una HU aparte si un cliente lo pide.

### D5. Plantilla del informe PDF (`plantilla_informe_v3.docx`)
Tiene imágenes embebidas (header, footer, portada) que con mucha probabilidad
llevan la marca del MPF y el GFD. Fue armada en Google Docs/Canva.
- **A) Vos editás la plantilla** en la herramienta original (cambiando las
  imágenes institucionales por el "Sello" y, si D4 = B, un placeholder
  `{ORGANIZACION}`) y la entregás como asset. El implementador solo cambia el
  código si hay placeholders nuevos.
- **B) El implementador reemplaza las imágenes embebidas** dentro del DOCX
  (desde código o a mano) por el logo nuevo, manteniendo el layout.
- **C) Plantilla nueva desde cero.**
- **Recomendada: A**. `ReportService` tiene muchos ajustes finos de layout
  atados a esta plantilla (L103-117, L143-200, L366-455). Editar el arte en su
  herramienta original es lo que menos riesgo tiene de romper la paginación.
  B es aceptable si no tenés acceso al original. Para decidir, necesito que
  confirmes qué muestran hoy la portada, el header y el footer del PDF.

### D6. Datos existentes en la base de desarrollo y en `Storage:DataDirectory`
Pueden mencionar al MPF: expedientes tipo `MPF-…`, siglas de oficiales e
informes PDF/ZIP generados con la marca anterior.
- **A) No tocar nada**: lo existente queda como está; solo lo nuevo sale con
  la marca nueva.
- **B) Script de migración** que reescriba textos en la base y regenere los
  informes.
- **Recomendada: A**. Por la regla dura de `AGENTS.md`, son datos cargados a
  mano como casos de prueba, y además los ZIP y PDF son **evidencia con hash**:
  regenerarlos invalida su integridad. Si querés una base limpia para demos,
  conviene armar una base aparte, no modificar esta.

### D7. ¿Partir la HU en dos?
- **A) Una sola HU** con todo.
- **B) Dos HU encadenadas:**
  1. `marca-comercial-sin-mpf-gfd` (esta): logo "Sello" en web y agent-ui,
     textos y assets institucionales, plantilla PDF (D5), README, comentarios.
     Casi todo frontend, de bajo riesgo y visible rápido.
  2. `auth-e-integraciones-sin-mpf` (nueva): D1, D2 y D3 (proveedor de auth,
     contrato de modo, Faro opcional) y D4 si necesita backend. Toca contrato
     client ↔ server.
- **Recomendada: B**. Las decisiones de D1 y D3 pueden necesitar más
  conversación sin frenar el logo y la marca. Además, los riesgos son
  distintos: la marca no rompe funciones, el login sí puede. Con B, de esta
  HU salen los escenarios de "Login", "Contrato de modo" y "Soporte"
  (pasan a la segunda), y en la primera los textos de `SoporteModal` solo se
  neutralizan.

### D8. Terminología del dominio ("fiscal", "sigla", placeholder de expediente)
La UI usa "Fiscal" y "DNI Fiscal" en `CaseCard`, "Foto del fiscal" en
`CaptureStep` e `IdentityCard`, y la búsqueda dice "Buscar por fiscal o
sigla…". Es vocabulario del proceso penal, no marca del MPF, pero un cliente
no fiscal (por ejemplo un perito privado o la policía) no lo usaría.
- **A) Fuera de esta HU**: solo se cambia el placeholder `MPF-001-2025` (es
  marca). La terminología configurable va en una HU futura.
- **B) Neutralizar ya** ("Funcionario", "Operador").
- **Recomendada: A**. Cambiar el vocabulario toca el wizard, el informe y la
  búsqueda, y es una decisión de producto distinta de "sacar la marca".

### D9. `agent-ui`: pie de la sidebar e ícono de bandeja
- El bloque MPF del pie de la sidebar: **A) eliminarlo**; **B) reemplazarlo
  por "Factum v<versión>"**; **C) el nombre de cliente de D4**.
- El ícono de bandeja: **A) la marca a color**; **B) un template monocromo de la
  "F"** (recomendado por macOS para la barra de menú).
- **Recomendada: B en el pie** (la versión del agente es útil para soporte y
  hoy no se ve en la sidebar) y **B en el tray** en macOS, con la marca a
  color en Windows y Linux.

### D10. Titular del copyright y documentación del arnés
- ¿Quién figura en "© {año} …" del `SiteFooter` y en `build.copyright` de
  agent-ui? **Recomendada:** "Factum" hasta que exista una razón social. Si ya
  tenés una, decímela.
- `AGENTS.md` L72 ("Replicado desde Evidentia-GFD") es una nota histórica del
  arnés. **Recomendada:** reescribir L3-4 y L14 (descripción del producto) y
  dejar L72 como historia interna, porque no es visible para clientes. Si
  preferís cero menciones en el repo, se reescribe también.

## Validación del usuario (2026-10-01)

Se aceptan **todas las opciones recomendadas** (D1–D10), con estas respuestas:

- **D7 → B: la HU se parte en dos.** Esta HU (`marca-comercial-sin-mpf-gfd`) cubre logo "Sello" (web + agent-ui), assets y textos institucionales, plantilla PDF, README/AGENTS y comentarios; los textos de `SoporteModal` solo se neutralizan. Los escenarios de Login, Contrato de modo y Soporte/Faro (D1 A, D2 A, D3 B) pasan a la HU nueva `auth-e-integraciones-sin-mpf`.
- **D3:** Faro es del usuario (producto propio) → se confirma B (integración opcional "Soporte", apagada por defecto), en la HU 2.
- **D4 → B:** `Branding:OrganizationName`, vacío por defecto. Si necesita backend más allá de leer config, la SDD lo resuelve (puede adelantarse un endpoint público de config o quedar para la HU 2).
- **D5 → A:** el usuario edita la plantilla en su herramienta original y entrega el asset. **Pendiente de confirmar que tiene acceso al original**; si no, fallback B (el implementador reemplaza las imágenes embebidas del DOCX).
- **D6 → A**, **D8 → A**, **D9 → B (pie "Factum v<versión>") + B (tray monocromo en macOS, color en Windows/Linux)**.
- **D10:** no hay razón social → copyright "Factum". AGENTS.md L72 queda como nota histórica.

### Actualización del usuario (2026-10-01, después de la validación)

- **Primer cliente: un estudio jurídico.** Los informes tienen que salir **con la identidad del estudio** (el estudio es el emisor) y en algún lugar del informe tiene que figurar que **fue realizado con Factum**.
- **D4 pasa de B a C:** nombre **y logo** de la organización configurables (`Branding:OrganizationName` + logo), aplicados al informe PDF (portada/encabezado) y a la UI donde corresponda. Vacíos por defecto.
- **Atribución obligatoria:** el informe lleva una leyenda tipo "Realizado con Factum" (con el logo Sello o sin él, a definir en la SDD), siempre, aunque la organización esté configurada.
- **D5:** el usuario tiene un informe de referencia ("más o menos" el que quiere) y lo va a entregar como base de la plantilla nueva. Hasta que llegue, el ítem de la plantilla queda bloqueado.
- **Datos del emisor:** además de nombre y logo, el encabezado del informe lleva profesionales, domicilio y teléfonos → `Branding` suma una lista libre de líneas de contacto. Los datos reales del cliente viven solo en configuración local no versionada; nunca en el repo.
