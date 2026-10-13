# Review — formulario-inspeccion-ux (#52)

**Veredicto:** APROBADA

## Checkpoints
- C1: [x] rama feat/formulario-inspeccion-ux; Fase/verify.sh no re-corridos (cambios sin commitear, el orquestador los maneja)
- C2: [x] HU y SDD existen; sin contrato nuevo (solo lectura de Tatana), `connection` es tipo solo de cliente
- C3: [x] solo client/ (7 archivos de la SDD); sin console.log; sin tocar datos
- C4: [x] `npx tsc --noEmit` en client/ limpio (corrido por mí); la SDD no pide tests
- C5: [x] progress frontend completo, con constancia de ui-ux-pro-max, senior-frontend, 3d-web-experience, web-design-guidelines y animación

## Criterios de aceptación (contra el diff)
- Indicador USB real: `useDeviceConnection` (3 estados, ref mientras `loadingDev`) + `CONNECTION_UI`, región `role="status" aria-atomic` siempre montada, aviso `FxBanner role="note"`. OK.
- `<form noValidate>` con submit por Enter: botón principal `type="submit"`, Atrás/Cancelar `type="button"`, guard `submitInFlight`, `CatalogManageDialog` fuera del form; Enter con overlay de CatalogAutoComplete y Calendar abierto no envía. OK.
- Error de captura con reintento: `shotError`, `stale` ("Captura anterior" atenuada), botón "Reintentar", `disabledReason` con `aria-describedby`. Se eliminó el catch silencioso. OK.
- < 1024 px: fila compacta `lg:hidden` + "Ver equipo" (`aria-expanded`/`aria-controls`), IMEI fuera del plegable. OK.
- Sugerencias: banner warn con "Reintentar" (`reload` con `seq`, error no se limpia al reintentar), foco devuelto tras éxito. OK.
- URL: `grep localhost:8765` solo `lib/agent.ts` y DesignSystemShowcase. OK.
- Tokens: solo clases `fx-*`, sin colores sueltos. OK.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- No verifiqué en navegador conexión real / Enter / layout; queda para la prueba manual del usuario de la SDD.
- No hay ESLint configurado en client/, no se pudo correr lint.
- `useDeviceConnection` escribe un ref durante el render (lo pedía la SDD); es idempotente, sin problema práctico.
