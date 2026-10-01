# impl_frontend — marca-comercial-sin-mpf-gfd

**Implementador:** implementer-frontend (Claude Opus 5.5) · **Fecha:** 2026-10-01
**Rama:** `feat/marca-comercial-sin-mpf-gfd` (sin commit, como pidió el orquestador)
**Alcance:** sección 11.2 de la SDD (F1-F20) + "Resolución de decisiones pendientes" (DP4 = íconos de app en variante **clara**).
**Estado:** `done` (F2 completo, incluido `factum-sello.png`).

---

## Bloqueos y pendientes

1. **`factum-sello.png`: resuelto (2026-10-01).** El primer intento de escribir el script con la salida en `server/` lo rechazó el clasificador de permisos (había concurrencia con `implementer-backend`). Después el orquestador avisó que el usuario lo autorizó explícitamente y que el backend ya había terminado. Se hizo así:
   - se agregó a `ops/brand/build-brand.mjs` la línea `out("server/src/Factum.Backend/Templates/factum-sello.png", rasterize(markSvg(LIGHT), 256));` y el permiso no se volvió a rechazar;
   - `file` → `PNG image data, 256 x 256, 8-bit/color RGBA` (marca clara `#2f6f12` / `#ffffff`, `rx=14`);
   - dos corridas seguidas → los 17 assets dan el mismo SHA-256, y los 16 previos no cambian respecto de la tabla de abajo. `factum-sello.png` = `b7efc50637689d347775625e7f6098d5b4ffb6fe5ce5909d5b323d01a5fcb7aa`;
   - `dotnet build server/src/Factum.Backend/Factum.Backend.csproj` → 0 errores (4 advertencias previas, entre ellas la de NU1903 Snappier). El `.csproj` lo copia con `<Content Include="Templates\**\*" CopyToOutputDirectory="PreserveNewest" />` (L20) y el archivo aparece en `bin/Debug/net10.0/Templates/factum-sello.png`;
   - no toqué ningún otro archivo de `server/`.
2. **URL de la fuente:** la SDD dice `github.com/sora-xyz/Sora-Font`, pero ese repo no existe (404). El repo oficial es **`github.com/sora-xor/sora-font`**: el copyright embebido en el TTF dice "Copyright 2019 The Sora Project Authors (https://github.com/sora-xor/sora-font)". Usé el TTF estático de ese repo, fijado a un commit.

---

## Archivos tocados

### ops/brand/ (nuevo, F1)
- `ops/brand/package.json`: `private`, `"type": "module"`, dependencias fijadas `opentype.js@1.3.4` y `@resvg/resvg-js@2.6.2`.
- `ops/brand/package-lock.json`.
- `ops/brand/build-brand.mjs`:
  - convierte el texto a trazos con `font.getPath(..., { kerning: true, letterSpacing: -0.02 })` (opentype 1.3.4 soporta `letterSpacing`) y `toPathData(2)`;
  - calcula la línea base con `hhea` según 6.1, y el ancho como fin del último glifo sin el tracking final, redondeado hacia arriba a 0.5 (sale 289);
  - rasteriza con resvg (`loadSystemFonts: false`);
  - escribe los ICO y el ICNS a mano.
- `ops/brand/fonts/Sora-Bold.ttf` + `ops/brand/fonts/OFL.txt`: commit `7f9a9c5d0ccd1c099cfac420aa27133df1c5fdc4` de `sora-xor/sora-font`, archivo `fonts/ttf/Sora-Bold.ttf`.
  - SHA-256 del TTF: `2ee62953668346b0aff83994a7864f75ee6b8443f99894e88f47559454291155`.
  - SHA-256 de `OFL.txt`: `ba0b9729c9428ba79a0459ab8ec575791b51509dbec213e383d0316d37fec299`.
  - El TTF va sin modificar, así que no aplica el renombrado por Reserved Font Name.
- `ops/brand/README.md`: qué genera, cómo correrlo, la URL exacta y los SHA-256.
- `ops/brand/node_modules/` queda ignorado por `**/node_modules/` (verificado con `git check-ignore`).

### Assets generados (F2; DP4: los íconos de app usan la variante clara)
- `client/public/logo-theme-dark.svg` y `logo-theme-white.svg` (reemplazados; viewBox `0 0 289 64`).
- `client/public/logo-mark-dark.svg` y `logo-mark-white.svg` (nuevos).
- `client/public/icon.svg` (nuevo, marca clara).
- `client/public/logo-app.ico` (reemplazado; ICO 16/32/48).
- `client/public/icon-512.png` y `apple-icon.png` (nuevos; 180 a sangre completa, sin `rx`).
- `agent-ui/resources/icon.png` (1024), `icon.ico` (16…256) e `icon.icns` (`icp4`…`ic14`, marca a 824/1024).
- `agent-ui/resources/tray/trayTemplate.png` + `@2x` (monocromo, la "F" calada, D9 B).
- `agent-ui/resources/tray/tray.png` + `@2x` y `tray.ico` (a color, variante clara).
- `server/src/Factum.Backend/Templates/factum-sello.png` (256×256, variante clara; es la única escritura en `server/`, ver "Bloqueos").

### Borrados (F4)
- `client/public/MPF_logo.png`, `mpfs.png` y `GFD_logo.png`. Los borré con `git rm`, así que las tres bajas ya quedaron **staged** en el índice.

### client/
- `src/app/layout.tsx` (F5): `title`, `description` e `icons` explícitos, exactamente como dice la SDD. No existe `src/app/favicon.ico`. El `<head>` del build quedó así:
  ```
  <title>Factum</title>
  <link rel="icon" href="/logo-app.ico" sizes="any"/>
  <link rel="icon" href="/icon.svg" type="image/svg+xml"/>
  <link rel="icon" href="/icon-512.png" type="image/png" sizes="512x512"/>
  <link rel="apple-touch-icon" href="/apple-icon.png" sizes="180x180"/>
  ```
- `src/components/shell/AppNavbar.tsx` (F6): logo horizontal desde `sm`, solo la marca (`h-6 w-6`) en `< sm`. Saqué el `<span>Factum</span>`. Las imágenes siguen con `alt=""` + `aria-hidden` y el contenedor conserva su `aria-label`.
- `src/app/page.tsx` (F7):
  - bloque de organización condicional (logo `h-6 max-w-[160px] object-contain`, el `onError` oculta el `<img>`, y el nombre);
  - logo inferior horizontal, sin el `<span>`, con el glow `rgba(127,211,78,.22)` en oscuro;
  - subtítulo "Ingresá con tus credenciales.".
- `src/hooks/usePublicConfig.ts` (F8, nuevo): promesa cacheada a nivel de módulo. Si hay error, devuelve todo en `null` sin toast. Arranca en `null` para no romper la hidratación.
- `src/lib/api.ts` (F9): `PublicConfig`, `getPublicConfig()`, `brandingLogoURL()` y los comentarios de soporte/Faro. `getMode` no se tocó.
- `src/types/index.ts`: re-export de `PublicConfig`.
- `src/components/UserMenu.tsx` (F10): el ítem de la organización aparece solo si hay nombre o logo. Usa `usePublicConfig()`.
- `src/lib/prime/pt/menu.ts`: el comentario dice "identidad/organización".
- `src/components/shell/SiteFooter.tsx` (F11): prop `organization?`, columna "Organización" condicional, `© {year} Factum` y JSDoc actualizado.
- `src/components/design-system/DesignSystemShowcase.tsx`: le pasa `organization` desde `usePublicConfig()`, y `sigla` pasa a `"DEMO"`.
- `src/components/shell/SystemStatusLine.tsx` (F12): saqué "· MPF Salta – GIF" y actualicé el JSDoc.
- `src/components/dashboard/SoporteModal.tsx` y `src/components/FaroIcon.tsx` (F13): textos neutralizados. El resto de Faro no cambió.
- `src/components/usb-guide/AndroidGuide.tsx` (F14) y `src/components/CaseFormStep.tsx` (F15, `"Ej: EXP-001-2025"`).
- `src/app/not-found.tsx`: solo agregué `width`/`height` a los dos `<img>` del logo, para evitar CLS (hallazgo de web-design-guidelines).

### agent-ui/
- `src/renderer/src/components/SelloMark.tsx` (F16, nuevo): SVG inline con `aria-hidden`, colores tomados de `--sello-bg` y `--sello-fg`.
- `src/renderer/src/styles/globals.css`: `--sello-bg`/`--sello-fg` (claro en `:root`, oscuro en `.dark`).
- `src/renderer/src/components/Sidebar.tsx` (F17):
  - `SelloMark` de 28 px en el header;
  - el pie dice `Factum · Tatana v{__TATANA_VERSION__}`, con el mismo estilo `text-[0.58rem]` y `var(--text-muted)`.
- `electron.vite.config.ts`: `renderer.define.__TATANA_VERSION__`, leído de `package.json` con `readFileSync`.
- `src/renderer/src/env.d.ts`: `declare const __TATANA_VERSION__: string`.
  - **También arreglé un error previo:** el archivo es un script global y tenía `declare global { interface Window ... }`, que TypeScript ignora en ese contexto. Por eso `tsc -p tsconfig.web.json` daba **26 errores ya en HEAD** (`window.tatana` no existía).
  - Lo reemplacé por `interface Window { tatana: TatanaAPI }` directo, porque sin eso la verificación de la SDD no podía pasar.
- `src/main/index.ts` (`createTray`, T11):
  - la ruta sale de `app.isPackaged ? process.resourcesPath/tray : ../../resources/tray`;
  - el archivo depende de la plataforma (`trayTemplate.png` / `tray.ico` / `tray.png`);
  - en darwin, `setTemplateImage(true)`, sin `resize`;
  - conservé el `try/catch` y el fallback vacío.
- `package.json` (F18): `build.copyright: "© Factum"` y `extraResources` para `resources/tray` → `tray`. Las dependencias no cambiaron.
- `src/renderer/src/components/Settings.tsx` (F19): placeholder `https://factum.example.com`.

Fuera de `factum-sello.png`, no toqué `server/`, README, `.gitignore`, `.gitlab-ci.yml`, `backlog.json` ni `progress/current.md`. Tampoco toqué la base ni `Storage`.

---

## Contrato compartido (sección 7): coincide con la SDD

- `GET /api/config/public` → `organization_name: string | null`, `organization_logo_url: string | null` (snake_case_lower). Los tipé en `PublicConfig` (`client/src/lib/api.ts`) con esos nombres exactos.
- `api.getPublicConfig()` usa `request` (el token es opcional).
- `api.brandingLogoURL(path)` → `` `${BACKEND_URL}${path}` ``.
- El logo solo se usa como `src` de `<img>`.
- `getMode`, `User` y `Case` no cambiaron.

---

## Verificación

### TypeScript
```
client$   npx tsc --noEmit                         → exit 0
agent-ui$ npx tsc --noEmit -p tsconfig.web.json    → exit 0   (en HEAD daba 26 errores, ver env.d.ts)
agent-ui$ npx tsc --noEmit -p tsconfig.node.json   → exit 0
```
`agent-ui/node_modules` no existía, así que lo instalé con `npm ci --ignore-scripts`, que no cambia el lock. Los `.tsbuildinfo` que generó tsc los dejé como en HEAD.

### Build del client (`npm run build`)
Lo corrí en un `git worktree` del scratchpad, con los cambios copiados y `npm ci` propio, para no pisar el `.next` del `next dev` del usuario en el puerto 3002. Ese server siguió respondiendo 200. El worktree ya está borrado.
```
▲ Next.js 16.2.10 (Turbopack)
✓ Compiled successfully in 2.4s
  Finished TypeScript in 3.7s ...
✓ Generating static pages using 6 workers (5/5) in 290ms
Route (app): ○ /  ○ /_not-found  ○ /dashboard  ○ /design-system
```

Hice además un `electron-vite build` de agent-ui en una copia del scratchpad:
- renderer ok;
- `__TATANA_VERSION__` queda reemplazado por `"1.0.0"`;
- el main incluye `trayTemplate.png`.

### Determinismo (`cd ops/brand && npm ci && node build-brand.mjs && node build-brand.mjs`)
Las dos corridas dan los mismos SHA-256 (`diff` vacío):
```
c21b9c8afb667be5788a56beb9c2b2445b612493d09c938bd1fcbe27fc6766b3  client/public/logo-app.ico
260736f4e009947dc56498cbf2d58b04235f41980213c770ee8706a2c9f1c54c  client/public/logo-mark-dark.svg
80ea633a2c571352a30217a0cb3cac8f7b07a19d0dd38917c86231d79f2a8087  client/public/logo-mark-white.svg
560d8bb0f7b5670710d3c1a52914e962aca13656f37438693bef30a7c6b024a9  client/public/logo-theme-dark.svg
f767cb76933e8182c64941dfeb739fb5776b10f37d57cc7cda7e6bd0bf7cd1e0  client/public/logo-theme-white.svg
df045241f43674b6d99159bf132a8762db0804976bc1c5d3aa4191d48de8813c  client/public/icon-512.png
80ea633a2c571352a30217a0cb3cac8f7b07a19d0dd38917c86231d79f2a8087  client/public/icon.svg
32bb8de9cfea53b40dfa399388f91066d680f70c7980a41a7c49d5ef8eea9f0a  client/public/apple-icon.png
02c824158d426a4561004e2c808a12818d6d41ad0623bed0f2742c89b83d55f3  agent-ui/resources/icon.icns
fc9ad7df02dc794d206028605edf6ebbcee59e0d422e8845291d5adc71915da4  agent-ui/resources/icon.ico
46e608727626a5e64dc3ec46f1caf76601ebc98516d395f9f4ff2a5611780abe  agent-ui/resources/icon.png
c2012841c97ce7a8abb9f37c37a5151877f989f93d1a6dc3f9bbc4883d8599ef  agent-ui/resources/tray/tray.ico
c6507528cc3923adbe0b9f3a15c9c1461db3da2551e214b88cd2e62567451695  agent-ui/resources/tray/tray.png
316c9530ac98f218efe76c2b194f5779a04f1f00c0baec590e3667f868add8d4  agent-ui/resources/tray/tray@2x.png
27557b4ef79aadcbfe4f4eb37ccfee440f7ca3773966b21f4599b2bcc84fe2ce  agent-ui/resources/tray/trayTemplate.png
0431f429f37d42e7dfec0fb089d1f1da99e840ffccb764e9b49db3998bfeb548  agent-ui/resources/tray/trayTemplate@2x.png
b7efc50637689d347775625e7f6098d5b4ffb6fe5ce5909d5b323d01a5fcb7aa  server/src/Factum.Backend/Templates/factum-sello.png
```
Revisé los contenedores con `file`:
- los ICO tienen 3, 7 y 4 PNG RGBA;
- el ICNS lo reconoce como "Mac OS X icon";
- `sips` lee el ICNS a 1024.

### F3: los SVG
- `grep -lE "<text|<style|font-family|@font-face"` sobre `logo-*.svg` e `icon.svg` no devuelve nada.
- Las marcas tienen `viewBox="0 0 64 64"`.
- Los fills son exactamente los de la SDD:
  - oscuro: `#7fd34e` / `#0b0c0e` / `#f3f5f7`;
  - claro: `#2f6f12` / `#ffffff` / `#0e1013`.
- `icon.svg` usa la variante clara (DP4).
- Contraste del glifo: 10.56:1 en la variante oscura y 6.17:1 en la clara (piden ≥ 3:1).

### Grep de aceptación (sobre mis carpetas)
```
client/src/components/DevModeBanner.tsx:9   "mpf"       ← excepción T13.5
client/src/lib/api.ts:163-164               "mpf" (×2)  ← excepción T13.4
```
El grep de `\bGIF\b` no da nada, y `ls client/public | grep -iE "mpf|gfd"` sale vacío.

### Verificación visual
La hice con Chrome headless (playwright-core en el scratchpad) sobre el build (`next start`, puerto 3099) y un `next dev` del worktree (puerto 3098, porque `/design-system` da 404 en producción). Mockeé `/api/config/public`, el logo, `auth/me`, `auth/mode` y `cases` con `page.route`, usando el placeholder "Estudio Jurídico Ejemplo". Los dos servidores ya están cerrados.

- **Login, oscuro y claro, con y sin branding:**
  - el chip de organización arriba a la izquierda se lee bien sobre la cámara en los dos temas;
  - sin branding, no aparece;
  - el logo horizontal inferior está bien, sin el "Factum" duplicado;
  - el subtítulo es neutro.
- **`/dashboard`:**
  - la navbar muestra el logo horizontal;
  - en el UserMenu, el ítem de la organización (logo `h-4` + nombre) aparece solo con branding; sin branding quedan la identidad, el separador y "Cerrar sesión".
- **`/design-system` (SiteFooter):**
  - la columna "Organización" aparece solo con branding;
  - el pie dice "© 2026 Factum".
- **Navbar móvil (390 px):** solo la marca, en los dos temas.
- **404:** el logo nuevo en los dos temas.
- **Línea de estado:** "Factum v2.0.0" + "DEV", sin MPF/GIF.
- **Sidebar de agent-ui** (renderer build, `window.tatana` simulado), en oscuro y claro:
  - SelloMark verde con la "F" en el header;
  - el pie dice "Factum · Tatana v1.0.0".

Las capturas quedaron en el scratchpad de la sesión (`.../scratchpad/shots/*.png`), que no se versiona. **No lo verifiqué:** la bandeja real de macOS y los íconos del instalador empaquetado, porque necesitan correr Electron o electron-builder. Quedan en la prueba manual del usuario (paso 4 de la SDD).

---

## Skills invocadas y hallazgos

- **ui-ux-pro-max** (antes del JSX). Consultas: `image alt text decorative`, `layout shift reserve space image` (ux) e `image error fallback` (stack nextjs). Lo que apliqué:
  - **Texto alternativo:** cuando el nombre de la organización va al lado del logo, el `<img>` lleva `alt=""` para que no se lea dos veces; solo con logo y sin nombre, lleva `alt="Logo de la organización"`. **Es una desviación menor y deliberada** de F7, que pedía `alt={organizationName ?? "Logo de la organización"}`. La apliqué igual en el login, el UserMenu y el SiteFooter.
  - **Logo inferior del login:** al sacar el `<span>Factum</span>`, la imagen pasa a tener `alt="Factum"`, porque ahora es el único portador del nombre.
  - **Contraste:** el chip de organización del login tiene fondo propio (`rgba(0,0,0,.5)` en oscuro, `rgba(255,255,255,.82)` en claro), porque se pinta sobre la imagen de la cámara.
  - **`next/image`:** la recomendación de usarlo no la apliqué. El logo viene de otro origen (el backend) y necesitaría `remotePatterns`, y los SVG locales ya seguían el patrón `<img>` del repo. Para reservar el espacio sumé `width`/`height`.
- **senior-frontend:** apliqué el patrón de caché a nivel de módulo con una promesa compartida (un solo fetch), y el guard `active` contra setState después del unmount. El estado inicial es igual en el servidor y en el cliente para evitar desajustes de hidratación. `useMemo` del UserMenu con las dependencias de branding. El SiteFooter sigue sin hooks, así que su logo no tiene `onError`, porque no se puede pasar un handler si lo monta un server component.
- **3d-web-experience** (solo como criterio): no agregué 3D.
  - Se sacó el cuadrado con degradé de la "T" de agent-ui, que era pseudo-profundidad, y quedó la marca plana.
  - El chip de organización reutiliza la entrada (opacidad/y) que ya tenía el bloque al que reemplaza, sin animaciones nuevas.
  - El glow del logo del login solo cambió de color (T8).
- **web-design-guidelines** (autochequeo con las reglas actuales de vercel-labs): apliqué lo siguiente.
  - `translate="no"` en el nombre de la organización y en "Factum · Tatana v…".
  - `min-w-0` en los hijos flex que truncan, y `break-words` en el nombre del footer.
  - `width`/`height` en todos los `<img>` del logo Sello (navbar, footer, login y 404).
  - **Lo que queda:** el logo de la organización no lleva dimensiones porque son desconocidas (la altura fija `h-6`/`h-4`/`h-10` reserva el alto). El `--text-muted` del pie de la sidebar de agent-ui tiene bajo contraste en oscuro, pero es el estilo previo que la SDD pide conservar.
- **ui-styling / mblode-agent-skills-ui-animation / design-system:** no los invoqué. No hay shadcn nuevo ni transiciones nuevas, y los tokens de marca salen de la SDD.
