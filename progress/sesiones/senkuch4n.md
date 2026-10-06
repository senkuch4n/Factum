# Bitácora de sesiones — senkuch4n

Bitácora viva del orquestador de esta persona: una línea por evento
(`- AAAA-MM-DD — #N slug — qué pasó`). Si una sesión se corta, la siguiente
retoma leyendo esto y `node ops/harness/hu.mjs ver`.

- 2026-10-06 — Migración del arnés a GitHub Project "Factum – HU" (https://github.com/users/senkuch4n/projects/3). backlog.json, progress/current.md y progress/history.md archivados. HU pendientes migradas: #6 usuarios-locales, #7 ios-herramientas-windows (ambas en Backlog, sin asignar).
- 2026-10-06 — chore — migración del arnés commiteada en chore/arnes-github-project, PR #8 contra develop. appsettings.json del agente (Mock:true) queda local sin commitear.
- 2026-10-06 — #9 zip-local-informe-servidor — alta, tomada, rama feat/zip-local-informe-servidor (desde chore/arnes-github-project hasta que se mergee PR #8), fase afinando; lanzado afinador.
- 2026-10-06 — Project: creada vista 'Tablero' (board por Status); la única vista era una tabla.
- 2026-10-06 — #9 afinador done -> docs/hu-zip-local-informe-servidor.md (13 dudas); fase afinada_pendiente_validacion. Item del Project recreado (el original no aparecía en el listado).
- 2026-10-06 — #9 validada por el usuario (recomendadas; D6=A con carpeta default Documentos/Factum/Evidencia; D8=A + borrador sobrevive recarga). Lanzado architect.
- 2026-10-06 — Project: #9 no aparecía en el listado/tablero aunque el item existía (GET directo OK). Retraso del índice de GitHub, sin incidente publicado; apareció tras recrear el item y esperar unos minutos. Ahora en 'Lista para dev'.
- 2026-10-06 — PR #8 mergeado; feat/zip-local-informe-servidor avanzada a origin/develop (7813181).
- 2026-10-06 — #9 architect done -> Refactorizaciones/zip-local-informe-servidor.md con 4 decisiones pendientes (§12); esperando al usuario.
- 2026-10-06 — #9 usuario resolvió DP1-DP3 = A; DP4 = ruta fija C:\Factum\Evidencia (reemplaza Documentos de DP2). Architect actualizando la SDD.
- 2026-10-06 — #9 SDD actualizada con decisiones (DP4 C:\Factum\Evidencia; comparte carpeta con datos del backend en instalación local, sin choque de nombres). arquitectura_lista -> implementando; lanzados implementer-backend y implementer-frontend (opus).
- 2026-10-06 — #9 implementer-frontend done -> progress/impl_frontend_zip-local-informe-servidor.md (tsc + next build OK; recorrido §11.2 pendiente; riesgo file_busy en grabaciones Android/ffmpeg). Esperando backend.
- 2026-10-06 — #9 implementer-backend done -> progress/impl_backend_zip-local-informe-servidor.md (sin bloqueos; verify.sh OK). -> en_revision; lanzado reviewer (sonnet).
- 2026-10-06 — #9 reviewer APROBADA -> progress/review_zip-local-informe-servidor.md (Backend.Tests 452/452, Agent.Tests 138/138). Fase aprobada; esperando prueba manual del usuario antes de commit/PR.
- 2026-10-06 — #9 prueba manual: 1,3,4,6 OK; 5 y 7 no probables en local; 8-9 pendientes. Punto 2 (ZIP no abre): ZIP real verificado con 7zz y la contraseña de Mongo -> abre OK (AES-256, 3 archivos). Probable Utilidad de Archivo de macOS; preguntado al usuario.
- 2026-10-06 — #9 OK del usuario (ZIP abre con otra herramienta; la Utilidad de Compresión de macOS no soporta AES). Commit + push + PR contra develop.
- 2026-10-06 — #9 cerrada: PR #10 contra develop, resumen de cierre comentado en el issue. Sin HU activa.
- 2026-10-06 — PR #10 mergeado. Tarea chica fuera del backlog: fix/aviso-zip-aes (aviso 7-Zip/Keka más visible en el resultado), implementer-frontend + reviewer sin HU. #6: preguntado al usuario antes de cerrar (Auth dev sin contraseña en la nube).
- 2026-10-06 — #6 usuarios-locales se mantiene (verificado: hoy no hay colección de usuarios ni hash de contraseñas; solo Auth dev/external). Propuesto orden: aviso ZIP -> #6 -> despliegue-nube.
- 2026-10-06 — fix/aviso-zip-aes: reviewer APROBADA -> progress/review_aviso-zip-aes.md; commit + PR.
