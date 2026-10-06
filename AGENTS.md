# Factum — instrucciones para agentes de IA

Producto comercial de adquisición forense de evidencia digital en
dispositivos móviles. La identidad del cliente que lo usa (nombre, logo,
contacto) es configuración, no código. Cualquier agente (Claude Code u otro)
que trabaje en este repo debería leer esto antes de tocar código. El detalle
funcional y de arquitectura está en `README.md`, y los casos de uso del
agente de escritorio en `AGENTE_TATANA.md`.

## Stack

| Parte | Carpeta | Tecnología |
|---|---|---|
| Frontend web | `client/` | Next.js 16 (App Router) + React 19 + Tailwind + Framer Motion. **Leer `client/AGENTS.md` antes de tocar código ahí**: tiene cambios de API respecto al Next.js "de memoria" de cualquier modelo. |
| Backend API | `server/src/Factum.Backend` | ASP.NET Core (.NET 10) + MongoDB.Driver. JWT, casos/expedientes, informes (DOCX + ZIP cifrado AES-256), integración opcional de soporte (Faro). |
| Agente local "Tatana" | `server/src/Factum.Agent` | ASP.NET Core (.NET 10). Corre en la PC del oficial, habla con el celular por USB (ADB / pymobiledevice3) y expone API + WebSocket en `localhost:8765`. No está en `docker-compose.yml`. |
| UI del agente | `agent-ui/` | Electron + electron-vite + React 19. |
| Base de datos | — | MongoDB 7 (Docker en desarrollo). |

## Comandos habituales

```bash
# server/ (desde la raíz del repo)
dotnet build server/src/Factum.Backend/Factum.Backend.csproj
dotnet build server/src/Factum.Agent/Factum.Agent.csproj

# client/
npm run dev
npx tsc --noEmit

# agent-ui/
npm run dev
npx tsc --noEmit -p tsconfig.web.json && npx tsc --noEmit -p tsconfig.node.json
```

Todavía no hay proyectos de tests en .NET ni tests en `client/`/`agent-ui/`.
Si una HU agrega lógica que vale la pena testear, la SDD dice dónde y cómo.

## Datos de la base de desarrollo — regla dura para implementadores

La MongoDB de desarrollo puede tener **datos cargados a mano por el usuario**
como caso de prueba, y la carpeta `Storage:DataDirectory` guarda evidencia
generada (ZIP/PDF). No son descartables.

- Ningún implementador borra ni modifica documentos de negocio preexistentes.
- Una prueba que escribe limpia **solo por los `_id` que ella misma insertó**,
  nunca con un filtro amplio (`deleteMany({ caso: X })` se lleva todo).
- Crear índices o colecciones cuando la HU lo pide está bien; tocar
  documentos ajenos, no.
- El orquestador pone esta restricción en el prompt de cada implementador
  que vaya a tocar la base.

## Contrato client ↔ server

No hay un paquete de tipos compartido: `client/` declara sus tipos en
TypeScript (`client/src/types/`) y el backend sus DTOs en C#
(`server/src/Factum.Backend/DTOs/`). El mismo problema existe entre
`client/` y el agente Tatana (API local + WebSocket). Reglas:

- Si un cambio de backend agrega/renombra/saca un campo que el frontend
  manda o lee, se avisa el nombre exacto del campo (con su casing JSON real:
  el backend serializa en **snake_case_lower** — `JsonNamingPolicy.SnakeCaseLower` en `Program.cs`, no el camelCase por defecto de ASP.NET) y
  en qué archivo del otro lado hay que tocarlo.
- Si un cambio de frontend necesita un campo nuevo del backend, se pide como
  tarea de backend primero.
- La SDD de cada HU que cruce los dos lados tiene una sección **Contrato
  compartido** con los nombres exactos.

## Arnés de orquestación RDD/SDD (GitHub Project + progress/)

Las HU pasan por un arnés con estado fuera del chat, así una sesión nueva
(de cualquier integrante del equipo) puede retomar exactamente donde quedó
otra. Replicado desde Evidentia-GFD el 2026-10-01; desde el 2026-10-06 el
estado vive en un GitHub Project y varias personas trabajan HU en paralelo.

**Kanban:** Project "Factum – HU" — https://github.com/users/senkuch4n/projects/3
(privado, vinculado a `senkuch4n/Factum`). Cada HU es un issue con label `hu`.

| Columna (Status) | Fase del arnés (campo `Fase`) |
|---|---|
| Backlog | `no_afinada` |
| Afinando | `afinando` |
| Por validar | `afinada_pendiente_validacion` |
| Lista para dev | `validada`, `en_arquitectura`, `arquitectura_lista` |
| En curso | `implementando`, `rechazada_reintentando` |
| En revisión | `en_revision` |
| Hecho | `aprobada` |
| Bloqueada | `bloqueada` |

Otros campos del Project: `Slug` (kebab-case, define nombres de archivos y
rama), `Toca` (backend / client / agent-ui y combinaciones), `Reintentos`
(rechazos del reviewer; tope 2). La columna **nunca se mueve a mano ni
directo por MCP**: se cambia la Fase con `ops/harness/hu.mjs`, que actualiza
las dos cosas juntas, cuenta reintentos y bloquea al tercer rechazo.

**Una HU activa por persona** (no una global): fases activas = `afinando`,
`en_arquitectura`, `implementando`, `rechazada_reintentando`, `en_revision`.
`hu.mjs fase` se niega a activar una HU si alguno de sus asignados ya tiene
otra activa, o si no tiene asignado.

```bash
node ops/harness/hu.mjs ver [N]                       # HU abiertas, o detalle de #N
node ops/harness/hu.mjs alta <slug> "<título>" <toca>  # issue nuevo en Backlog
node ops/harness/hu.mjs tomar N                       # asignarse #N
node ops/harness/hu.mjs fase N <fase>                 # cambiar Fase + columna
node ops/harness/hu.mjs comentar N "<texto>"          # comentario en el issue
```

**MCP de GitHub** (`.mcp.json`, servidor remoto oficial con toolsets
`context,repos,issues,pull_requests,projects`): para leer issues/PRs, abrir
PRs y comentar. Cada integrante necesita `gh` logueado con scope `project`
(`gh auth refresh -h github.com -s project,read:project`, en una terminal
interactiva) y `export GITHUB_PAT="$(gh auth token)"` en su shell.

| Archivo / carpeta | Qué contiene | Quién lo escribe |
|---|---|---|
| Project "Factum – HU" | Estado de cada HU, asignado, reintentos; resumen de cierre como comentario del issue | **Solo el orquestador**, vía `hu.mjs` |
| `progress/sesiones/<login-github>.md` | Bitácora viva de las sesiones de esa persona (una línea por evento) | Orquestador de esa persona |
| `backlog.json`, `progress/current.md`, `progress/history.md` | **Archivados** (solo lectura): estado y bitácora hasta el 2026-10-06 | — |
| `progress/impl_backend_<id>.md`, `progress/impl_frontend_<id>.md` | Lo que hizo cada implementador: archivos tocados, verificación, bloqueos | Implementador correspondiente |
| `progress/review_<id>.md` | Veredicto del reviewer | Subagente `reviewer` |
| `docs/hu-<slug>.md` | RDD — Historia de Usuario (Contexto, Gherkin, Datos, UX, Fuera de alcance, Dudas) | Subagente `afinador`, validada por el usuario |
| `Refactorizaciones/<slug>.md` | SDD — spec técnica: modelo de datos, endpoints, checklist atómico y **Contrato compartido** | Subagente `architect` |
| `skills/CATALOGO.md` | Catálogo de skills seleccionables por HU | — |
| `CHECKPOINTS.md` | Checklist objetiva que usa el reviewer | — |
| `.claude/agents/{afinador,architect,implementer-backend,implementer-frontend,reviewer}.md` | Subagentes de Claude Code | — |
| `ops/harness/verify.sh` | Verificación (archivos base + build/tsc en los lados con cambios); la corre el hook `Stop` | — |
| `ops/harness/hu.mjs` | Único camino para cambiar la Fase/columna de una HU en el Project | Lo corre el orquestador |
| `.github/ISSUE_TEMPLATE/hu.yml` | Plantilla para dar de alta una HU desde la web (cae en Backlog) | — |

**Regla anti-teléfono-descompuesto:** los subagentes escriben su resultado
completo en el archivo que les corresponde y devuelven una sola línea de
referencia (`done -> progress/...`), nunca el contenido completo en el chat.

**Reparto (decisión del usuario 2026-10-01):**

- **Backend** (`server/`: API y agente Tatana) = subagente `implementer-backend` de Claude.
- **Frontend** (`client/` **y** `agent-ui/`) = subagente `implementer-frontend` de Claude.
  La SDD dice explícitamente qué app toca.
- **Revisión** = subagente `reviewer` de Claude.

**Modelos:** los dos implementadores corren **siempre con Opus 5.5**
(`model: "opus"` en el tool `Agent`), sin preguntar por HU. El `reviewer`
corre con Sonnet (`model: "sonnet"`). El orquestador corre con el modelo de
la sesión.

**Si el reviewer rechaza,** se relanza el implementador correspondiente con
el feedback — máximo 2 veces; al tercer rechazo la HU queda `bloqueada` y se
avisa al humano.

**Skills de UX/UI para el frontend — obligatorias en TODA HU:**
`implementer-frontend` invoca **siempre** `ui-ux-pro-max` antes de escribir
el JSX final, `senior-frontend`, `3d-web-experience` (como criterio, sin
agregar 3D salvo que la HU lo pida) y `web-design-guidelines` como
autochequeo antes de terminar; suma `ui-styling` y
`mblode-agent-skills-ui-animation` cuando corresponda. En una HU sin cambios
visuales igual los invoca y deja constancia en el progress ("sin hallazgos
aplicables"). El `reviewer` lo verifica.

**Antes de armar una HU, chequear si hace falta:** un typo, un formato o una
config se resuelven directo (ver "Cuándo NO aplica" en `CLAUDE.md`). Armarles
HU + SDD + review son 4 o 5 subagentes para un cambio de tres líneas.

## Ramas y flujo

- `main` = producción, `develop` = integración. **Nunca commitear directo a
  `main`** ni mergear una rama de HU a `main`: las ramas de HU van contra
  `develop`, y a `main` solo se promueve `develop`.
- **Ramas de HU en paralelo, desde `develop`** (desde 2026-10-06): cada HU
  sale en `feat/<slug>` desde `develop` actualizado y cierra con un PR contra
  `develop` cuyo cuerpo dice `Closes #N`. Como `develop` no es la rama por
  defecto, el issue se cierra solo recién cuando `develop` se promueve a
  `main`; mientras tanto la tarjeta queda en **Hecho**.
- El estado ya no vive en archivos versionados, así que no hay nada que
  reconciliar entre ramas. Los únicos archivos del arnés que se escriben son
  por HU (`docs/hu-<slug>.md`, `Refactorizaciones/<slug>.md`,
  `progress/impl_*_<slug>.md`, `progress/review_<slug>.md`) o por persona
  (`progress/sesiones/<login>.md`).
- Antes de abrir el PR, rebasar sobre `develop`; si dos HU tocan los mismos
  archivos de código, el segundo PR resuelve el conflicto.
- Un commit no mezcla trabajo de features no relacionadas: `git add` solo de
  los archivos de la tarea actual.
- Cada agente se identifica en el trailer del commit
  (`Co-Authored-By: <agente> <email>`).
