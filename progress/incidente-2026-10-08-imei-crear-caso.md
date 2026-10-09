# Incidente 2026-10-08 — No se podía crear caso (IMEI vacío → 400)

**Entorno:** producción (nube / VPS).
**Reportado por:** un perito, vía el usuario (superadmin).
**Severidad:** bloqueante (un perito no podía crear casos).
**Estado:** resuelto y desplegado el 2026-10-08.

## Síntoma

Al cargar los datos y apretar **"Crear caso y continuar"**, el botón mostraba
**"Guardando…"** y la pantalla volvía a la misma, **sin ningún mensaje de error**.
Con la cuenta de superadmin funcionaba; con la del perito no.

## Diagnóstico

1. Se descartó regresión del rediseño recién promovido (PR #30/#31): el wizard de
   creación de caso (`handleSaveCase`, `CaseFormStep`, el banner de error) estaba
   **idéntico** a lo que ya había en producción. El merge solo agregó el saludo/reloj.
2. Backend sano; con superadmin andaba → no era una caída, sino algo de esa cuenta/datos.
3. En la pestaña **Network** del navegador del perito, el `POST /api/cases` daba **400**
   (los 400 no se loguean por defecto en el backend, por eso "no llegaba nada" al log).
4. El cuerpo del 400 indicaba **IMEI faltante**.

## Causa raíz

El equipo del perito reportaba el **IMEI vacío** (pasa en iPhones y algunos Android que
no exponen el IMEI por USB). El wizard solo ofrecía el campo de **carga manual** cuando el
dispositivo venía con el centinela `INGRESAR_MANUALMENTE`, no cuando venía vacío. Resultado:

- No aparecía el campo para escribir el IMEI.
- La validación del front no lo exigía (`validateCaseForm` solo pedía `imeiOverride` si
  `imeiManual`, y `imeiManual` era `device.imei === "INGRESAR_MANUALMENTE"`, no el vacío).
- Se mandaba el IMEI vacío → el backend (`CaseService.CreateAsync`) respondía 400, y como
  no había campo IMEI en pantalla, el error quedaba invisible y el perito veía "Guardando"
  y vuelta a la misma pantalla.

## Fix

Usar el helper ya existente `isImeiMissing()` (vacío **o** centinela) para decidir la carga
manual del IMEI, en dos puntos de `client/`:

- `client/src/components/CaseFormStep.tsx` — muestra el campo + aviso "IMEI no detectado
  automáticamente" y lo cuenta como obligatorio; el `SpecRow` muestra "—" cuando falta.
- `client/src/app/dashboard/page.tsx` — `handleSaveCase` valida y manda el IMEI escrito.

Commit: `f0c919b` — *fix(client): permitir cargar el IMEI a mano cuando el equipo lo reporta vacío*.
Solo `client/`; no toca backend ni agente. Verificado con `tsc` + `npm run build`.

## Despliegue

- PR #32 (`fix/imei-manual-cuando-vacio` → `develop`), PR #34 (`develop` → `main`).
- Push a `main` disparó el workflow "Desplegar producción": imagen `sha-15e9124` publicada
  en GHCR y desplegada al VPS con health-check y rollback. Deploy OK.
- Verificado: el perito pudo crear el caso (cargando el IMEI a mano) tras un Ctrl+F5.

## Pendiente / a considerar

- **Tatana (opcional):** evaluar que el agente devuelva el centinela `INGRESAR_MANUALMENTE`
  cuando no puede leer el IMEI, en vez de un valor vacío, para que el contrato sea explícito.
- **Backend (opcional):** los 400 de validación no se loguean; si se quisiera verlos en el
  log del backend para diagnósticos futuros, habría que subir el nivel de ese caso.
