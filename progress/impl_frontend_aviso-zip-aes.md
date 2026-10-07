# impl_frontend — aviso-zip-aes (tarea chica fuera del backlog, sin HU/SDD)

Rama: `fix/aviso-zip-aes` · App: `client/` · Estado: **done** (sin commit, según lo pedido)

## Problema
El aviso de compatibilidad del ZIP AES-256 era una línea `text-xs text-fx-text-3` debajo de los botones
y pasaba desapercibido. Un usuario abrió el ZIP con doble clic en macOS (Utilidad de Archivo), le dio error
y creyó que la contraseña estaba mal.

## Archivos tocados
- `client/src/components/ZipCompatNotice.tsx` (**nuevo**): único componente con el texto. Usa el
  `FxBanner` existente (`tone="warn"`, `role="note"`), con el icono AlertTriangle por defecto del tono.
  Acepta `id` (para `aria-describedby`) y `className`.
  Texto: título "¿Te dice que la contraseña es incorrecta?" + "No es la contraseña: el programa que viene
  con Windows o con macOS no abre este cifrado (AES-256). Usá 7-Zip o WinRAR (Windows) o Keka /
  The Unarchiver (macOS)." Los nombres de programas van con `translate="no"`.
- `client/src/components/ResultStep.tsx`:
  - Flujo `server`: se quitó la línea chica de abajo de los botones. El aviso queda **entre** la sección
    con la contraseña y los botones de descarga. Mantiene `id="result-zip-compat"`, así que el
    `aria-describedby` del link "Descargar ZIP" sigue funcionando.
  - Flujo `agent`: se quitó la línea chica del final de la sección "Evidencia ZIP". El aviso queda justo
    **antes** de `ZipLocalActions` ("Mostrar en carpeta" / "Guardar una copia…"), debajo de la contraseña
    y el hash.
  - Se sacó el import `Lock`, que ya no se usaba.
- `client/src/components/CaseCard.tsx`: en el historial (caso expandido, bloque "Paquete del informe"),
  el aviso aparece justo después de `ZipPasswordRow`, solo si `isEncrypted`. Ahí también se ve la
  contraseña y se abre o guarda el ZIP.

En los tres lugares, el aviso se monta solo si el ZIP está cifrado (`encrypted` / `isEncrypted`, que salen
de `zip_encrypted === true`). No se cambió lógica, props, contratos con el backend ni con Tatana.

## Decisiones no obvias
- **`role="note"` y no `status`/`alert`**: es contenido estático que se ve con la pantalla, no un evento.
  Es la misma convención que documenta `lib/prime/pt/message.ts`. `alert` interrumpiría al lector de
  pantalla en cada render del resultado.
- **Tono `warn`**: el problema hace que el usuario sospeche de la contraseña, que es la evidencia crítica.
  Contraste de tokens: claro `#8a5a00` sobre `#fbf0da` ≈ 5.4:1 y oscuro `#f2b13c` sobre `#2a2011` ≈ 8:1.
  Los dos pasan AA para texto chico.
- **Se agregó en CaseCard (historial)**, aunque el pedido citaba ResultStep. El objetivo era "donde el oficial
  mira la contraseña y abre o guarda el ZIP", y el historial es ese mismo momento. Es el mismo componente,
  sin texto duplicado. **No** se agregó en `CaseGridCard`: es una tarjeta compacta, sin contraseña visible,
  y un banner ahí sería ruido. Si el orquestador prefiere limitarlo a ResultStep, alcanza con revertir
  2 líneas de `CaseCard.tsx`.
- Sin animación nueva: solo hereda el `fx-fade-in` de FxBanner, que es `motion-safe`, y en server el
  `FADE_IN` del resto de los bloques.

## Skills invocados
- `ui-ux-pro-max`: consulta `"inline warning callout icon" --domain ux`. Lo aplicado: aviso pegado al
  control al que se refiere y enlazado con `aria-describedby` (link de descarga del flujo server), icono
  más texto (no solo color) y contraste AA verificado sobre los tokens.
- `senior-frontend`: extraer un único componente presentacional sin estado. No hace falta `"use client"`
  porque solo lo importan componentes cliente y FxBanner ya lo declara. Sin cambios de props ni de
  lógica en los consumidores.
- `3d-web-experience` (criterio): no hay pseudo-3D ni animaciones sin propósito. No se agregó 3D.
- `web-design-guidelines`: se trajeron las guías de Vercel y se revisaron los 3 archivos. Icono decorativo
  con `aria-hidden` (default de FxBanner), sin comillas rectas ni `...`, `text-pretty` en el párrafo,
  animación con `motion-safe`, texto largo que hace wrap dentro de un contenedor `min-w-0`. Sin hallazgos
  pendientes.
- `ui-styling` y `mblode-agent-skills-ui-animation`: no aplican (no hay shadcn ni transiciones nuevas).

## Verificación
```
$ cd client && npx tsc --noEmit
EXIT=0
```
No hay tests en `client/`. No se corrió `next build` (regla del checkout principal).

## Prueba manual sugerida
1. Generar un caso con ZIP cifrado: en el paso de resultado, el banner ámbar aparece entre la contraseña y
   los botones (server) o arriba de "Mostrar en carpeta" (agent).
2. Generar un caso sin cifrar: no aparece el banner.
3. Historial → expandir un caso cifrado: el banner aparece debajo de la contraseña.
4. Tema oscuro: revisar la legibilidad.
