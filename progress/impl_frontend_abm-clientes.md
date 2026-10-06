# impl_frontend — abm-clientes (#12)

**Estado:** done
**Implementador:** implementer-frontend (Claude Opus 5.5)
**Fecha:** 2026-10-06
**Rama:** `feat/abm-clientes` (sin commit, por instrucción del orquestador)
**App:** solo `client/`. `agent-ui/` no se tocó.

## Checklist §10.2

| # | Estado | Nota |
|---|---|---|
| F1 | ok | Leí `client/AGENTS.md` y la doc de Next 16 (`01-getting-started/03-layouts-and-pages.md` para rutas anidadas, `03-api-reference/04-functions/use-router.md`). Invoqué los skills de la sección "Skills". |
| F2 | ok | `lib/api.ts`: tipos de §8.1, `ApiErrorBody.field?: ChangePasswordField \| AdminUserField`, `existing_user_id?`, y los 9 métodos `admin*` con `requestSafe` y `encodeURIComponent`. |
| F3 | ok | `types/index.ts` reexporta los 10 tipos de §9.1. |
| F4 | ok | `lib/admin-accounts.ts`, puro. Los textos de §8.3 son idénticos y las reglas/regex son las de §6.4. |
| F5 | ok | `useAuth({ withHistory })`, con default `true`. El dashboard no cambia. |
| F6 | ok | `hooks/useAdminAccounts.ts`: `users`, `loading`, `error`, `reload`, `upsert` y `refreshOne`. Recibe un `enabled`: no llama a la API hasta que la pantalla confirma superadmin + `local`. |
| F7 | ok | `AccountStatusBadge.tsx` (+ `AccountRoleBadge`) y `AccountActionsMenu.tsx`. |
| F8 | ok | `AccountsToolbar.tsx` (debounce de 200 ms) y `AccountsTable.tsx`, con los 4 estados y las tarjetas en mobile. |
| F9 | ok | `AccountFormDialog.tsx`: alta y edición, validación en blur y al enviar, `dni_taken` + "Ver cuenta existente", `stale_update` + "Recargar", y confirmación para descartar. |
| F10 | ok | `TemporaryPasswordDialog.tsx`: `closable={false}`, `closeOnEscape={false}`, `dismissableMask={false}`. El padre pone el estado en `null` al cerrar. |
| F11 | ok | `SuspendAccountDialog.tsx`. |
| F12 | ok | `AccountDetailDialog.tsx` (drawer `position="right"`) + `AccountHistory.tsx` (páginas de 20, "Ver más", deduplicación por `id`, cambios expandibles con `aria-expanded`). |
| F13 | ok | `AdminAccountsScreen.tsx`: guarda de rol y modo, `ConfirmDialog` para reactivar y resetear, toasts y `upsert` por fila. |
| F14 | ok | `app/admin/cuentas/page.tsx` (server component mínimo con `metadata`). |
| F15 | ok | `UserMenu` (`onOpenAdmin`, ícono `Users`, antes de "Mi perfil de perito") y `AppNavbar` (`role?` en el tipo de `user` y `canAdmin`). |
| F16 | ok | Greps de §14.2 limpios (abajo). |
| F17 | ok | Autochequeo con `web-design-guidelines` + §9.7 (abajo). |

## Archivos tocados

Nuevos:
- `client/src/lib/admin-accounts.ts`
- `client/src/hooks/useAdminAccounts.ts`
- `client/src/app/admin/cuentas/page.tsx`
- `client/src/components/admin/AdminAccountsScreen.tsx`
- `client/src/components/admin/AccountsToolbar.tsx`
- `client/src/components/admin/AccountsTable.tsx`
- `client/src/components/admin/AccountStatusBadge.tsx`
- `client/src/components/admin/AccountActionsMenu.tsx`
- `client/src/components/admin/AccountFormDialog.tsx`
- `client/src/components/admin/TemporaryPasswordDialog.tsx`
- `client/src/components/admin/SuspendAccountDialog.tsx`
- `client/src/components/admin/AccountDetailDialog.tsx`
- `client/src/components/admin/AccountHistory.tsx`

Modificados:
- `client/src/lib/api.ts`: tipos y métodos admin, y `ApiErrorBody` ampliado.
- `client/src/types/index.ts`: reexports.
- `client/src/hooks/useAuth.ts`: opción `withHistory`.
- `client/src/components/UserMenu.tsx`: ítem "Administrar cuentas".
- `client/src/components/shell/AppNavbar.tsx`: `role?` y `canAdmin` → `router.push("/admin/cuentas")`.
- `client/src/lib/password-policy.ts`: **cambio colateral necesario**. Al ampliar `ApiErrorBody.field`, `describeChangePasswordError` dejó de compilar. Se agregó el type guard `isPasswordField` sobre el `VALID_FIELDS` que ya existía. El comportamiento es el mismo.
- `client/src/components/feedback/CopyButton.tsx`: prop opcional `buttonLabel` (default "Copiar", así que no cambia nada para quienes ya lo usan). Hace falta para que el segundo botón diga "Copiar mensaje para el cliente" de forma visible.

No toqué `server/`, `agent-ui/` ni `progress/sesiones/`. Los cambios de `server/` que muestra `git status` son del implementer-backend, que trabaja en paralelo.

## Contrato (§8)

- Los nombres de §8.1 coinciden campo por campo con `server/src/Factum.Backend/DTOs/AdminUserDtos.cs`, que ya está en disco (del backend en paralelo) y verifiqué: `AdminUserDto`, los requests, `AdminUserEventDto` y `has_more`.
- Rutas E1-E9 exactas: `/api/admin/users[/{id}[/suspend|/reactivate|/reset-password|/unlock|/events?offset&limit]]`.
- E5 manda `{ reason }` solo si hay motivo; si no, `{}`. E6, E7 y E8 mandan `{}`.
- E4 manda siempre los 6 campos editables (trim) + `expected_updated_at = updated_at` tal como llegó.
- Los textos de §8.3 están literales en `ADMIN_MESSAGES`. Lo que se muestra en pantalla es siempre `serverMessage` de la API cuando viene (`adminErrorMessage`), y `ADMIN_MESSAGES` queda como fallback o validación local.
- No inventé ningún campo. No faltó ninguno.

## Verificación

```
$ cd client && npx tsc --noEmit
(sin salida, exit 0)

$ grep -rn "console\." client/src/components/admin client/src/lib/admin-accounts.ts client/src/hooks/useAdminAccounts.ts
(sin resultados)

$ grep -rn "temporary_password" client/src | grep -v "lib/api.ts\|TemporaryPasswordDialog\|AdminAccountsScreen"
(sin resultados)

$ grep -rn "localStorage\|sessionStorage" client/src/components/admin client/src/hooks/useAdminAccounts.ts client/src/lib/admin-accounts.ts
(sin resultados)
```

- Con el `npm run dev` que ya corría el usuario (no lo lancé ni lo maté), `GET http://localhost:3000/admin/cuentas` → 200 con el skeleton y el título "Administrar cuentas · Factum": la ruta compila.
- Saneo de las regex/normalización con node: `"12345"` → inválido, `"123456"` y `"+54 11 5555-1234"` → válidos, `"ab.co"` → email inválido, `"  Pérez "` → `"perez"`, `RelativeTimeFormat` → "hace 3 días".
- **No hice la prueba manual de §14.4 contra la API.** Habría que loguearse como superadmin y crear cuentas en `factum_dev`, y no sé si el backend que corre en `:8080` ya tiene los endpoints de la HU. Por la regla de datos de `AGENTS.md`, no escribí nada en la base. Queda para la prueba manual del usuario (§14.4).
- No corrí `next build`.

## Decisiones no obvias

1. **D4 en la UI = deshabilitado con explicación**, como pidió el orquestador, en lugar de ocultar (§9.4 decía "no en la fila propia").
   - En el menú "⋯" de la fila propia, "Resetear contraseña" y "Suspender" aparecen deshabilitados, con una nota al pie: "No podés suspender ni resetear la contraseña de tu propia cuenta. Para cambiarla usá “Cambiar contraseña” en tu menú."
   - En el detalle, esos botones van `disabled` con `aria-describedby` a esa misma nota visible.
   - "Desbloquear" sí se oculta si no hay bloqueo vigente, porque el Gherkin dice "no se ofrece".
   - La regla 2 (último superadmin) no se anticipa en la UI. Desde el panel solo puede darse por carrera, porque quien actúa es un superadmin activo y no se puede suspender a sí mismo. Si pasa, se muestra el 409 `last_superadmin` del backend.
2. **Guarda de la pantalla.** `denied` = `user.role !== "superadmin"` o (`mode` conocido y distinto de `local`) → `router.replace("/dashboard")`. Mientras `mode` es `null` se ve el skeleton. Las llamadas de admin arrancan solo con `allowed` = superadmin && `mode === "local"`. Un 403 `superadmin_required` / 404 `not_available` en cualquier llamada también redirige.
   - Caso borde: si `GET /api/auth/mode` falla, `useAuthMode` queda en `null` y el superadmin ve el skeleton. Así lo pide §9.4 ("hasta tener user y mode").
3. **Edición sin cambios se resuelve en el cliente.** Si el formulario (con trim) es igual a la cuenta, cierra con el toast "No había cambios para guardar" sin llamar a E4. El caso `changed: false` del servidor también se maneja, con el mismo toast.
4. **Errores de acción.**
   - Con el detalle cerrado: `FxBanner` (role alert) sobre la tabla.
   - Con el detalle abierto: el banner va dentro del drawer, porque si no quedaría tapado por la máscara modal.
   - El diálogo de suspensión muestra su propio error y queda abierto.
   - `invalid_state` → `refreshOne` + recarga del historial. `user_not_found` → `reload` y se cierra el detalle.
5. **Event bubbling del `Menu` popup.** Los eventos de un portal burbujean por el árbol de React, así que un click en un ítem abría el detalle de la fila. El disparador y el `Menu` van dentro de un `<span>` que hace `stopPropagation` de click y keydown.
6. **Tabla y mobile.** Desde `md` hay una tabla con `tr tabIndex={0}`, Enter abre el detalle (solo si el foco está en la fila misma) y el nombre va en `th scope="row"`. En mobile, cada tarjeta es un `<button>` real (sin interactivos anidados) + el menú "⋯" al costado. El estado "vacío" (solo superadmins) muestra el llamado "Todavía no hay clientes…" y debajo igual lista a los superadmins.
7. **`TemporaryPasswordDialog`.** Muestra también una vista previa del "mensaje para el cliente", para que el superadmin sepa qué copia. El foco inicial va a "Copiar". La temporal es una prop desde el estado `reveal` del padre, que se pone en `null` al cerrar: no va a storage, a la URL ni a la consola.
8. **Búsqueda.** `normalizeSearch` es una copia local con el criterio de `normalizeCatalogKey` (NFD + `\p{M}` + minúscula + trim), sin colapsar los espacios internos. No reusé `normalizeCatalogKey` porque `cleanCatalogValue` está pensado para deduplicar catálogos. El DNI se compara por `includes` sobre el texto normalizado.
9. **Estado en la URL.** El guideline de Vercel recomienda reflejar los filtros en query params, pero §9 prohíbe `useSearchParams` (evita `Suspense`). Los filtros y el detalle abierto quedan en el estado de React.
10. **Sin animaciones nuevas** (criterio de `mblode-agent-skills-ui-animation`: "delight scales inversely with frequency"; lo que se usa mucho no se anima). Diálogos y drawer usan las transiciones del pt global, que ya respetan `motion-safe`. Skeletons con `motion-safe:animate-pulse`, el chevron del historial con `motion-reduce:transition-none` y los hovers solo con `transition-colors`. No hay `transition: all` ni animaciones de layout.
11. **Validación (ui-ux-pro-max: "Error Placement" e "Inline Validation").** Errores inline debajo del campo con `aria-describedby` y foco al primero al enviar. En blur no se marca un obligatorio vacío que no se tocó. Si un campo ya tiene error, se revalida en vivo para sacarlo apenas se corrige.
12. **Mensaje propio con comillas tipográficas** (“Cambiar contraseña”). Los textos de `ADMIN_MESSAGES` conservan las comillas rectas de §8.3 porque tienen que ser idénticos al backend.

## Skills invocados

- **ui-ux-pro-max**: invocado antes del JSX. Búsquedas `--domain ux` de "data table row actions admin", "inline validation error field" y "empty state no results". Apliqué: tabla → tarjetas en mobile, error inline + `aria-describedby` + validación en blur, estados vacíos con acción ("Nueva cuenta" / "Limpiar filtros"). Descarté "Bulk actions" porque está fuera de scope.
- **senior-frontend**: callbacks memorizados donde importan (`upsert`, `refreshOne`, `reload`, `clearFilters`), `useMemo` para filtrado y contadores, contador de secuencia para descartar respuestas viejas (listado e historial) y debounce con ref al último `onChange`, para no reiniciar el timer en cada render del padre.
- **3d-web-experience**: usado como criterio. No hay 3D ni pseudo-3D y no se agregó nada ("3D for 3D's sake" es anti-patrón). Sin hallazgos aplicables.
- **ui-styling**: solo tokens `fx-*` (sin colores de la paleta Tailwind ni hex). Tablas, badges y skeletons con utilidades. Mobile-first: `md:` para la tabla y 44 px de alto en mobile (`h-11`, `max-md:min-h-11` en `SelectButton`, el "⋯" y "Ver cambios").
- **mblode-agent-skills-ui-animation**: ver la decisión 10.
- **web-design-guidelines**: autochequeo con las reglas actuales de vercel-labs (`command.md`) sobre los archivos tocados. Hallazgos corregidos:
  - `AccountFormDialog.tsx`: el placeholder no-ejemplo termina en "…";
  - `AccountActionsMenu.tsx`: comillas tipográficas en el texto propio;
  - `AdminAccountsScreen.tsx`: `text-balance` en el `h1`;
  - `TemporaryPasswordDialog.tsx`: saqué un `aria-label` sobre un `<p>` sin rol, que los lectores no anuncian de forma confiable;
  - `AccountsTable.tsx`: saqué el `aria-label` del `<tr>`, que tapaba el contenido de las celdas.

  Hallazgos no aplicados, a propósito:
  - URL con filtros: §9 lo prohíbe;
  - Title Case: el repo usa sentence case en español;
  - `touch-action`: es global de la app, fuera de scope.

## Checklist de accesibilidad §9.7

- [x] Tabla semántica: `<caption className="sr-only">`, `th scope="col"`, nombre en `th scope="row"`, fila con `tabIndex={0}` + Enter.
- [x] Diálogos de Prime: atrapan el foco y lo devuelven al disparador. Foco inicial explícito: "Cancelar" en suspender y en `ConfirmDialog`, primer campo en el formulario, "Copiar" en la temporal.
- [x] Errores con `role="alert"` (`FxBanner` error y errores de campo de `FormField`). Éxitos con toasts. Resultado del filtro anunciado con `role="status"` / `aria-live="polite"` ("N de M cuentas").
- [x] El estado nunca va solo con color: `Tag` con texto + ícono (Activa / Suspendida / Bloqueada hasta HH:MM / Pendiente de primer ingreso).
- [x] Botones de al menos 44 × 44 px en mobile (menú "⋯", limpiar búsqueda, filtros segmentados, "Ver cambios", "Volver al dashboard", "Nueva cuenta").
- [x] Animaciones con `motion-safe` / `motion-reduce`.
- [x] Íconos decorativos con `aria-hidden`. Botones de solo ícono con `aria-label` ("Acciones para {name}", "Limpiar búsqueda"). El menú lleva `aria-haspopup`, `aria-expanded` y `aria-controls`.
- [x] DNI y temporal con `translate="no"`, números con `tabular-nums`, `spellCheck={false}` en email y búsqueda, `inputMode` numeric/tel/email y `autoComplete="off"`.

## Bloqueos

Ninguno.

## Para la prueba manual (§14.4)

Requiere que el backend de esta HU esté corriendo en modo `local`. Usar solo DNIs de prueba inexistentes (`99000010`…).
