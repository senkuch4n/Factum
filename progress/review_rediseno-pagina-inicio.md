# Review — rediseno-pagina-inicio

**Veredicto:** APROBADA

Verificado por el reviewer: `npx tsc --noEmit` en `client/` (exit 0); `./ops/harness/verify.sh` (Arnés OK);
grep de consumidores de `ThemeToggle`/`webcam-pixel-grid`/`WebcamPixelGrid` en `client/src` (sin resultados fuera del propio archivo);
grep de "xbox" en `client/src` (sin resultados; solo aparece en docs como referencia visual);
grep de legacy en `app/page.tsx`, `components/login`, `components/form`, `pt/password.ts`, `pt/message.ts`
(solo clases `dark:` del patrón de logo en `LoginHero.tsx:23-26`, permitidas; sin framer-motion, useTheme, getUserMedia, hex ni console.log).
No corrí `npm run build` (hay dev server en 3002); el implementador lo corrió en una copia y tsc está limpio.

## Checkpoints
- C1 arnés sano: [x] verify.sh exit 0.
- C2 cadena de documentos: [x] HU y SDD existen; contrato sin cambios (solo `ApiError.serverMessage`, cliente).
- C3 arquitectura: [x] solo `client/` (más progress); sin backend ni agent-ui; sin console.log; `page.tsx` server component, `"use client"` en hojas.
- C4 verificación real: [x] tsc limpio; no se pidieron tests.
- C5 sesión cerrada: [x] progress existe y deja constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience, web-design-guidelines.

## Verificaciones puntuales
- Login: `LoginForm.tsx:93-120` valida (DNI 7-8 dígitos, usuario, contraseña) antes de llamar a `api.login`; `sanitizeDni` (`login-errors.ts:30`) limpia el pegado; foco al primer inválido; `inFlight` evita doble envío; redirección `router.push("/dashboard")` (D8, sin redirigir si hay sesión).
- Errores (D5): `describeLoginError` (`login-errors.ts:44-62`) por status 401/4xx/5xx/red, sin "Failed to fetch" ni "HTTP 500"; no usa `getMode()` ni literales de modo (grep sin `"mpf"`).
- `lib/api.ts`: `ApiError` existente se extiende de forma compatible (4.º parámetro opcional `serverMessage`); `status` y `missing` intactos, así que `dashboard/page.tsx` y `ExpertProfileDialog` no se rompen; `message` se arma como antes.
- HU anteriores: `usePublicConfig` usado tal cual (organizationName/organizationLogoSrc), logo Sello con el patrón de AppNavbar, `onError` en logo de organización; `getMode`/`PublicConfig` no tocados.
- Accesibilidad: labels con `htmlFor`, `aria-invalid`/`aria-describedby`/`aria-required`, toggle `<button>` con nombre cambiante y mismo nodo (foco conservado), objetivos de 44 px, `translate="no"`, `motion-safe:` en entrada y fundido, spinner del chip con `motion-safe:animate-spin`, hero estático (D9). Contrastes anotados ≥ 4,5:1.
- F14: diferido por decisión del orquestador; los dos archivos no tienen consumidores y el build no los necesita. No bloquea.
- `docs/INFORME*`: no están en el índice de git (no abiertos).

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- F14 pendiente: ejecutar `git rm client/src/components/ThemeToggle.tsx client/src/components/ui/webcam-pixel-grid.tsx` cuando se autorice (código muerto, ThemeToggle puede quedar con dependencias a framer-motion).
- Archivos de scratchpad del implementador (copia de build, capturas) quedan fuera del repo; son descartables.
- `ui-styling` no se invocó (la SDD decía "cuando corresponda"; no hay shadcn). Aceptable, con justificación en el progress.
- Se usa `rounded-full` en lugar de `rounded-fx-pill` (la clase no existe); y `!pl-10`/`!pr-12` por el orden de merge del pt de inputtext: desvío justificado respecto de la SDD. Conviene que una HU de cierre corrija el orden de merge del pt.
- Mobile: la `SystemStatusLine` fija puede tapar un campo mientras se scrollea (componente global, fuera de alcance).
- Agregó una barra de acento decorativa de 48x4 px en el hero (`LoginHero.tsx:~71`), estática y `aria-hidden`; no está en la SDD pero es inocua.
- `agent-ui/tsconfig.*.tsbuildinfo` aparecen modificados/sin trackear en el working tree: no deben commitearse con esta HU.
