# Review — rediseno-dashboard-captura

**Veredicto:** APROBADA

Verificado por mí: `npx tsc --noEmit` en `client/` exit 0. `npm run build` en un worktree temporal con `client/src` copiado (ya borrado): compila, TypeScript OK, rutas `/`, `/dashboard` y `/design-system` generadas. No toqué el `.next` del checkout principal.

## Checkpoints
- C1 (arnés): [x] fuera de alcance de este diff (cambios administrativos ignorados por pedido).
- C2 (docs): [x] SDD con Contrato compartido ("sin cambios"). El diff no toca `api.ts`, `agent.ts`, `pericial.ts`, `types/` ni `hooks/`. Los nombres generados `foto_${webcamTarget}_<ts>.jpg`, `grabacion_camara_<ts>.webm` y `adjunto_<fecha>_<hora>_<n>.<ext>` son idénticos a HEAD.
- C3 (arquitectura): [x] Solo `client/`. `page.tsx` y `PhoneFrame.tsx` no están en el diff. `PhoneShell`, `StepHeader`, `StepActions`, `FxBanner` y el pt `dropdown` se reutilizan, no se duplican. Sin `console.log`. El único cambio en un archivo de partes previas es `pt/dropdown.ts` (una línea, `outline-none` para el foco), permitido por la SDD T3 y documentado. `dialog.ts` e `index.ts` solo suman código.
- C4 (verificación): [x] tsc y build OK. La SDD no pide tests.
- C5 (cierre): [x] Progress frontend presente, con constancia de `ui-ux-pro-max`, `senior-frontend`, `3d-web-experience` y `web-design-guidelines` y lo que salió de cada uno. Sin temporales en `client/`.

## Revisión puntual
- Streams: `WebcamCaptureModal` y `CameraRecordModal` mantienen el estado en un `Body` hijo del `Dialog`, que Prime solo monta mientras está visible. El cleanup de desmontaje hace `stopStream` (`getTracks().forEach(t => t.stop())`), `clearInterval` y `revokeObjectURL` (`WebcamCaptureModal.tsx` L122-131; `CameraRecordModal.tsx` L129-192). El `tempStream` de enumeración también se detiene (`CameraRecordModal.tsx` L179).
- Grep de reglas T2 sobre los archivos de la parte: sin `framer-motion`, `AnimatePresence`, `style={`, variables legacy, hex, `rgba(`, `dark:`, `bg-black` ni `text-white`.
- No se duplicó nada de las partes 1 y 2.

## Cambios requeridos
Ninguno.

## Observaciones no bloqueantes
- `capture/MediaErrorGuide.tsx` es un archivo extra que no estaba en T1 (guía de error y `MEDIA_ACTIONS` compartidos). Está justificado porque elimina un bloque duplicado.
- Preexistente según el implementador: la grabación de la cámara externa aparece en dos chips en la bandeja (el `LocalFile` y el adjunto que agrega `page.tsx`). Conviene una tarea aparte.
- Pendiente de prueba manual: equipo real Android/iOS, AirPlay, la guía on_device, la luz de la webcam, el `Dropdown` de cámara/micrófono con más de un dispositivo y el foco desde el checklist del paso 5 hacia `#capture-roles-status`.
- Parte 4: los `…:outline` sueltos bajo `cn()` se pierden (tailwind-merge). Antes de borrar las clases legacy hay que hacer grep.
