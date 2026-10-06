# Implementación frontend — marca-por-cliente (#13)

**Estado:** done (sin commit, como se pidió)
**App:** solo `client/` (Next.js 16). `agent-ui/` y `server/` no se tocaron. Los cambios de `server/` que aparecen en el working tree son del `implementer-backend`, que trabaja en paralelo.
**SDD:** `Refactorizaciones/marca-por-cliente.md` §8, §9 y §10.2. DP1 = A: aviso en el formulario, sin precarga.

## Checklist §10.2

- [x] **F1.** Leí `client/AGENTS.md`. Todo lo nuevo son componentes `"use client"` y no usa APIs de Next que hayan cambiado. Invoqué los skills (ver más abajo).
- [x] **F2.** `lib/api.ts`:
  - los tipos de §8.1 (`BrandingImageKind`, `BrandingImageAction`, `BrandingImage`, `AccountBranding`, `BrandingSaveMetadata`, `AccountBrandingSaveResponse`, `BrandingField` y `BrandingErrorCode`);
  - `ApiErrorBody` suma `index`, `contrast` y `min_contrast`, y `field` incluye `BrandingField`;
  - `AdminAction` suma `"update_branding"`;
  - los métodos `getMyBranding`, `saveMyBranding`, `adminGetBranding`, `adminSaveBranding` y `getBrandingImage`;
  - un helper `sendMultipart`, con el mismo patrón que `finishGeneration`: sin `Content-Type` manual, con `Authorization`, y la red caída se convierte en `ApiError(SERVER_UNREACHABLE, 0)`.

  `types/index.ts` reexporta los tipos.
- [x] **F3.** `lib/branding.ts` (nuevo, puro):
  - constantes de §8.4 y `BRANDING_MESSAGES` idénticos a §5.1, verificados contra `BrandingErrors.cs` del backend;
  - `normalizeHex`, `contrast` y `contrastWithWhite`, con el mismo algoritmo y el umbral 0.03928 de `BrandingColors.cs`;
  - `displayContrast`, que trunca a 1 decimal y formatea con `toFixed(1)`;
  - `validateBrandingForm` con el orden de §5, `brandingToForm`, `sameBrandingForm`, `buildBrandingMetadata` / `buildBrandingFormData`, `inspectImageFile` (peso → firma → `createImageBitmap`, que se cierra) y `brandingErrorMessage`.
- [x] **F4.** `hooks/useMyBranding.ts`: store de módulo con `useSyncExternalStore`.
  - Si cambia el DNI, descarta el estado y revoca el object URL.
  - `setMyBranding` vuelve a pedir el logo solo si cambió `version`.
  - `resetMyBranding` se llama en `useAuth.handleLogout`.
  - Las respuestas en vuelo de una sesión anterior se ignoran (contador `generation`).
- [x] **F5.** En `components/branding/`:
  - `ColorField.tsx`: selector nativo, campo hex y un badge con texto **e ícono** dentro de `aria-live="polite"`;
  - `ContactLinesField.tsx`: hasta 6 líneas, "Agregar línea" y "Quitar" con foco gestionado;
  - `BrandingImageField.tsx`: arrastrar y soltar, más un botón accesible por teclado. Además incluye el hook `useBrandingImageSrc`, que pide la imagen como blob con `Authorization` y la revoca al cambiar o desmontar.
- [x] **F6.** `BrandingPreview.tsx`: mini A4 (`aspect-[210/297]`) con fondo blanco y tinta `#0E1013` fijos. Tiene:
  - membrete: el logo si hay; si no, el nombre;
  - las líneas de contacto;
  - un filete y los números de sección en el primario efectivo;
  - una fila de tabla con fondo de acento;
  - el isotipo al cierre;
  - un `figure` con `aria-label="Vista previa del informe"` y la nota exacta.
- [x] **F7.** `BrandingForm.tsx`: grilla de 2 columnas desde `lg`, con la vista previa `sticky`, y una sola columna en mobile con la vista previa debajo. Tiene el contador `n/150` y el chip "Usar «sugerencia»" (D10).

  `BrandingDialog.tsx`:
  - target `self` o `account`; ancho `w-[min(64rem,100%)]` y pantalla completa en `max-sm`;
  - el subtítulo fijo de §9.4;
  - un skeleton mientras carga, y un `FxBanner` con "Reintentar" si falla la carga;
  - el aviso de "sin marca" en primera o tercera persona, con la frase de DP1-A cuando la instalación tiene marca;
  - al cancelar con cambios, pide confirmación con `ConfirmDialog`;
  - errores: un 400 se muestra en su campo, con el `index` reconvertido al índice del formulario porque las líneas vacías no viajan, y lleva el foco; un 409 muestra un banner con "Recargar"; un 413 o un corte de red muestran un banner;
  - los toasts tienen los textos exactos de la SDD; si es la propia marca, llama a `setMyBranding`;
  - los object URLs se liberan al cerrar o desmontar.
- [x] **F8.** `UserMenu.tsx`:
  - agrega la prop `onOpenBranding` y el ítem "Marca del informe" (`Palette`) después de "Mi perfil de perito";
  - la organización sale de `useMyBranding(user.dni)`: si la cuenta tiene marca, se muestra la suya aunque esté vacía (si está vacía no hay ítem); si no, la config pública.

  `AppNavbar.tsx` monta el `BrandingDialog` (`self`) en todos los modos. `useAuth.ts` llama a `resetMyBranding()`.
- [x] **F9.** Panel:
  - `AccountActionsMenu`: `"branding"` → "Editar marca", después de "Editar", para cualquier cuenta;
  - `AccountDetailDialog`: botón "Editar marca";
  - `AdminAccountsScreen`: el diálogo `account`. `onSaved` sube `historyKey` y, si `dni === user.dni`, llama a `setMyBranding`.
- [x] **F10.** `lib/admin-accounts.ts`:
  - `ACTION_LABELS.update_branding`;
  - las 6 `FIELD_LABELS`;
  - en `formatChangeValue`, un color vacío se muestra como "Color de Factum", y una imagen como "versión {v}" o "(sin imagen)".

  `AccountHistory.tsx`: ícono `Palette`, y `update_branding` es expandible.
- [x] **F11.** Hice el autochequeo con `web-design-guidelines` y la checklist de §9.7 (ver más abajo).

## Archivos

Nuevos:
- `client/src/lib/branding.ts`
- `client/src/hooks/useMyBranding.ts`
- `client/src/components/branding/{BrandingDialog,BrandingForm,BrandingImageField,BrandingPreview,ColorField,ContactLinesField}.tsx`

Modificados:
- `client/src/lib/api.ts`
- `client/src/types/index.ts`
- `client/src/lib/admin-accounts.ts`
- `client/src/hooks/useAuth.ts`
- `client/src/components/UserMenu.tsx`
- `client/src/components/shell/AppNavbar.tsx`
- `client/src/components/admin/{AccountActionsMenu,AccountDetailDialog,AccountHistory,AdminAccountsScreen}.tsx`

## Verificación

```
cd client && npx tsc --noEmit   → exit 0, sin salida
```

- Nunca corrí `next build`. No hay tests en `client/`, y la SDD no pidió ninguno.
- Ninguna prueba tocó la base, y no maté ningún proceso.
- **Contrato:** comparé los nombres de §8.1 con `server/src/Factum.Backend/DTOs/BrandingDtos.cs` (que ya escribió el backend) y los textos de §5.1 con `Services/Branding/BrandingErrors.cs`. Coinciden. Uso `field`, `index`, `contrast`, `min_contrast` y `max_bytes` tal como dice la SDD, sin campos inventados.

## Skills invocados (tool `Skill`)

- **ui-ux-pro-max**, antes del JSX. Consultas: `"file upload drag drop"` (WCAG 2.2 "Dragging Movements": tiene que haber una alternativa con un solo puntero o con teclado → zona-botón + "Elegir archivo" / "Reemplazar") e `"inline validation error focus"` (error inline conectado con `aria-describedby` y foco al primero cuando falla el envío).
- **senior-frontend**: un store de módulo con `useSyncExternalStore` y sin contexto nuevo; ciclo de vida de los object URLs (se revocan al reemplazar, cerrar o desmontar, y con control de carreras por secuencia); `AbortController` para los blobs de imagen.
- **3d-web-experience** (como criterio): la vista previa es una hoja plana, sin tilt, perspectiva ni animación. No agregué 3D ni efectos pseudo-3D.
- **ui-styling**: solo tokens `fx-*` (temas claro y oscuro). Los únicos colores fijos son los de la hoja y las miniaturas (blanco y la tinta), a propósito, porque representan el papel del informe. Las áreas táctiles miden ≥ 44 px en mobile (`h-11` / `max-md:min-h-11`).
- **web-design-guidelines**: autochequeo sobre `components/branding/*`, `UserMenu`, `AccountDetailDialog` y `AccountActionsMenu`. Lo que corregí:
  - estado hover en la zona de subida;
  - `touch-manipulation`;
  - `width`/`height` en la miniatura;
  - `translate="no"` en el nombre y el contacto de la vista previa.
- **mblode-agent-skills-ui-animation**: no lo invoqué, porque no agregué transiciones ni gestos animados nuevos. Solo `transition-colors` con los tokens existentes y spinners con `motion-safe:`.

## Checklist §9.7

- Foco atrapado y devuelto: lo hace Prime. Para eso, en el panel el diálogo queda montado y se cierra con `visible=false`, así Prime devuelve el foco.
- Zonas de subida operables con teclado, con `aria-describedby` hacia la ayuda (y hacia el error si hay uno).
- Errores con `aria-invalid`, `aria-describedby` y foco al primero, tanto en la validación local como en un 400.
- El contraste se anuncia con `aria-live="polite"`, y el badge tiene ícono y texto, además de un `sr-only` con la referencia ("con blanco" / "con el texto").
- `prefers-reduced-motion`: las únicas animaciones son spinners y el pulse del skeleton, con `motion-safe:`.
- No probé en un navegador mobile ni con los dos temas (el arnés no tiene navegador). La vista previa usa fondo y tinta inline, independientes del tema. Esto queda para la prueba manual §14.4.

## Decisiones no obvias

1. **Errores en vivo y botón deshabilitado.** "Guardar marca" se deshabilita mientras haya errores de cliente, como pide la SDD. Por eso los errores se muestran en vivo, con una excepción: el error de **formato** de un color no se muestra mientras ese campo hex tiene el foco, para no marcar "#12" a medio escribir. La guía de Vercel prefiere el botón habilitado, pero manda la SDD.
2. **Un archivo rechazado no bloquea el guardado.** Su error se muestra, pero la acción sigue igual (`keep`).
3. **Envío sin cambios.** Si la marca ya existe y el formulario no cambió, no se manda el request: se muestra el toast "No había cambios para guardar." y el diálogo se cierra. Es el mismo patrón que `AccountFormDialog`. Si la marca no existe, el request se manda igual y el backend responde `changed:false` sin crear el documento (DT7).
4. **"Quitar" sin imagen guardada vuelve a `keep`**, nunca manda `remove`. Con imagen guardada, "Quitar" pone `remove` y muestra "Se va a quitar al guardar", con el botón "Restaurar".
5. **El `UserMenu` no muestra organización mientras carga la marca propia**, para no mostrar un instante la de la instalación a una cuenta que tiene la suya. El logo del menú va sobre un chip blanco, porque un logo pensado para la hoja blanca se perdería en el tema oscuro. Además, si el logo falla, se marca por `src` y no con un booleano fijo, así un logo nuevo se vuelve a intentar.
6. **Contraste.** El badge y la validación comparan contra 4.5 con el valor sin truncar, y muestran el valor truncado, como en el backend.

## Observaciones (sin bloqueo)

- `/design-system` monta el `UserMenu` con `DEMO_USER`. Si hay sesión, el ítem de organización muestra la marca de la cuenta real (por el token), igual que ya hacía "Mi perfil de perito". Es solo del showcase y no lo cambié.
- `ChangePasswordForm` llama a `api.logout()` sin `resetMyBranding()`. Está cubierto igual: un cambio de DNI descarta el store.
