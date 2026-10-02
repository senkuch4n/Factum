# Historial de HU cerradas

Log append-only. Al cerrar cada HU (aprobada o bloqueada), el orquestador
agrega una sección `## <id> — APROBADA|BLOQUEADA (fecha)` con qué se hizo,
commits, modelos usados, intentos de revisión y lo que quedó pendiente.

## rediseno-base-primereact — APROBADA (2026-10-01)

- **Qué:** base del sistema de diseño comercial en `client/`. `primereact@10.9.9` (MIT) en modo unstyled con pass-through propio en Tailwind (`client/src/lib/prime/`); tokens `--fx-*` claro/oscuro con AA verificado (acento lima, separado del éxito menta); Inter + IBM Plex Mono vía `next/font`; arranque en oscuro (`ev-theme`); navbar `AppNavbar` (reemplaza solo el `<header>` de `/dashboard`); línea de estado + `SiteFooter` (contenido placeholder); 404 nueva; sonner/tooltip restilizados; `/design-system` solo en desarrollo. Inspiración visual xbox.com/es-AR, sin marca ni assets.
- **Decisiones:** D1–D11 recomendadas; DP1 → PrimeReact 10.9.9 MIT unstyled (la v11 exige licencia comercial).
- **Modelos:** afinador/architect (sesión), implementer-frontend opus, reviewer sonnet. Intentos de revisión: 0 rechazos.
- **Rama:** `feat/rediseno-base-primereact` (sin commitear al cierre).
- **Pendiente:** contenido del `SiteFooter` (D8); contraste de `AgentChip` en modo claro y z-index Toast vs navbar → `rediseno-dashboard`; prueba visual manual (SDD §8); `npm audit` con 14 vulnerabilidades preexistentes.

## marca-comercial-sin-mpf-gfd — APROBADA (2026-10-01)

- **Qué:** Factum sin marca MPF/GFD/Evidentia. Logo "Sello" (F maciza con esquina cortada, Sora 700) generado de forma reproducible por `ops/brand/build-brand.mjs` para `client/`, `agent-ui/` y el pie del informe; íconos de app en variante clara. `Branding` (nombre, logo, líneas de contacto) configurable y vacío por defecto, con la config real en `appsettings.Local.json` (ignorado); `GET /api/config/public` (`organization_name`, `organization_logo_url`) y `GET /api/config/branding/logo`. Plantilla v3 emparchada (sin imágenes institucionales, `{ORGANIZACION}`) + "Realizado con Factum" con el Sello en el pie. Borrado de assets/plantillas viejas (B9, autorizado por el usuario). AGENTS.md: descripción comercial y casing snake_case_lower.
- **Decisiones:** D1–D10 recomendadas, HU partida (D7 B); emisor = estudio jurídico (D4 → C); DP2 sí, DP3 pie+logo sin versión, DP4 variante clara.
- **Modelos:** afinador/architect (sesión), implementer-backend y implementer-frontend opus, reviewer sonnet. 0 rechazos. Dos acciones bloqueadas por permisos (borrado B9 y escritura del PNG en server/) resueltas con autorización explícita del usuario.
- **Rama:** `feat/marca-comercial-sin-mpf-gfd` (encadenada sobre `feat/rediseno-base-primereact`).
- **Pendiente:** plantilla v4 y terminología → `informe-pericial-de-parte`; login/modo/Faro → `auth-e-integraciones-sin-mpf`; prueba manual (informe de punta a punta, empaquetado de Tatana); no commitear `agent-ui/tsconfig.web.tsbuildinfo`.

## informe-pericial-de-parte — APROBADA (2026-10-01)

- **Qué:** el informe pasa a ser un Informe Pericial Técnico Informático. Perfil persistente del perito (`expert_profiles`, por DNI de login, copiado al caso); campos nuevos de causa (tribunal, organismo, sala, integrantes, carátula, partes, proponente, titular del dispositivo…); paso 4 "Informe" (`ReportStep`) con textos largos y borrador de operaciones; roles de captura (IMEI/modelo, nombre del dispositivo) y anexo de capturas; `plantilla_informe_v4.docx` (placeholders canónicos, frases neutralizadas, membrete del estudio, firma) generada desde `ops/plantilla/`, sin datos reales; domicilio constituido en config local (`Report:DomicilioConstituido`); D10: el DOCX sale del ZIP y el hash informado es el del ZIP entregado; hora local configurable (America/Argentina/Buenos_Aires); terminología no fiscal solo en etiquetas. v3 y `sin-foto-placeholder.png` borrados.
- **Decisiones:** D1–D16 recomendadas (validadas por el usuario; es el perito). DP1/DP2 (textos por defecto y frases fijas) aceptados en modo autónomo; pendientes de revisión del usuario.
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/informe-pericial-de-parte` (encadenada sobre `feat/marca-comercial-sin-mpf-gfd`).
- **Pendiente:** revisar la v4 en Word/WPS y los textos por defecto; cargar `Report:DomicilioConstituido`; recorrido e2e con dispositivo real; código muerto de conversión a PDF (T16); `zip-cifrado-real` desbloqueada.

## auth-e-integraciones-sin-mpf — APROBADA (2026-10-01)

- **Qué:** `MpfAuthProvider` reemplazado por `ExternalHttpAuthProvider` genérico y configurable (`Auth:External:*`, campos de respuesta configurables, URL = BaseUrl + LoginPath, solo 401/403 = credenciales inválidas); modo `"mpf"` → `"external"` con compatibilidad y warning para `Auth:Mode=mpf`/`Auth:Mpf*`; modo desconocido o config incompleta falla seguro; Faro como integración opcional "Soporte" apagada por defecto (`DisabledSupportService`, filtro `RequireSupportEnabled`, flag en config pública, dock sin el ítem); `ServiceKey` fuera de los archivos versionados; `UserMenu` sin "sigla".
- **Decisiones:** D1 A, D2 A, D3 B validadas por el usuario; D1–D12 de la HU, DP1 A y DP2 A en modo autónomo.
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/auth-e-integraciones-sin-mpf` (encadenada sobre `feat/informe-pericial-de-parte`).
- **Pendiente:** **rotar la `ServiceKey` de Faro** (sigue en el historial de git; acción del usuario); prueba manual 11.4 (login contra proveedor real/falso, soporte encendido/apagado).

## rediseno-pagina-inicio — APROBADA (2026-10-01)

- **Qué:** login (`/`) rediseñado con PrimeReact (pt `password`, `inputtext`, `button`), hero con la marca y la organización configurada, validación de DNI en cliente, mensajes de error legibles, redirección si ya hay sesión, animación de entrada con `prefers-reduced-motion`; sin navbar/footer del shell; independiente del literal del modo de auth.
- **Decisiones:** D1–D9 recomendadas (modo autónomo).
- **Modelos:** afinador/architect (sesión), implementer-frontend opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/rediseno-pagina-inicio` (encadenada sobre `feat/auth-e-integraciones-sin-mpf`).
- **Pendiente:** F14 — borrar `ThemeToggle.tsx` y `ui/webcam-pixel-grid.tsx` (sin consumidores; borrado denegado por permisos, requiere autorización del usuario o va en `rediseno-cierre-legacy`); orden de merge del pt de inputtext (`!pl-10`) a corregir en el cierre.

## zip-cifrado-real — APROBADA (2026-10-01)

- **Qué:** el ZIP de evidencia ahora se cifra de verdad (AES-256 vía `EvidenceZip`), con verificación posterior (detecta ZipCrypto/ZIP en claro, clave incorrecta, hash alterado, entradas faltantes/extra); flags `zip_encrypted`/`zip_encryption` en el caso, `zip_password` fuera de los DTOs (`[JsonIgnore]`) y servida solo por `GET /api/cases/{id}/zip-password`; `encrypt_zip` en la config pública; frontend con `CopyButton` y estados cifrado/plano. Proyecto de tests nuevo `server/tests/Factum.Backend.Tests` (10 tests).
- **Decisiones:** D1–D11 y P1 A / P2 A en modo autónomo.
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos. Backend retomado tras pausa del usuario.
- **Rama:** `feat/zip-cifrado-real` (encadenada sobre `feat/rediseno-pagina-inicio`).
- **Pendiente:** pruebas manuales M1–M9 de la SDD (e2e con caso real, M5 texto de aseguramiento cifrado, M7 Zip64 > 4 GB, M9 403 en caso ajeno); `ToDictionary` asume nombres únicos en el directorio del caso.

## rediseno-dashboard-historial — APROBADA (2026-10-01)

- **Qué:** parte 1 del rediseño de /dashboard: modo historial (CaseHistory con DataTable/Paginator/Calendar de Prime, CaseCard/CaseGridCard, StatusBadge con Tag, DashboardStats) y piezas globales (AppNavbar, toasts `FxToastProvider`, `feedback/` y `overlay/` con banners, tips, drawer y ConfirmDialog, GuideModal/USBGuide, SoporteModal, ExpertProfileDialog, AgentChip). Nuevos pt: calendar, column, datatable, inputtextarea, paginator, selectbutton, tag, tooltip. Borrados componentes `ui/` sin consumidores (cycling-placeholder-input, data-table, date-range-calendar, floating-dock, pagination).
- **Decisiones:** D1–D11 de la paraguas y DP1–DP3 A (autónomo). CaseCard respeta el contrato de zip-cifrado-real (sin `zip_password`).
- **Modelos:** architect (sesión), implementer-frontend opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/rediseno-dashboard-historial` (encadenada sobre `feat/zip-cifrado-real`).
- **Pendiente:** prueba manual (chip online/REC con equipo real, contraste con DevTools, regresión del wizard 1–6); token `rounded-fx-pill` para la parte 4; typos preexistentes ("inspecciónes", orden lexicográfico de N° de causa).

## rediseno-dashboard-wizard — APROBADA (2026-10-01)

- **Qué:** parte 2 del rediseño de /dashboard: contenedor del wizard (riel de pasos con ProgressBar, franja compacta mobile, banners, foco y scroll al cambiar de paso, `MotionConfig reducedMotion`), pasos 1, 2, 4, 5 y 6 (CaseFormStep con Calendar/Dropdown, DeviceConnect, PhoneFrame + `PhoneShell`, ReportStep, GenerateStep, ResultStep), `components/wizard/` (StepHeader, StepActions), `CopyButton` migrado con la misma firma. Nuevos pt: dropdown, progressbar. Borrado `ui/dotted-glow-background.tsx`.
- **Decisiones:** DP1–DP6 A (autónomo).
- **Modelos:** architect (sesión), implementer-frontend opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/rediseno-dashboard-wizard` (encadenada sobre `feat/rediseno-dashboard-historial`).
- **Pendiente:** prueba manual con Tatana real, `sticky` del paso 3 dentro de `fx-card`, contraste con DevTools, "Restaurar texto por defecto" con backend real.

## rediseno-dashboard-captura — APROBADA (2026-10-01)

- **Qué:** parte 3 del rediseño de /dashboard: paso 3 de captura (estado del equipo, captura/grabación, AirPlay, mezcla de micrófono, bandeja de capturas y adjuntos, zona de soltar archivos, marcas de identificación, escenario con `PhoneShell`) y sus modales (visor, webcam, cámara externa, explorador de archivos del celular, modo de grabación iOS) sobre `overlay/FxMediaDialog` y `components/capture/` (incluye `MediaErrorGuide`). Nuevo pt checkbox. Sin cambios de contrato con Tatana ni en `Props` de CaptureStep.
- **Decisiones:** DP1–DP6 A (autónomo).
- **Modelos:** architect (sesión), implementer-frontend opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/rediseno-dashboard-captura` (encadenada sobre `feat/rediseno-dashboard-wizard`).
- **Pendiente:** prueba manual con Android/iOS real, AirPlay, guía on_device, luz de la webcam, Dropdown con varios dispositivos, foco del checklist paso 5 → `#capture-roles-status`; bug preexistente: grabación de cámara externa aparece en dos chips (tarea aparte); grep de `…:outline` bajo `cn()` antes de borrar clases legacy (parte 4).

## rediseno-cierre-legacy — APROBADA (2026-10-01)

- **Qué:** cierre de la serie de rediseño: fuera shadcn (`components.json`, `@layer base`, variables legacy), carpeta `components/ui/` eliminada (ConfirmDialog → `overlay/`, CopyButton → `feedback/`, Lens → `usb-guide/`; resto borrado), `ThemeToggle`, `DevModeBanner`, `AppToaster`, clases y keyframes legacy de `globals.css`, escala de radios legacy; token `rounded-fx-pill`; `borderColor.DEFAULT = --fx-border`; pt inputtext/inputtextarea reaplican el `className` del consumidor (sin `!`); historial con "inspecciones" y orden con `Intl.Collator` numérico; dependencias npm sin uso desinstaladas.
- **Decisiones:** DP1–DP8 A (autónomo); DP7 sin fallback (medido OK).
- **Modelos:** architect (sesión), implementer-frontend opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/rediseno-cierre-legacy` (encadenada sobre `feat/rediseno-dashboard-captura`).
- **Pendiente:** prueba manual del wizard con Tatana real (bordes sin color, diálogos, tooltips) en los dos temas.

## rediseno-dashboard (paraguas) — APROBADA (2026-10-01)

- Cerrada al aprobarse sus 4 hijas: historial, wizard, captura y cierre-legacy.

## grabacion-camara-duplicada — APROBADA (2026-10-01)

- **Qué:** la bandeja del paso 3 tiene una sola fuente de verdad (`files` de `useFileManager`); se elimina `localFiles` de `CaptureStep` y la vista sale de `localBlobs` (blob URLs revocadas al borrar/desmontar). Cada grabación de cámara externa, adjunto de PC y foto de webcam aparece una vez y sobrevive a ir al paso 2 y volver; borrar siempre saca el archivo de la subida. Contador único para `adjunto_<fecha>_<hora>_<n>` (sin pérdida silenciosa por colisión). Videos de PC solo en "Adjuntos".
- **Decisiones:** HU D1–D4 A (usuario); SDD D4 → B por el usuario (chip muestra el nombre original, `adjunto_…` secundario y en el title); resto recomendadas.
- **Modelos:** afinador/architect (sesión), implementer-frontend opus, reviewer sonnet. 0 rechazos.
- **Rama:** `feat/grabacion-camara-duplicada` (encadenada sobre `feat/rediseno-cierre-legacy`).
- **Pendiente:** prueba manual (grabar con cámara externa, adjuntar varios archivos de una vez, ir al paso 2 y volver, borrar y confirmar que no se sube).

## formulario-caso-catalogos — APROBADA (2026-10-01)

- **Qué:** catálogos por perito en MongoDB (colección nueva, índice único perito+catálogo+clave normalizada; `CatalogsController`) para destinatario (ex "Tribunal"), partes (compartido por denunciante/denunciada/proponente), profesiones y tipos de dispositivo: alta automática al guardar un caso, editar/borrar desde el campo (`CatalogAutoComplete`, `CatalogManageDialog`), siembra inicial leyendo los casos del perito (solo lectura). Integrantes como lista (`IntegrantesField`) con tratamiento por fila; el backend sigue escribiendo `integrantes_tribunal` como "A, B y C" ("e" ante /i/) y los casos viejos se muestran como una fila. Snapshot: editar el catálogo no cambia casos. Fix aparte (commit previo): desborde de "Fecha de intervención".
- **Decisiones:** HU D1–D11 A (usuario); SDD D1–D15 recomendadas, DP1 A (usuario).
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos. 48 tests backend OK.
- **Rama:** `feat/formulario-caso-catalogos` (encadenada sobre `feat/grabacion-camara-duplicada`).
- **Pendiente:** reiniciar el backend para tomar los endpoints nuevos; prueba manual e2e con Mongo real (SDD §11) y vista a 375 px en ambos temas.

## informe-diseno-modelo — APROBADA (2026-10-01)

- **Qué:** plantilla v5 del informe pericial (generada por `ops/plantilla/build_plantilla_v5.py`, determinista) con el estilo de `docs/informe_modelo.pdf`: portada (título, causa, carátula, perito, fecha, logo completo como tarjeta, nombre y contacto del estudio, franja primaria y bandas de acento), banda fina en el encabezado de páginas interiores con título y causa, títulos de sección en color primario, bloques en dos columnas, tabla de hashes rediseñada, Century Gothic/Arial/Courier New, "Página N de M" (cuenta la portada) y "Realizado con Factum" en todas las páginas. Branding suma `PrimaryColor`, `AccentColor` (validación hex y contraste ≥ 4.5:1) y `OrganizationIsotype` opcional (fallback: nombre del estudio en la banda). Defaults neutros versionados; logo y colores del estudio solo en `branding/` y `appsettings.Local.json` (ignorados). Texto legal sin cambios; sigue DOCX; informes ya generados no cambian.
- **Decisiones:** HU D1–D11 recomendadas (usuario); SDD 15 recomendadas, DP1–DP3 A (usuario).
- **Modelos:** afinador/architect (sesión), implementer-backend opus, reviewer sonnet. 1 rechazo (colores exactos del estudio en la SDD versionada; corregido por el orquestador en el doc).
- **Rama:** `feat/informe-diseno-modelo` (encadenada sobre `feat/formulario-caso-catalogos`).
- **Pendiente:** abrir las muestras en Word (scratchpad de la sesión, `muestras/estudio/`) y aprobar colores; pasar el isotipo PNG transparente cuando exista (solo config); reiniciar el backend.

## informe-diseno-v6 — APROBADA (2026-10-01)

- **Qué:** plantilla v6 "Filete" del informe pericial (`ops/plantilla/build_plantilla_v6.py`, determinista; reemplaza a la v5): portada blanca con filete verde, título en tinta, subtítulo, ficha de la causa, logo más chico con nombre/contacto y "Realizado con Factum"; interior con encabezado de texto gris + línea fina (sin el bloque de acento de la v5), romanos verdes arriba de cada título con filete, fichas a todo el ancho, tabla de hashes con línea verde y fila del ZIP con tinte, pie "Realizado con Factum" + "Página N de M". Paleta Factum como default de Branding (el estudio la puede pisar); en la instalación local se borraron las claves de color. Texto legal idéntico (se conservan ":" y sangría).
- **Decisiones:** HU D1–D10 recomendadas, D9 A "Filete" elegida sobre canvas comparativo (https://claude.ai/artifact/FabuSoobjeFwZKxkn96uU5); SDD DP1/DP2 A (usuario).
- **Modelos:** afinador/architect (sesión), implementer-backend opus, reviewer sonnet. 0 rechazos. 88 tests OK.
- **Rama:** `feat/informe-diseno-v6` (encadenada sobre `feat/informe-diseno-modelo`).
- **Pendiente:** reiniciar el backend; revisar el render en Word/WPS (solo verificado en LibreOffice); README: default de la caja del logo a revisar.

## editor-texto-enriquecido — APROBADA (2026-10-01)

- **Qué:** las ocho secciones del paso "Informe" pasan a un editor WYSIWYG (Tiptap 3, `client/src/components/editor/`) con barra accesible (B/I/U, viñetas, numerada, deshacer/rehacer; "Más": subtítulo, cita, código, bloque de código, enlace, formatos disponibles), atajos Markdown y pegado desde Word filtrado (Ctrl/Cmd+Shift+V sin formato). Se guarda Markdown acotado ("dialecto Factum") con `report_texts.formato = "markdown"`; casos viejos sin la marca se generan idénticos y se convierten con escape solo si el perito edita. Backend: Markdig 1.4.0 + renderer propio a OpenXML sobre la v6 (listas • ◦ ▪ y 1., código Courier New, cita, subtítulo, enlaces con URL visible), validación en servidor (sin HTML salvo `<u>`, sin imágenes, solo http/https/mailto), "vacío" igual en cliente y servidor, defaults en Markdown.
- **Decisiones:** HU 11 recomendadas (usuario); SDD D1–D20 recomendadas, DP1/DP2 A (usuario).
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos. 209 tests backend OK.
- **Rama:** `feat/editor-texto-enriquecido` (encadenada sobre `feat/informe-diseno-v6`).
- **Pendiente:** prueba manual (pegado desde Word/Google Docs, Ctrl/Cmd+Shift+V en Chrome/Firefox/Safari, abrir el DOCX en Word sin aviso de reparación); reiniciar el backend.

## editor-imagenes-informe — APROBADA (2026-10-02)

- **Qué:** botón "Imagen" en el editor de las secciones del informe con selector de capturas del caso (`CapturePickerDialog`, miniaturas por endpoint autenticado de solo lectura, autorización por perito dueño, PNG/JPEG por magic bytes, sin SVG); nodo Tiptap `reportImage` serializado como `![alt](captura:<nombre>)`; en el DOCX la captura sale centrada como "Figura N" (misma numeración que el anexo, reutiliza su ImagePart, sin tope de tamaño), y sigue en el anexo. Se bloquea la generación si una captura citada falta o cambió ("Imagen no disponible" + "Quitar"); pegar/arrastrar imágenes bloqueado con aviso; tope de 20 por sección; obligatoria con solo imagen = vacía, opcional con solo imagen = sale. ZIP y tabla de hashes sin cambios.
- **Decisiones:** HU D1–D12 A (usuario); SDD recomendadas, DP1 B y DP2 B (usuario).
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 0 rechazos. 335 tests backend.
- **Rama:** `feat/editor-imagenes-informe` (encadenada sobre `feat/editor-texto-enriquecido`).
- **Pendiente:** ~~2 tests intermitentes~~ corregidos aparte (máscara de hash por <w:t>); prueba manual (insertar capturas, borrar una captura y ver el bloqueo, abrir en Word).

## instalacion-local-docker — APROBADA (2026-10-02)

- **Qué:** paquete de instalación para una PC Windows en el estudio. `deploy/windows/`: compose de producción (imágenes preconstruidas por versión exportadas a `.tar`, `pull_policy: never`, web y backend solo en 127.0.0.1, Mongo 7.0.43 sin publicar en red interna, healthchecks, evidencia en `C:\Factum\evidencia`), `.env` con secretos generados al instalar, `appsettings.Local` montado, scripts PowerShell 5.1 (`instalar` con chequeo de requisitos Q1–Q11, `backup` con pausa y manifiesto de hashes + tarea diaria 20:00, `restaurar`, `actualizar` con backup y rollback, `diagnostico`, `abrir-factum`), `.bat` envoltorios, armado del paquete y del Tatana portátil (verifica `Mock=false`). Imagen del frontend `standalone` (≈295 MB, sin `.env.local`) e imagen del backend sin LibreOffice. Tatana escucha en localhost por defecto (`--bind`), carga `appsettings.Local.json`; Dockerfile del agente borrado. Guía `docs/instalacion-windows.md`. Modo de auth `dev` documentado como riesgo (HU `usuarios-locales` pendiente).
- **Decisiones:** HU D1–D20 A (usuario); SDD DT1–DT19 recomendadas, DT2/DT4/DT5 A (usuario).
- **Modelos:** afinador/architect (sesión), implementers opus, reviewer sonnet. 1 rechazo (parser del `.ini` de `launch-tatana.bat` con claves vacías).
- **Rama:** `feat/instalacion-local-docker` (desde `develop`).
- **Pendiente:** prueba real en Windows (§10.3 de la SDD: instalación sin internet, reinicio, backup/restauración con byte alterado, actualización con falla simulada, Tatana con celular real, comando del `.ini` en cmd); mejoras no bloqueantes en `restaurar.ps1` (sobrantes del manifiesto, reinicio tras mongorestore fallido) y `actualizar.ps1` (rollback ante excepción entre paso 4 y `up`).
