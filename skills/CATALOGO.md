# Catálogo de skills del arnés

> Skills de texto que el orquestador puede pasarle a un subagente
> (`architect` o implementadores) para una HU puntual, además de su protocolo
> normal. Se agregan acá a medida que aparezcan patrones repetidos en Factum
> (p. ej. "cómo probar Tatana en modo Mock", "cambios en el ZIP cifrado").

| Skill | Archivo | Cuándo usarlo |
|---|---|---|
| _(ninguno todavía)_ | — | — |

Los skills de UX/UI del frontend (`ui-ux-pro-max`, `senior-frontend`,
`3d-web-experience`, `web-design-guidelines`, `ui-styling`,
`mblode-agent-skills-ui-animation`) no van acá: son obligatorios o
contextuales en toda HU de frontend y los invoca el propio
`implementer-frontend` con el tool `Skill` (ver
`.claude/agents/implementer-frontend.md`). `senior-frontend` y
`3d-web-experience` viven en `.claude/skills/`; el resto son skills globales
del usuario.
