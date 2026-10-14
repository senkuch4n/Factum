# QA final integrada de Factum, 2026-10-13 (segunda corrida, con la sesión de Jperez)

**Veredicto: sin regresiones.** Los puntos 1 a 6 pasaron. No hay hallazgos Críticos ni Altos. Quedan 2 Medios y 4 Bajos, todos de pulido o preexistentes.

Entorno: web en :3000 (sesión de Jperez), backend en :8080, Tatana en :8765 (`0.0.0-dev`) y el Samsung SM-M127F (R58RA3R5AGA, Android 11) por USB. Código: main en 4b4f81c, que es el merge de develop en 62618df. No se reinició nada. No hubo errores de consola en todo el recorrido.

> **Sobre los tiempos:** la pestaña de Claude in Chrome corría con `document.visibilityState = "hidden"`, porque la ventana no estaba en primer plano. Con la pestaña oculta, el navegador frena `requestAnimationFrame`, así que las transiciones entre pasos (1→2, 3→4, 4→5 y 5→3) solo avanzaban cuando se sacaba una captura y parecían durar entre 5 y 20 s. **No es un defecto del producto.** Los tiempos de red y de backend de la tabla de abajo sí son confiables.

## Resultado por punto

| # | Prueba | Resultado |
|---|---|---|
| 1 | Paso 1: el Samsung aparece con IMEI y serial | OK |
| 1 | Paso 2 con el expediente QA-FINAL-001 | OK. El acordeón Siguiente/Editar mueve el foco, la ficha se actualiza mientras se escribe (expediente, carátula, contador de obligatorios), el chip dice "Samsung SM-M127F · Android 11" y la precarga desde 1213123124 trae "Mantener". El teléfono 3D salía **negro** porque la pantalla del celular estaba apagada (`Display Power: state=OFF`). Lo desperté con `adb input keyevent WAKEUP` y con "Actualizar captura" se vio Google Play. |
| 1 | Paso 3: 2 capturas | OK, 2 PNG de unos 44 KB |
| 1 | Paso 3: grabación de unos 3 min | OK. Se detuvo en menos de 1 s (a 1 s del clic ya estaba otra vez "Grabar"). El cronómetro marcaba **03:05**, la tarjeta dice 03:05 y `ffprobe` da **185,855 s** (h264 486x1080 + opus). |
| 1 | Paso 4 | OK. Textos por defecto con "Capturas de pantalla: 2" y "Grabaciones de pantalla: 2", autoguardado ("Guardado hace un momento"), y los textos se mantienen después de ir y volver. |
| 1 | Paso 5: ZIP cifrado | OK. Pidió marcar una captura como "IMEI y modelo" (se marcó en el paso 3). prepare → zip → finish → commit respondieron todos 200, en unos 5 s. **La verificación no falló.** La auditoría independiente también da bien: `7zz t` con la contraseña dice "Everything is Ok" (4 archivos) y el SHA-256 del ZIP coincide con el que muestra la UI (`e03a0101…1780`). |
| 1 | Paso 6 | OK. Muestra el hash del DOCX, la ruta del ZIP, la contraseña, "Mostrar en carpeta" y "Guardar una copia". |
| 1 | DOCX (copiado de `server/src/Factum.Backend/dev-data/cases/8f37d42d…/`, hash igual al de la UI `c8b649a8…1bd9`) → PDF con soffice → imágenes | **Portada en una página: OK. "Android 11" una sola vez (pág. 4): OK. Firma: OK**, porque el bloque "SE DEJA CONSTANCIA", la línea, el nombre, la profesión con M.P. y el carácter quedan juntos en la pág. 7, con el isotipo debajo. Tiene 8 páginas. Ver B1. |
| 2 | Grabación de unos 30 s → recargar → "Retomar" | OK. "Pendientes de retomar" muestra QA-FINAL-001 y el chip REC sigue en el header. Al retomar aparece "Detener" y "Grabando desde 18:06" con el cronómetro corriendo (00:29 → 00:38). Al detener: cronómetro 00:38, archivo de **38,868 s**, guardado en el caso (4 evidencias en Mongo) y vista previa que reproduce enseguida. |
| 3 | Dos pestañas con el mismo celular | OK. La segunda pestaña en el paso 2 muestra el aviso "Este celular también está abierto en otra pestaña (caso QA-FINAL-001)…". Su vista previa usa `POST /devices/…/preview`, que va a `agent-data/previews/` y no entra al caso. Después de dos vistas previas, el caso sigue con 4 archivos. Se cerró la pestaña sin crear caso. |
| 4 | Doble clic rápido en "Pantalla" | OK: una sola captura (un solo `POST /screenshot`) |
| 4 | Papelera de una captura | OK. El modal es propio de la app ("¿Borrar esta evidencia?"), `activeElement` es el botón **Cancelar** y se canceló. |
| 5 | Historial: buscar "QA-FINAL" | OK, "1 de 6 resultado" y el caso aparece Completado |
| 5 | Métricas | OK. Total 6, 100 % completados, gráfico por mes, sin errores. |
| 5 | Header a 390 px (iframe de 390 px: `resize_window` no achicó la ventana) | OK: el chip de Tatana baja a una segunda fila (y de 76 a 108) y no tapa "Métricas" (y de 14 a 50). Ver M2. |
| 6 | `curl -D- localhost:8080/api/config/public` | OK: 200, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `Referrer-Policy: no-referrer` y **sin `Server`** |

## Hallazgos

| ID | Gravedad | Dónde | Reproducción | Esperado / obtenido | archivo:línea probable |
|---|---|---|---|---|---|
| M1 | Media | Paso 3, vista previa de la grabación de 3 min | Grabar unos 3 min y detener | Esperado: el `<video>` carga enseguida. Obtenido: quedó en spinner (`readyState 0`, `networkState 2`) más de 30 s. Un `<video>` nuevo con la misma URL cargó al instante (185,855 s), y el de la tarjeta terminó reproduciendo más tarde. Con la grabación de 38 s no pasó. Puede que el `<video>` se monte antes de que termine el remux/import y quede con la primera respuesta colgada. **Ojo:** la pestaña oculta pudo influir; hay que confirmarlo con la pestaña visible. | `client/src/components/VideoCard.tsx:105-107` (src fijo, sin reintento ni `key` por revisión) |
| M2 | Media (visual) | Header a 390 px | Dashboard a 390 px de ancho | La píldora activa "Inspecciones" (x de 55 a 169) se monta unos 10 px sobre el círculo del logo (40 px desde x 25). La nav centrada con `justify-center` desborda hacia la izquierda. | `client/src/components/shell/AppNavbar.tsx:139` (`flex-1 justify-center`) y `:71` (nav `justify-center`); `BRAND_CAP` en `:44` |
| B1 | Baja | Informe DOCX, pág. 2 | Tipo de causa = "Causa" (el valor por defecto que se precarga) | La frase queda "en el marco de la causa **Causa N°** QA-FINAL-001": la palabra se repite. Es preexistente, de la plantilla. | `server/src/Factum.Backend/Templates/plantilla_informe_v6.docx` ("…en el marco de la causa {tipoCausa} N° {numeroCausa}"), generada desde `ops/plantilla/build_plantilla_v4.py` |
| B2 | Baja | Paso 2, Partes | Escribir en "Parte denunciante" y pasar con el clic a "Parte denunciada" | La lista de sugerencias de la primera ("Nuevo: «…»") quedó abierta junto con la de la segunda: 2 listas a la vez. Se cierra con un clic afuera. | combobox de sugerencias del paso 2 (cierre en blur) |
| B3 | Baja | Paso 3 | Ir del paso 5 al 3 con el link "Paso 3" de los faltantes | Aparece el banner "Retomando inspección · QA-FINAL-001" aunque no es una retoma. Además, después de importar, el cliente pide `GET :8765/files/screenshot_….png` (la ruta temporal) y recibe 404, sin efecto visible. | flujo de navegación al paso 3 / URL temporal del preview en el paso 3 |
| B4 | Baja (texto) | Historial | Buscar "QA-FINAL" | Dice "1 de 6 resultado". Sería mejor "1 de 6 resultados" o "1 resultado de 6". | contador del dashboard |

Nota: la extensión de Chrome reportó 503 en `POST /api/agent-events`, pero `performance.getEntries` da **204** y los eventos se guardaron. Es un falso positivo de la herramienta y no se reporta como hallazgo.

## Tiempos

| Medición | Valor |
|---|---|
| Crear caso (clic → paso 3, red) | menos de 4 s (8,8 s contando la sobrecarga de las herramientas) |
| Captura de pantalla (clic → miniatura) | menos de 4 s |
| Inicio de grabación (clic → REC 00:01) | unos 2 s |
| Detener grabación de 3 min | menos de 1 s en la UI y el archivo importado enseguida |
| Duración del archivo frente al cronómetro | 185,9 s / 03:05 y 38,9 s / 00:38 |
| Generar (clic → paso 6) | unos 5 s (prepare, zip con verificación, finish y commit) |
| ZIP | 1.176.577 B (4 archivos) |
| DOCX | 287.717 B, 8 páginas |

## Lo que anduvo bien
Retomar una grabación después de recargar (antes A2) ya funciona, y el aviso y el aislamiento entre pestañas (antes A1) también. El doble clic en captura no duplica. La grabación ya no pierde la cola (antes C2): coincide con el cronómetro. El ZIP se verifica y abre con 7zz. El informe ya no repite "Android Android", la firma queda junta y la portada entra en una página. El chip de Tatana muestra el celular correcto. Los headers de seguridad están bien. No hubo errores de consola.

## Datos creados (para borrar solo con permiso del usuario)
- **Mongo `factum_dev.cases`**: `_id = 8f37d42d-0aa9-4d86-aca1-b03ca7b84b28` (NroReferencia `QA-FINAL-001`, Status 2 = completado).
- **Derivados de ese caso**: 8 docs en `agent_events` y 12 en `case_events` con `CaseId = 8f37d42d-0aa9-4d86-aca1-b03ca7b84b28`, más las sugerencias nuevas "QA-FINAL Denunciante" y "QA-FINAL Denunciada" (si `catalog_entries` las guardó con el caso) y las versiones de los textos del informe dentro del propio caso.
- **Carpeta en ~/Factum/Evidencia**: `/Users/joelmiguelserrudo/Factum/Evidencia/QA_FINAL_001_8f37d42d/` (ZIP + `.factum/estado.json` + `.factum/pendiente/` vacía).
- **Informe en el backend**: `/Users/joelmiguelserrudo/Projects/Factum/server/src/Factum.Backend/dev-data/cases/8f37d42d-0aa9-4d86-aca1-b03ca7b84b28/`.
- **Vista previa de la segunda pestaña**: `/Users/joelmiguelserrudo/Projects/Factum/server/src/Factum.Agent/agent-data/previews/preview_7dfa3c52db32_20261013_180835_455.png`. La copia de trabajo `agent-data/cases/8f37d42d…` ya la borró Tatana al hacer commit.

## Capturas guardadas en esta carpeta
`01-paso6-listo.jpg`, `01-docx-portada.png`, `01-docx-android11.png`, `01-docx-firma.png`, `01-docx-pag2-causa-causa.png`, `02-retomar-detener.jpg`, `03-aviso-otra-pestana.jpg`, `04-modal-borrar.jpg`, `05-historial-busqueda.jpg`, `05-metricas.jpg`, `05-390px-header.png`, y en `docx/` el informe con su PDF y sus páginas.

## Pruebas físicas pendientes para el usuario
Desconectar el USB a mitad de una captura y de una grabación. Bloquear la pantalla mientras graba. Grabar 10 min o más con la pestaña visible (y confirmar ahí M1). iPhone. Windows 11.
