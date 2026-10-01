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

## marca-comercial-sin-mpf-gfd — APROBADA (2026-10-01)

- **Qué:** Factum sin marca MPF/GFD/Evidentia. Logo "Sello" (F maciza con esquina cortada, Sora 700) generado de forma reproducible por `ops/brand/build-brand.mjs` para `client/`, `agent-ui/` y el pie del informe; íconos de app en variante clara. `Branding` (nombre, logo, líneas de contacto) configurable y vacío por defecto, con la config real en `appsettings.Local.json` (ignorado); `GET /api/config/public` (`organization_name`, `organization_logo_url`) y `GET /api/config/branding/logo`. Plantilla v3 emparchada (sin imágenes institucionales, `{ORGANIZACION}`) + "Realizado con Factum" con el Sello en el pie. Borrado de assets/plantillas viejas (B9, autorizado por el usuario). AGENTS.md: descripción comercial y casing snake_case_lower.
- **Decisiones:** D1–D10 recomendadas, HU partida (D7 B); emisor = estudio jurídico (D4 → C); DP2 sí, DP3 pie+logo sin versión, DP4 variante clara.
- **Modelos:** afinador/architect (sesión), implementer-backend y implementer-frontend opus, reviewer sonnet. 0 rechazos. Dos acciones bloqueadas por permisos (borrado B9 y escritura del PNG en server/) resueltas con autorización explícita del usuario.
- **Rama:** `feat/marca-comercial-sin-mpf-gfd` (encadenada sobre `feat/rediseno-base-primereact`).
- **Pendiente:** plantilla v4 y terminología → `informe-pericial-de-parte`; login/modo/Faro → `auth-e-integraciones-sin-mpf`; prueba manual (informe de punta a punta, empaquetado de Tatana); no commitear `agent-ui/tsconfig.web.tsbuildinfo`.

## informe-pericial-de-parte — APROBADA (2026-10-01)

- **Qué:** el informe pasa a ser un Informe Pericial Técnico Informático. Perfil persistente del perito (`expert_profiles`, por DNI de login, copiado al caso); campos nuevos de causa (tribunal, organismo, sala, integrantes, carátula, partes, proponente, titular del dispositivo…); paso 4 "Informe" (`ReportStep`) con textos largos y borrador de operaciones; roles de captura (IMEI/modelo, nombre del dispositivo) y anexo de capturas; `plantilla_informe_v4.docx` (placeholders canónicos, frases neutralizadas, membrete del estudio, firma) generada desde `ops/plantilla/`, sin datos reales; domicilio constituido en config local (`Report:DomicilioConstituido`); D10: el DOCX sale del ZIP y el hash informado es el del ZIP entregado; hora local configurable (America/Argentina/Buenos_Aires); terminología no fiscal solo en etiquetas. v3 y `sin-foto-placeholder.png` borrados.
- **Decisiones:** D1–D16 recomendadas (validadas por el usuario; es el perito). DP1/DP2 (textos por defecto y frases fijas) aceptados en modo autónomo; pendientes de revisión del usuario.
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/informe-pericial-de-parte` (encadenada sobre `feat/marca-comercial-sin-mpf-gfd`).
- **Pendiente:** revisar la v4 en Word/WPS y los textos por defecto; cargar `Report:DomicilioConstituido`; recorrido e2e con dispositivo real; código muerto de conversión a PDF (T16); `zip-cifrado-real` desbloqueada.

## auth-e-integraciones-sin-mpf — APROBADA (2026-10-01)

- **Qué:** `MpfAuthProvider` reemplazado por `ExternalHttpAuthProvider` genérico y configurable (`Auth:External:*`, campos de respuesta configurables, URL = BaseUrl + LoginPath, solo 401/403 = credenciales inválidas); modo `"mpf"` → `"external"` con compatibilidad y warning para `Auth:Mode=mpf`/`Auth:Mpf*`; modo desconocido o config incompleta falla seguro; Faro como integración opcional "Soporte" apagada por defecto (`DisabledSupportService`, filtro `RequireSupportEnabled`, flag en config pública, dock sin el ítem); `ServiceKey` fuera de los archivos versionados; `UserMenu` sin "sigla".
- **Decisiones:** D1 A, D2 A, D3 B validadas por el usuario; D1–D12 de la HU, DP1 A y DP2 A en modo autónomo.
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/auth-e-integraciones-sin-mpf` (encadenada sobre `feat/informe-pericial-de-parte`).
- **Pendiente:** **rotar la `ServiceKey` de Faro** (sigue en el historial de git; acción del usuario); prueba manual 11.4 (login contra proveedor real/falso, soporte encendido/apagado).
