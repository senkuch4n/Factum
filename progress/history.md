# Historial de HU cerradas

Log append-only. Al cerrar cada HU (aprobada o bloqueada), el orquestador
agrega una sección `## <id> — APROBADA|BLOQUEADA (fecha)` con qué se hizo,
commits, modelos usados, intentos de revisión y lo que quedó pendiente.

## rediseno-base-primereact — APROBADA (2026-10-01)

- **Qué:** base del sistema de diseño comercial en `client/`. `primereact@10.9.9` (MIT) en modo unstyled con pass-through propio en Tailwind (`client/src/lib/prime/`); tokens `--fx-*` claro/oscuro con AA verificado (acento lima, separado del éxito menta); Inter + IBM Plex Mono vía `next/font`; arranque en oscuro (`ev-theme`); navbar `AppNavbar` (reemplaza solo el `<header>` de `/dashboard`); línea de estado + `SiteFooter` (contenido placeholder); 404 nueva; sonner/tooltip restilizados; `/design-system` solo en desarrollo. Inspiración visual xbox.com/es-AR, sin marca ni assets.
- **Decisiones:** D1–D11 recomendadas; DP1 → PrimeReact 10.9.9 MIT unstyled (la v11 exige licencia comercial).
- **Modelos:** afinador/architect (sesión), implementer-frontend opus, reviewer sonnet. Intentos de revisión: 0 rechazos.
- **Rama:** `feat/rediseno-base-primereact` (sin commitear al cierre).
- **Pendiente:** contenido del `SiteFooter` (D8); contraste de `AgentChip` en modo claro y z-index Toast vs navbar → `rediseno-dashboard`; prueba visual manual (SDD §8); `npm audit` con 14 vulnerabilidades preexistentes.
