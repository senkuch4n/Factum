# HU: Marca por cuenta (cada informe sale con la marca de su dueño)

**Issue:** #13
**Slug:** `marca-por-cliente`
**Apps afectadas:** `server/src/Factum.Backend` (API e informe DOCX) y `client/` (web).
**No toca** `agent-ui/` ni `server/src/Factum.Agent` (Tatana).
**Depende de:** #6 `usuarios-locales` y #12 `abm-clientes`, las dos mergeadas en `develop`.
**Origen:** plan SaaS. Estas decisiones ya están tomadas y no se discuten acá:

- Cada cliente es una cuenta de usuario.
- Solo los superadmins (el dueño y Leo) administran cuentas.
- Después viene el despliegue en la nube, con el backend en un plan gratuito y posiblemente
  con disco efímero.

**Como** cliente de Factum (estudio, perito o fuerza con cuenta propia)
**quiero** que mis informes salgan con mi nombre, mi logo, mi isotipo, mis datos de contacto
y mis colores
**para que** cada informe que entrego lleve la identidad de quien lo emite, aunque varias
organizaciones compartan la misma instalación de Factum.

---

## Contexto

### Qué existe hoy (arqueología del 2026-10-06, rama `feat/marca-por-cliente` = `develop` en `4e0ce40`)

La HU `marca-comercial-sin-mpf-gfd` sacó la marca del MPF/GFD y creó la sección `Branding`
de la configuración. Así, la identidad del emisor quedó como **configuración de la
instalación**: una sola marca para todos los usuarios. Con el modelo SaaS de #6/#12, todas
las cuentas comparten hoy esa marca.

#### Backend (`server/src/Factum.Backend/`)

| # | Dónde | Qué hay |
|---|---|---|
| B1 | `appsettings.json` → `Branding` | `OrganizationName`, `OrganizationLogo` (ruta a archivo), `OrganizationIsotype` (ruta), `ContactLines` (lista), `PrimaryColor` y `AccentColor` (`#RRGGBB`). Todo vacío en el repo; los valores reales van en `appsettings.Local.json` o en variables `Branding__*`. |
| B2 | `Services/Branding/BrandingOptions.cs` | POCO de esa sección. |
| B3 | `Services/Branding/BrandingService.cs` | **Singleton** que lee `Branding` **una sola vez** al arrancar. Para cambiar algo hay que reiniciar el backend. Normaliza y valida. Nombre: trim, ≤ 150 caracteres (se trunca). Contacto: ≤ 6 líneas de ≤ 150 caracteres (se trunca). Logo e isotipo: archivo de disco, ≤ 1 MiB, 16-4096 px por lado, PNG o JPEG detectado por contenido (`ImageProbe`), con `Version` = 12 caracteres del SHA-256. Primario: contraste ≥ 4.5:1 con blanco. Acento: contraste ≥ 4.5:1 con la tinta. **Nunca tira**: si un dato es inválido, loguea un warning y usa el default o nada. Expone `IBrandingService.Current` (`BrandingSnapshot`). |
| B4 | `Services/Branding/BrandingColors.cs` | Defaults (el verde de Factum `2F6F12` y el tinte `E8F3DF`), `Normalize` y cálculo de contraste. |
| B5 | `Services/Reports/ReportService.cs` L155 y L215 | Los dos flujos de generación (el viejo `GenerateAsync` y el agent `GenerateReportAsync`) pasan `branding.Current` a `GenerateDocxAsync`. **El informe no sabe de quién es la marca**: usa siempre la de la instalación. |
| B6 | `ReportService.cs` L333-405 | Usos en el DOCX: bloques condicionales `MEMBRETE`, `MEMBRETE_CON_LOGO`, `MEMBRETE_SIN_LOGO` e `ISOTIPO`; `BrandColors.Apply` (reemplaza los colores centinela de la plantilla por los de la marca); `{LOGO_ORGANIZACION[:WxH]}` y `{ISOTIPO_ORGANIZACION[:WxH]}`; `{CONTACTO}` multilínea; el acento en la fila del ZIP de la tabla de hashes. |
| B7 | `Services/Reports/ReportValues.cs` L117-119 | `{ORGANIZACION}` y `{CONTACTO_EN_LINEA}` (líneas unidas con " · "). |
| B8 | `ReportService.cs` L1275 `LoadSello` | La **atribución de Factum** al pie ("sello" `Templates/factum-sello.png`) es independiente de `Branding` y sale siempre. |
| B9 | `Controllers/ConfigController.cs` | `GET /api/config/public` (**anónimo**): `organization_name`, `organization_logo_url` (`/api/config/branding/logo?v=<version>`), `support_enabled` y `encrypt_zip`. `GET/HEAD /api/config/branding/logo` sirve los bytes desde memoria, con `ETag` y `Cache-Control: public, max-age=3600`. **No expone** `ContactLines`, el isotipo ni los colores. |
| B10 | `Services/Cases/CaseService.cs` L119, L544 y `AgentGeneration.cs` L303 | La generación ya conoce al dueño: `cas.Officer.Dni` (solo el dueño accede a su caso). Un caso `Completed` no se regenera (409). Así, los informes ya generados quedan como están: el DOCX está en disco con la marca que tenía. |
| B11 | `Models/UserAccount.cs` (`users`) | Desde #12 tiene `Organization` (≤ 120, "estudio, fuerza o razón social", para identificar al cliente en el listado). La HU #12 aclaró: **"No es la marca (#13)"**. `SessionValidator` lee la cuenta de `users` **en cada request**. |
| B12 | `Models/ExpertProfile.cs` (`expert_profiles`, `_id` = DNI) | Perfil de la **persona** del perito: `Nombre`, `Matricula`, `Profesion`, `Caracter` y `Tratamiento`. Lo edita cada usuario en "Mi perfil de perito" y se **copia al caso** (`PeritoSnapshot`) al crearlo o editarlo. Alimenta `{nombrePerito}`, `{matriculaPerito}`, etc. No tiene datos de la organización. |
| B13 | `Models/UserAdminEvent.cs` (`user_admin_events`) + `UserAdminService` | Auditoría de solo inserción: `ActorDni/Name`, `TargetUserId/Dni/Name`, `Action` (`create`, `update`, `suspend`, `reactivate`, `reset_password`, `unlock`) y `Changes` (`{ field, from, to }` con el nombre del campo en JSON). Se muestra como historial en el detalle de la cuenta del panel. |
| B14 | `Services/Reports/ReportSettings.cs` → `Report:DomicilioConstituido` y `Report:DefaultTexts:*` | Otros datos **del emisor** que también son globales: `{fraseDomicilio}` (", con domicilio constituido en …") y los textos por defecto de las secciones. Ver D12. |
| B15 | Datos de usuario por DNI | `cases.Officer.Dni`, `expert_profiles._id` y `catalog_*` usan el DNI como llave del dueño, en los tres modos de auth (`dev`, `external` y `local`). `users` y el panel solo existen en `local`. |

#### Frontend (`client/`)

| # | Dónde | Qué hay |
|---|---|---|
| C1 | `src/hooks/usePublicConfig.ts` | Caché de módulo: **un solo fetch por carga de la app** de `/api/config/public`. Da `organizationName` y `organizationLogoSrc`. Como es anónimo, no sabe quién está logueado. |
| C2 | `src/components/login/LoginHero.tsx` | El login (antes de autenticarse) muestra el logo y el nombre de la organización de la instalación. |
| C3 | `src/components/UserMenu.tsx` L31-56 | Muestra la organización (logo + nombre) como primer ítem del menú del usuario. |
| C4 | `src/components/shell/SiteFooter.tsx` | Columna de la organización (hoy solo montado en `/design-system`). |
| C5 | `src/components/admin/*` (`/admin/cuentas`) | Panel de #12: `AccountsTable`, `AccountDetailDialog` (con historial) y `AccountFormDialog` (con el campo "Organización"). |
| C6 | `ExpertProfileDialog` (desde `AppNavbar`/`UserMenu`) | Patrón de diálogo "Mi perfil de perito", que el cliente edita sobre sí mismo. |
| — | Tema web | Los colores de la UI son tokens fijos de Factum (`--fx-*`). Hoy `Branding` **no** cambia colores de la interfaz. |

### Qué es lo nuevo

1. **Marca por cuenta**, guardada en la base de datos (no en archivos de configuración ni en
   el disco del servidor): nombre, logo, isotipo, líneas de contacto, color primario y
   color de acento.
2. **Edición desde la web**, sin reiniciar el backend y sin tocar `appsettings` (quién
   edita, según D1).
3. **El informe usa la marca del dueño del caso** (`Officer.Dni`) en el momento de
   generarlo (D8), con un fallback definido para las cuentas sin marca (D4).
4. **Validación con error visible.** Hoy un dato inválido se ignora en silencio, con un
   warning en el log. Ahora el que carga la marca ve el error (D11).
5. La `Branding` de `appsettings` deja de ser **la** marca y pasa a ser el default de la
   instalación (D4/D6).

---

## Criterios de aceptación

```gherkin
Feature: Marca por cuenta en los informes

  Background:
    Given el backend con Auth:Mode = "local"
    And los superadmins "Dueño" (DNI 20111111) y "Leo" (DNI 20222222)
    And las cuentas cliente "Estudio Pérez" (DNI 30111222) y "Perito Gómez" (DNI 30333444)

  # ── Edición de la marca (según D1) ────────────────────────────────
  Scenario: El cliente carga su marca
    Given "Estudio Pérez" logueado
    When abre "Marca del informe" desde el UserMenu
    And carga el nombre "Estudio Pérez & Asoc.", un logo PNG, un isotipo PNG, 3 líneas de contacto y los colores primario "#1F3A93" y acento "#E6ECFA"
    And guarda
    Then ve "Marca guardada" y la vista previa con los datos nuevos
    And la marca queda asociada a su cuenta (según D7), sin reiniciar el backend

  Scenario: Un superadmin edita la marca de un cliente desde el panel
    Given "Dueño" logueado en /admin/cuentas
    When abre el detalle de "Perito Gómez" y elige "Editar marca"
    Then edita los mismos campos con las mismas reglas que el cliente
    And al guardar, la marca de "Perito Gómez" queda actualizada

  Scenario: Quitar el logo o el isotipo
    Given "Estudio Pérez" tiene logo cargado
    When elige "Quitar logo" y guarda
    Then su marca queda sin logo, y los informes nuevos usan el membrete sin logo (MEMBRETE_SIN_LOGO)

  # ── Validación (según D3 y D11) ───────────────────────────────────
  Scenario: Imagen inválida
    When el usuario sube un archivo que no es PNG ni JPEG por contenido, que pesa más de 1 MiB, o con un lado fuera de 16-4096 px
    Then la imagen se rechaza con un mensaje que dice cuál regla no cumple
    And la marca guardada no cambia
    And el backend aplica la misma validación aunque la UI la saltee

  Scenario: Color sin contraste suficiente
    When el usuario elige un primario con contraste < 4.5:1 con blanco, o un acento con contraste < 4.5:1 con la tinta
    Then el campo muestra el error con el contraste calculado ("2.1:1, mínimo 4.5:1")
    And no se puede guardar hasta corregirlo o elegir "Usar colores de Factum"

  Scenario: Textos fuera de límite
    When el nombre supera 150 caracteres, hay más de 6 líneas de contacto o una línea supera 150 caracteres
    Then el formulario lo marca y no envía
    And el backend responde 400 con el campo exacto, sin truncar en silencio

  # ── Informe ───────────────────────────────────────────────────────
  Scenario: El informe sale con la marca del dueño del caso
    Given "Estudio Pérez" y "Perito Gómez" tienen marcas distintas
    When cada uno genera el informe de un caso propio, por el flujo viejo o por el flujo agent
    Then el DOCX de cada uno lleva su nombre, logo, isotipo, contacto y colores
    And ninguno lleva datos de la marca del otro ni de Branding de appsettings (salvo el fallback de D4)
    And la atribución de Factum al pie sigue saliendo como hoy (según D13)

  Scenario: Cambiar la marca no altera informes ya generados
    Given "Estudio Pérez" tiene un caso Completed con su informe generado con la marca anterior
    When cambia su logo
    Then ese DOCX y su hash no cambian (no se regenera nada)
    And solo los informes que se generen desde ese momento llevan la marca nueva (según D8)

  Scenario: Cuenta sin marca cargada
    Given "Perito Gómez" nunca cargó su marca
    When genera un informe
    Then el informe usa el fallback de D4
    And la generación no falla por falta de marca

  Scenario: Cambio de marca durante una generación
    Given "Estudio Pérez" tiene una generación en curso
    When guarda una marca nueva al mismo tiempo
    Then el informe sale entero con una sola de las dos versiones, nunca mezcladas

  # ── Interfaz web (según D5 y D6) ──────────────────────────────────
  Scenario: El cliente ve su organización en la web
    Given "Estudio Pérez" con marca cargada, logueado
    Then el UserMenu muestra su logo y su nombre (no los de la instalación ni los de otra cuenta)
    And si cambia la marca, el UserMenu se actualiza sin recargar la página

  Scenario: Login sin sesión
    Given nadie logueado
    Then la pantalla de login muestra lo que defina D6, nunca la marca de una cuenta en particular

  Scenario: Aislamiento entre cuentas
    Given "Perito Gómez" logueado
    Then no puede leer ni modificar la marca ni el logo de "Estudio Pérez" por ninguna URL de la API (403 o 404)
    And los endpoints de logo/isotipo por cuenta no son anónimos o no permiten enumerar cuentas (según la SDD)

  # ── Auditoría (según D9) ──────────────────────────────────────────
  Scenario: El cambio de marca queda auditado
    When cualquier usuario guarda un cambio de marca (propia o, si es superadmin, ajena)
    Then queda un registro con quién, cuándo, sobre qué cuenta y qué campos cambiaron
    And las imágenes se registran por su versión (hash corto), nunca con los bytes
    And si no cambió nada, no se guarda ni se audita
    And el historial del detalle de la cuenta en /admin/cuentas lo muestra

  # ── Modos de auth (según D7) ──────────────────────────────────────
  Scenario: Modos dev y external
    Given Auth:Mode = "dev" o "external"
    Then el comportamiento es el que defina D7, y nada falla por no existir la colección users

  # ── Regresión ─────────────────────────────────────────────────────
  Scenario: Nada más cambia
    Then los textos, capturas, tabla de hashes, ZIP cifrado y nombres de archivo del informe son los de hoy
    And el login, el panel de #12 y el perfil de perito funcionan como hoy
    And ningún documento existente de cases, expert_profiles, catalog_entries, catalog_seeds ni agent_events se escribe
    And "dotnet build" del Backend, "dotnet test" y "npx tsc --noEmit" en client/ terminan sin errores
```

---

## Datos que se registran

### Marca de una cuenta (colección nueva, según D2 y D7; nombres tentativos)

| Dato | Obligatorio | Uso |
|---|---|---|
| `_id` | Sí | Llave del dueño (DNI, según D7). Un documento por cuenta. |
| `OrganizationName` | No (≤ 150) | `{ORGANIZACION}` y el UserMenu. |
| `ContactLines` | No (≤ 6 líneas de ≤ 150) | `{CONTACTO}` y `{CONTACTO_EN_LINEA}`. |
| `Logo` | No | Bytes (PNG/JPEG, ≤ 1 MiB, 16-4096 px), `ContentType`, `Width`, `Height` y `Version` (SHA-256 corto). Membrete del informe y UserMenu. |
| `Isotype` | No | Igual que `Logo`. Va al cierre del informe (`ISOTIPO`). |
| `PrimaryColor` | No | `RRGGBB`. Vacío = verde de Factum. Contraste ≥ 4.5:1 con blanco. |
| `AccentColor` | No | `RRGGBB`. Vacío = tinte de Factum. Contraste ≥ 4.5:1 con la tinta. |
| `UpdatedAt` / `UpdatedBy` | Sí | Cuándo y quién (DNI) la cambió por última vez. También sirve para el control de concurrencia, como en D11 de #12. |
| `CreatedAt` | Sí | Primera vez que se guardó. |

No se escribe en `users`, `expert_profiles` ni `cases`. En `users`, la SDD puede agregar
como mucho un puntero o la versión, si lo justifica.

### Auditoría (según D9)

Un registro en `user_admin_events` con `Action = "update_branding"` (nombre tentativo) y
`Changes` con `organization_name`, `contact_lines`, `primary_color`, `accent_color`,
`logo` e `isotype`. En las imágenes, `from`/`to` son la versión (hash corto) o vacío,
nunca los bytes. Si el cliente edita su propia marca, `ActorDni` = `TargetDni`.

---

## Diseño UX/UI (`client/`, web)

`agent-ui/` no cambia.

### Entrada

- **Cliente:** ítem nuevo **"Marca del informe"** (ícono `Palette` o `Stamp` de lucide) en
  el `UserMenu`, después de "Mi perfil de perito". Abre un diálogo amplio (en mobile, a
  pantalla completa), con el mismo patrón que `ExpertProfileDialog`. Solo aparece si D1
  habilita al cliente.
- **Superadmin:** en el detalle de la cuenta de `/admin/cuentas`, la acción **"Editar
  marca"**, que abre el mismo componente apuntando a esa cuenta. El encabezado dice "Marca
  de Estudio Pérez", para que no se confunda con la propia.

### Formulario

Dos columnas en escritorio (formulario a la izquierda, vista previa a la derecha) y una en
mobile (con la vista previa debajo):

- **Nombre de la organización** (texto, contador `n/150`).
- **Logo** y **Isotipo**: zona de arrastrar y soltar o botón "Elegir archivo" (`accept`
  PNG/JPEG). Miniatura sobre fondo blanco (el del informe), con el tamaño y las
  dimensiones, y los botones "Reemplazar" y "Quitar". Ayuda debajo: "PNG o JPEG, hasta
  1 MB, entre 16 y 4096 px por lado. Para el isotipo conviene PNG con fondo
  transparente." La imagen se valida en el navegador antes de subirla (tipo, peso y
  dimensiones), y el backend la vuelve a validar.
- **Datos de contacto**: hasta 6 líneas. Lista con "Agregar línea" y "Quitar" por línea,
  y un placeholder de ejemplo ("Av. Siempre Viva 123, Salta · Tel. 387 …").
- **Colores**: primario y acento, cada uno con un selector de color y un campo hex
  `#RRGGBB`. Al lado, el contraste calculado en vivo, con un badge que tiene texto e ícono
  (no solo color): "Contraste 7.2:1 ✓" o "2.1:1, mínimo 4.5:1". Botón "Usar colores de
  Factum".
- **Vista previa** (D11): una mini-hoja A4 estilizada con el membrete (logo o nombre según
  haya logo), un filete y un número de sección en el primario, una fila de tabla con el
  fondo de acento y el isotipo al cierre. Se actualiza en vivo. Aclaración: "Vista
  aproximada. El informe final respeta la plantilla."

### Estados y feedback

- **Cargando:** un skeleton del formulario.
- **Sin marca cargada:** el aviso "Todavía no cargaste tu marca. Tus informes salen con
  {fallback de D4}." y los campos vacíos. Si `users.Organization` tiene valor, se ofrece
  como sugerencia para el nombre (D10).
- **Guardar:** el botón primario "Guardar marca" ("Guardando…"). Al terminar, el toast
  "Marca guardada. Se aplica a los próximos informes." y el UserMenu actualizado sin
  recargar.
- **Cancelar con cambios sin guardar:** pide confirmación.
- **Errores:**
  - de validación del backend (400): en el campo correspondiente, con `aria-invalid` y
    foco al primero;
  - conflicto de concurrencia (409): "La marca se modificó desde otra sesión. Recargá para
    ver los cambios.";
  - red: un `Message` con "Reintentar".
- El aviso de que **los informes ya generados no cambian** está visible en el diálogo
  (texto secundario debajo del título).

### Accesibilidad

- Las zonas de subida se pueden operar con teclado, con `aria-describedby` hacia la
  ayuda de formato.
- Los cambios de contraste se anuncian con `aria-live="polite"`.
- El diálogo atrapa el foco y lo devuelve al disparador.

---

## Fuera de alcance

- Cambiar los colores, el logo o la tipografía de la **interfaz web** según la marca del
  cliente (white-label de la app), salvo lo que valide D5.
- Cambiar la plantilla DOCX, su estructura o sus textos, o una plantilla por cliente.
- Regenerar o modificar informes ya generados (`Completed` no se regenera: B10).
- Quitar u ocultar la atribución de Factum del pie del informe (D13).
- Logo en SVG o en otros formatos además de PNG/JPEG (D3).
- Recortar o editar la imagen dentro de la web: se sube tal cual.
- Pasar `Report:DomicilioConstituido` y `Report:DefaultTexts` a la cuenta (D12).
- Organizaciones con varios usuarios que compartan una marca.
- Migrar automáticamente la `Branding` de `appsettings` a alguna cuenta.
- Cambios en `agent-ui/` o en Tatana.

---

## Notas de implementación (mínimas; el detalle va en la SDD)

- `IBrandingService.Current` (singleton) pasa a ser "default de la instalación". El
  informe necesita resolver la marca **por DNI del dueño del caso** en cada generación,
  en los dos flujos (B5). La SDD define el servicio (por ejemplo,
  `ResolveForAsync(officerDni)`) y si hay caché, con invalidación al guardar.
- Las reglas de validación de `BrandingService` (límites, `ImageProbe`, contraste) se
  reutilizan. Al guardar desde la web, **rechazan** (400) en lugar de truncar o ignorar.
  Al leer `appsettings`, siguen tolerantes como hoy.
- Las imágenes no van en el documento de `users`, porque `SessionValidator` lo lee en cada
  request (B11) y cargaría hasta 2 MiB por request. Ver D2.
- El logo por cuenta para la web necesita un endpoint autenticado o con URL no enumerable,
  con `ETag`/versión como el de `ConfigController`. **No** debe ser público por DNI.
- La subida es `multipart/form-data` con un tope de tamaño en el endpoint (no el límite
  global de subidas grandes de evidencia).
- Regla de datos: solo se escribe en la colección de marca nueva (por `_id`) y en
  `user_admin_events` (inserción). Las pruebas usan DNIs inexistentes (`99000001`…) y
  limpian solo los `_id` que insertaron. Las de informe generan en una carpeta temporal,
  nunca en `Storage:DataDirectory` de casos reales.
- Frontend: skills obligatorias del arnés (`ui-ux-pro-max`, `senior-frontend`,
  `3d-web-experience`, `web-design-guidelines`). App: solo `client/`. Leer
  `client/AGENTS.md` (Next.js 16).
- La SDD lleva la sección **Contrato compartido** con los campos en snake_case
  (`organization_name`, `contact_lines`, `primary_color`, `accent_color`, `logo_url`,
  `isotype_url`, `updated_at`…).

---

## Dudas para validar con el usuario

> Todas tienen una opción **Recomendada**.

### D1. ¿Quién edita la marca?
- **A) Solo los superadmins**, desde el panel (`/admin/cuentas` → "Editar marca").
- **B) Solo el cliente**, desde su `UserMenu` ("Marca del informe").
- **C) Los dos:** el cliente edita la suya y los superadmins pueden editar la de cualquier
  cuenta (por ejemplo, para cargarla en el alta o para ayudar al cliente). Los dos caminos
  usan el mismo formulario y se auditan.
- **Recomendada: C.** El que tiene los archivos del logo y sabe sus datos de contacto es
  el cliente, y en un SaaS no conviene que cada cambio de teléfono pase por el dueño o por
  Leo. Pero en el onboarding (y con clientes poco técnicos) los superadmins necesitan
  poder cargarla ellos. Es el mismo componente con dos entradas, así que agrega poco
  esfuerzo sobre A o B.

### D2. ¿Dónde se guardan el logo y el isotipo?
- **A) En MongoDB, como binario (`BinData`) dentro de un documento por cuenta**, en una
  colección nueva (por ejemplo, `account_brandings`). Cada imagen ≤ 1 MiB, así que el
  documento queda lejos del límite de 16 MB.
- **B) En MongoDB con GridFS.**
- **C) En disco** (`Storage:DataDirectory/branding/<dni>/…`), con la ruta en la base.
- **D) Embebidas en el documento de `users`.**
- **Recomendada: A.** Con disco efímero en la nube, C pierde los logos en cada
  redeploy. GridFS (B) está pensado para archivos de más de 16 MB y suma dos colecciones
  y streaming sin ninguna ventaja para imágenes de 1 MiB. D cargaría hasta 2 MiB en
  cada request autenticado, porque `SessionValidator` lee `users` siempre. A deja todo en
  un solo backup (el de Mongo) y se lee una vez por generación.

### D3. ¿Qué formatos y tamaños se aceptan?
- **A) Las mismas reglas de hoy:** PNG o JPEG detectado por contenido, ≤ 1 MiB y entre 16
  y 4096 px por lado, para el logo y el isotipo.
- **B) A, más SVG** (convertido a PNG en el servidor).
- **C) A, pero con un tope menor** (por ejemplo, 512 KB o 2000 px).
- **Recomendada: A.** Son las reglas que ya usa el informe (`ImageProbe`, D de
  `marca-comercial`), probadas con la plantilla v6. El DOCX no embebe SVG de forma
  confiable, y aceptar SVG suma riesgo (scripts y entidades externas) y una dependencia de
  rasterizado. 1 MiB por imagen alcanza para un logo nítido en A4.

### D4. ¿Qué pasa con una cuenta que no cargó su marca?
- **A) Se usa la `Branding` de `appsettings` como default de la instalación**, entera. Si
  la cuenta guardó su marca alguna vez, se usa **solo** la suya: un campo vacío queda
  vacío, sin completarlo con el de `appsettings`. Los colores vacíos son los de Factum.
- **B) Informe sin membrete** (sin nombre, logo ni contacto) y paleta de Factum, ignorando
  `appsettings`.
- **C) Marca de Factum** (logo "Sello" y nombre "Factum") en el membrete.
- **D) Como A, pero campo por campo:** lo que la cuenta no tenga se completa con
  `appsettings`.
- **Recomendada: A.** No rompe las instalaciones de un solo cliente que hoy ya usan
  `appsettings`. En la nube alcanza con dejar `Branding` vacío para obtener B. D puede
  mezclar el logo de la instalación con el nombre del cliente en un mismo membrete, y eso
  en un informe pericial es peor que no tener membrete. C presenta a Factum como emisor,
  y Factum es la herramienta, no quien firma.

### D5. ¿La marca cambia algo de la interfaz web del cliente?
- **A) Solo lo que ya muestra la organización:** el logo y el nombre en el `UserMenu` (y
  en el `SiteFooter` donde se monte) pasan a ser los de la cuenta logueada. Los colores y
  el logo de Factum en la barra no cambian.
- **B) A, más los colores de la marca aplicados a la UI** (acentos y botones primarios).
- **C) Nada:** la marca solo afecta al informe.
- **Recomendada: A.** Hoy el `UserMenu` ya muestra la organización. Si mostrara la de la
  instalación en una cuenta con marca propia, confundiría. B obliga a validar contraste
  en modo claro y oscuro con colores arbitrarios del cliente, sobre un sistema de diseño
  que se rehízo hace poco: si interesa, conviene una HU de white-label aparte.

### D6. ¿Qué muestra el login (y `/api/config/public`) sin sesión?
- **A) La `Branding` de `appsettings`** (default de la instalación), como hoy. En la nube
  se deja vacía y el login muestra solo Factum.
- **B) Nunca muestra organización:** se saca del login.
- **Recomendada: A.** Antes del login no se sabe de qué cuenta se trata, así que ninguna
  marca de cliente puede salir ahí (sería filtrar quiénes son los clientes). A no rompe
  las instalaciones dedicadas a un solo cliente y no requiere cambios en `LoginHero`.
  `/api/config/public` sigue anónimo, y la marca de la cuenta se pide con un endpoint
  autenticado aparte.

### D7. ¿Cuál es la llave de la marca y qué pasa en `dev`/`external`?
- **A) La llave es el DNI** (como `expert_profiles`, `catalog_*` y `cases.Officer.Dni`).
  El cliente edita la suya en cualquier modo de auth. El panel de superadmin (D1-C) solo
  existe en `local`, como el resto del panel.
- **B) La llave es `users._id`.** La marca por cuenta solo existe en `local`. En `dev` y
  `external` se usa siempre `appsettings`.
- **Recomendada: A.** El informe se genera a partir del caso, que tiene
  `Officer.Dni`, no el `_id` de la cuenta. Con el DNI, resolver la marca es una lectura
  directa y funciona igual en los tres modos, como el perfil de perito. El DNI no es
  editable (D3 de #6), así que es una llave estable.

### D8. ¿En qué momento se toma la marca para el informe?
- **A) Al generar el informe:** se usa la marca vigente del dueño del caso
  (`Officer.Dni`) en ese instante. Los informes ya generados no cambian.
- **B) Se congela en el caso al crearlo** (como `PeritoSnapshot`), y el informe usa esa
  copia aunque la marca cambie después.
- **Recomendada: A.** La marca es de quien **emite** el informe, y la emisión ocurre al
  generar. Con B, un cliente que actualiza el logo vería salir casos abiertos de la semana
  anterior con el logo viejo, sin poder corregirlo. Además, B necesita guardar hasta
  2 MiB de imágenes por caso. Lo ya generado queda protegido por el 409 sobre `Completed`
  (B10), y la auditoría (D9) permite reconstruir qué marca había en cada fecha.

### D9. ¿Se audita el cambio de marca? ¿Dónde?
- **A) Sí, en `user_admin_events`**, con la acción nueva `update_branding`. `Changes`
  lleva los campos que cambiaron y, en las imágenes, la versión (hash corto). Se audita
  tanto si la edita un superadmin como si la edita el propio cliente. Aparece en el
  historial del detalle de la cuenta.
- **B) Sí, en una colección nueva** solo para la marca.
- **C) No se audita**, solo `UpdatedAt`/`UpdatedBy` en el documento.
- **Recomendada: A.** Reutiliza la auditoría de #12, que ya tiene el formato
  `{ field, from, to }` y la UI de historial. Una colección aparte (B) dispersaría la
  historia de la cuenta. Saber qué logo estaba vigente en una fecha puede importar si un
  informe se discute.

### D10. ¿Qué relación tiene con `expert_profiles` y con `users.Organization`?
- **A) Son tres cosas separadas y no se sincronizan.** `expert_profiles` es la
  **persona** que firma (nombre, matrícula, carácter). La marca es la **organización** que
  emite. `users.Organization` es una etiqueta interna del panel. Al cargar la marca por
  primera vez, si `users.Organization` tiene valor, se ofrece como sugerencia para el
  nombre (sin escribir nada en `users`).
- **B) La marca va dentro de "Mi perfil de perito"**, como una segunda pestaña del mismo
  diálogo.
- **C) Al guardar la marca, se copia el nombre a `users.Organization`** (o al revés).
- **Recomendada: A.** Un perito independiente puede tener una marca personal y un estudio
  puede tener varios peritos: mezclarlos complica los dos modelos. Escribir en
  `expert_profiles` además choca con la regla de datos. C acopla un dato interno de los
  superadmins (que el cliente no ve) con uno público del informe. La sugerencia ahorra el
  tipeo sin acoplar nada.

### D11. Validación de colores y vista previa
- **A) Validación estricta al guardar** (400 con el contraste calculado, sin el fallback
  silencioso de hoy) y una **vista previa aproximada en la web** (HTML/CSS), sin generar
  un DOCX.
- **B) Igual que hoy:** se acepta cualquier cosa y, al generar, se cae al default con un
  warning en el log.
- **C) A, más un botón "Descargar informe de muestra"** que genera un DOCX de ejemplo con
  la marca.
- **Recomendada: A.** Con B, el cliente se entera recién al ver un informe con colores
  distintos a los que eligió, y sin saber por qué (el warning queda en un log que no ve).
  C es lo más fiel, pero implica un caso ficticio y una generación DOCX fuera del flujo de
  casos: se puede sumar en otra HU si la vista aproximada no alcanza.

### D12. `Report:DomicilioConstituido` y `Report:DefaultTexts`, ¿siguen globales?
- **A) Siguen globales en esta HU**, y se anota una HU aparte para pasarlos a la cuenta o
  al perfil de perito.
- **B) Se suman a esta HU:** el domicilio constituido entra en la marca y los textos por
  defecto también pasan a ser por cuenta.
- **Recomendada: A.** Son datos del emisor y en un SaaS también deberían ser por cuenta
  (hoy todos los clientes saldrían con el mismo domicilio constituido). Pero el domicilio
  es más del perito que de la marca, y los textos por defecto tienen su propia validación
  (`ReportSettings`) y su UI en el editor de textos. Meterlos acá duplica el alcance.
  **Ojo:** hasta resolverlo, en la nube conviene dejar `DomicilioConstituido` vacío.

### D13. ¿La atribución de Factum en el pie del informe se mantiene?
- **A) Sí, siempre**, como hoy (B8). No es parte de la marca del cliente y no se puede
  quitar.
- **B) Se puede ocultar por cuenta** (white-label total), configurable solo por
  superadmins.
- **Recomendada: A.** Es la firma del producto y no compite con la marca del emisor. Si
  más adelante se vende un plan sin atribución, B es un flag por cuenta que se agrega sin
  cambiar el modelo.

## Validación del usuario (2026-10-06)

- **D1–D13:** se aceptan todas las opciones recomendadas (respuesta textual: "dale banco todas las recomendaciones"). En
  particular, D1 = C: la marca la editan el cliente y los superadmins.
