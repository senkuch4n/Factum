# Review — marca-comercial-sin-mpf-gfd

**Veredicto:** APROBADA

## Checkpoints
- C1 (arnes): [x] no es alcance del diff; backlog.json/current.md no se tocaron por la revision. verify.sh no corrido por el reviewer (lo corre el orquestador antes de aprobar).
- C2 documentos: [x] HU y SDD existen; contrato compartido verificado: C# `PublicConfigResponse(OrganizationName, OrganizationLogoUrl)` con `SnakeCaseLower` en Program.cs:58 -> `organization_name` / `organization_logo_url`; TS en client/src/lib/api.ts:58-60 idem.
- C3 arquitectura: [x] sin tocar MpfAuthProvider, contrato `auth/mode` ni logica de Faro (solo comentarios neutralizados en SupportService). agent-ui: preload intacto; cambio en env.d.ts solo tipos. Sin documentos Mongo nuevos. Sin console.log.
- C4 verificacion: [x] corrido por mi: dotnet build Backend (0 errores; 2 warnings NuGet NU1902/NU1903 preexistentes) y Agent (0 warnings/errores); `tsc --noEmit` client OK; agent-ui `-p tsconfig.web.json` y `-p tsconfig.node.json` OK. `next build` no corrido (next dev del usuario en 3002).
- C5 sesion: [x] impl_backend e impl_frontend existen; el frontend deja constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience y web-design-guidelines (impl_frontend lineas ~205-215).

## Verificaciones especificas
- Grep de aceptacion: solo quedan las excepciones T13 (MpfAuthProvider, Program.cs, appsettings Mpf*, api.ts/DevModeBanner literal "mpf", README modo mpf). Cero GIF; `client/public` sin mpf/gfd. Cero evidentia.
- Datos del cliente: `appsettings.Local.json` y `branding/` ignorados (.gitignore:38-39, check-ignore OK); `Branding` en appsettings.json vacio (OrganizationName "", Logo "", ContactLines []); csproj excluye ambos del output. No se versionan `docs/INFORME*` (en .git/info/exclude, ninguno en el indice ni `git ls-files`).
- Plantilla v3 emparchada: sin `word/media`, sin referencias r:embed, sin MPF/GFD/Evidentia/Ministerio/Gabinete; contiene `{ORGANIZACION}` (x2). `B-PLANTILLA` v4 no se implemento (correcto, DP1). Atribucion en pie por codigo (Pass 4, ReportService.cs ~856), sin version.
- ops/brand: ejecutado dos veces en una copia del scratchpad: salida identica (determinista) y byte a byte igual a lo versionado (icon-512.png, logo-mark-dark.svg, factum-sello.png). `fonts/OFL.txt` presente (Sora, OFL 1.1). Iconos de app en variante clara (DP4).
- BrandingService: valida tamano (<=1 MB), magic bytes PNG/JPEG, dimensiones, ruta solo desde config del operador; nunca tira; endpoint publico solo expone nombre y logo (ContactLines no sale).
- Borrados B9 (plantillas/assets institucionales, ReportTemplateBuilder) coherentes con T12.

## Desvios declarados
- `[HttpHead]` en el logo: aceptable (HEAD util para sondeo, mismo ETag/cache, sin datos extra).
- `env.d.ts` de agent-ui (`interface Window` global en vez de `declare global`, mas `__TATANA_VERSION__`): aceptable; es un script global, el cambio es correcto y tsc pasa; no expone nada nuevo al renderer.
- `alt=""` del logo cuando hay nombre de organizacion (page.tsx:167, UserMenu.tsx:40): aceptable y correcto por accesibilidad (decorativo con texto adyacente); con solo logo usa alt descriptivo.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- No se genero un DOCX real de punta a punta en esta revision: queda como prueba manual del usuario (seccion "Prueba manual", punto 3), incluyendo hash del informe de un caso viejo.
- Warnings NU1902/NU1903 (SharpCompress, Snappier) son preexistentes, no de esta HU.
- `agent-ui/tsconfig.web.tsbuildinfo` aparece sin trackear; conviene no commitearlo (artefacto de build).
- El SiteFooter (server component) no tiene `onError` para el logo de la organizacion; limitacion declarada y aceptable.
