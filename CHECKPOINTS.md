# CHECKPOINTS — Evaluación del estado final de una HU

> El `reviewer` recorre estos checkpoints contra el diff real de la HU y
> marca `[x]`/`[ ]` en su veredicto (`progress/review_<id>.md`).

## C1 — El arnés está sano

- [ ] `backlog.json` es válido y tiene como mucho 1 HU en estado activo
      (`afinando`, `en_arquitectura`, `implementando`, `en_revision`).
- [ ] `./ops/harness/verify.sh` termina con exit code 0.

## C2 — La HU tiene su cadena de documentos completa

- [ ] Existe `docs/hu-<slug>.md` con las secciones estándar (Contexto,
      Gherkin, Datos, Diseño UX/UI, Fuera de alcance) y la validación del
      usuario.
- [ ] Existe `Refactorizaciones/<slug>.md` con el checklist atómico y, si
      cruza lados, la sección **Contrato compartido**.
- [ ] Los nombres de campo del diff coinciden exactamente con el Contrato
      compartido en `client/src/types/` (o `agent-ui/src/`) y en
      `server/src/**/DTOs/` / modelos, con el casing JSON real.

## C3 — El código respeta la arquitectura del repo

- [ ] Los cambios quedan dentro de los lados que la SDD declara
      (`server/` API, `server/` Tatana, `client/`, `agent-ui/`).
- [ ] `client/AGENTS.md` fue respetado si se tocó `client/` (Next.js 16).
- [ ] `agent-ui/` respeta main / preload / renderer.
- [ ] Los modelos de Mongo toleran documentos existentes sin los campos
      nuevos; no se reescriben documentos ajenos.
- [ ] No hay `console.log`/`Console.WriteLine` de debug, datos sensibles en
      logs, ni TODOs sin contexto.

## C4 — La verificación es real

- [ ] `dotnet build` limpio en los `.csproj` tocados (sin warnings nuevos).
- [ ] `npx tsc --noEmit` limpio en `client/` y/o `agent-ui/` si se tocaron.
- [ ] Si la SDD pidió tests, existen, prueban algo real y pasan.
- [ ] Si la HU genera un documento (PDF/ZIP), se verificó la salida real o
      quedó explícito como prueba manual del usuario.

## C5 — La sesión se cerró bien

- [ ] `progress/impl_backend_<id>.md` y/o `progress/impl_frontend_<id>.md`
      existen y describen qué se tocó.
- [ ] Si la HU toca `client/` o `agent-ui/`: el progress frontend deja
      constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience`
      y `web-design-guidelines` (y qué salió, o "sin hallazgos aplicables").
      Sin esa constancia es observación a corregir.
- [ ] No quedan scripts de prueba ni archivos temporales sin borrar, y no se
      tocaron datos de desarrollo ajenos.

---

**Cómo se usa:** cada checkbox se marca contra el diff real, no contra lo
que dice el implementador. Un `[ ]` que justifica un rechazo cita archivo y
línea.
