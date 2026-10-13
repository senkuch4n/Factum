# Feedback del usuario — #56 (2026-10-13), prueba manual

Usuario: "me parece tosco todavía el formulario". Diagnóstico del orquestador mirando la pantalla real
(Chrome 1366x656, tema oscuro, Samsung SM-M127F conectado, paso 2 con precarga):

1. **Cajas dentro de cajas:** card de página > card del formulario > card de perfil/banner, y la ficha
   también es card con scroll interno propio. Se ve encajonado y el formulario queda en una columna
   angosta (~460 px) entre el rail de pasos y la ficha. Aplanar: una sola superficie, separar con
   espacio y divisores, no con bordes anidados. Más ancho útil para los campos.
2. **Etiquetas que se parten en 2–3 líneas:** MAYÚSCULAS + tracking + ícono + "· opcional" +
   íconos de historial hacen que "NÚMERO DE CAUSA / EXPEDIENTE", "FECHA DE INTERVENCIÓN",
   "OBJETO DE LA CAUSA · opcional", "DESTINATARIO (tribunal, fiscalía, persona…)" se rompan, con
   íconos desalineados. Etiquetas en sentence case, una línea, peso medio; "opcional" como texto
   tenue a la derecha o en el hint; aclaraciones como hint debajo del campo; íconos fuera o
   alineados a la primera línea. "Hoy / Ayer" como chips del campo, no en la línea de la etiqueta.
3. **Grilla irregular:** expediente ocupa media columna sola; pares de campos con alturas de
   etiqueta distintas desalinean los inputs. Grilla consistente (expediente + fecha en una fila,
   carátula a ancho completo, etc.), inputs alineados por la base.
4. **La ficha no luce y el 3D no se ve:** "Sin expediente" en monoespaciada itálica desentona; la
   lista de "Sin carátula / Sin denunciante / Sin titular" es ruido; el teléfono 3D queda chico y al
   fondo de la ficha, visible solo scrolleando dentro de ella. Rediseñar la ficha como carátula
   elegante: el teléfono 3D arriba y protagonista (más grande), datos vacíos como placeholders sutiles
   o colapsados, tipografía de la app, sin scroll interno (sticky con altura del viewport; si no
   entra, priorizar 3D + progreso + carátula/expediente).
5. **Banner de precarga azul** que choca con la marca verde, con tres acciones como links sueltos.
   Integrarlo con tokens neutros/acento, más compacto, acciones como botones ghost pequeños o menú.
6. **Jerarquía:** el título "Datos de la causa" queda debajo del banner; encabezados de sección
   muy chicos. Título y progreso primero; secciones con encabezado más claro.
7. **Densidad general / pulido:** bordes y fondos de input pesados, espaciados irregulares;
   buscar un acabado más refinado (radios, bordes sutiles, focus ring, ritmo vertical de 8 px).

Revisar también en tema claro y a 1366x656 (la pantalla real del usuario) además de 1440+.

## Feedback 2 (2026-10-13), tras el reintento 1

Usuario: "me gusta mucho más, pero siento que la parte de llenar los datos no es tosca pero me abruma".
Elección explícita del usuario: **una sección a la vez (acordeón guiado)**:
- Solo una sección abierta: la activa. Las completas y las precargadas quedan **cerradas como resumen de
  una línea** (p. ej. "✔ Causa — 1234/2026 · Pérez c/… · 13/10" / "✔ Destinatario — Sala II · Tribunal X
  (precargado)") con acción "Editar" que la abre (y cierra la anterior).
- Al abrir el paso 2 se abre la primera sección con faltantes. Cada sección abierta termina con
  "Siguiente →" que valida esa sección (errores solo de esa sección, foco al primero) y abre la próxima
  con faltantes; la última muestra "Crear caso y continuar". Enter dentro de una sección = Siguiente.
- El índice de la ficha (Causa / Destinatario / Partes / Equipo) también navega: clic abre esa sección.
- Si al crear el caso hay errores en otra sección, se abre esa sección y se enfoca el primer error
  (mantener el comportamiento de foco existente).
- Las secciones precargadas cerradas deben indicar "precargado — revisar" de forma discreta para no
  perder la revisión que pedía la HU (D3).
- Transición de apertura/cierre con microinteracciones de tokens; con reduced-motion sin animación de
  altura. a11y: encabezados como botones con aria-expanded/aria-controls, resumen legible por lector.
- Tamaño del teléfono 3D: sin cambio (el usuario no pidió agrandarlo).
