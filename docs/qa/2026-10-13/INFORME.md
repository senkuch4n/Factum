# QA de robustez Factum — 2026-10-13 (develop con #49 #53 #54 #55 #57)

Veredicto: **NO promover develop → main** hasta corregir C1 y C2.

Reportes por frente (docs/qa/2026-10-13/): api-backend.md, tatana.md, e2e-navegador.md, informe-formato.md (+ evidencia en informe/, informe-formato/, capturas/, ziprepro/, tatana-scripts/).

## Propuesta de HU (por prioridad)
1. zip-verificacion-stored (Crítica, backend Tatana + copia backend): C1 — VerifyAsync con SHA256.HashDataAsync sobre entrada Stored+AES de SharpZipLib da hash incorrecto para ciertos tamaños → el ZIP falla siempre. Regresión expuesta por #54. Repro qa/ziprepro.
2. grabacion-android-robusta (Crítica/Alta, Tatana + client): C2 corte de ~2 min al detener (scrcpy no respeta SIGINT, cierre forzado), grabación de 0 B registrada como evidencia, captura negra sin aviso; A2 grabación huérfana tras recargar sin forma de detenerla (consultar /agent/state al montar).
3. tatana-ws-y-hosts (Alta, Tatana): T-01 broadcast WS serial sin timeout traba todo; T-02 apagado 30 s; T-03 AllowedHosts "*" (DNS rebinding); T-04 config local inválida no arranca; bajas T-05…
4. wizard-concurrencia-pestanas (Alta, client + backend): A1 captura de otra pestaña importada al caso; A3 doble clic en Generar; A4 "Capturas: 3" vs 2 hashes; A5 eventos de custodia falsos/duplicados; A6 expediente repetido sin aviso; M6 chip "esperando dispositivo"; foco con Tab; papelera sin confirmar; superposición ficha/teléfono.
5. informe-formato-fixes (Alta/Media, backend): H01 firma separada; H02 U+FFFE/FFFF rompe la generación (Clean + validación API); H03 "Android Android" (page.tsx:413 vs DeviceInfo.cs:20); H04 RTL; H05/H06 portada/encabezado; menores (pixelado, títulos md, tablas md, "siendo las horas", sangrías, "Se constató" duplicado, "No informado", idioma es-AR, styles.xml).
6. api-hardening (Media, backend): API-01 headers de seguridad; API-02 UseExceptionHandler (+CORS en 500, sin stack); API-03 rate-limit de login por IP; API-04 403 vs 404; API-05 caracteres de control/RTL en campos.

## Pendiente de limpieza (no hay borrado de casos en UI/API)
Casos QA: ba916e4f-0708-4799-91ff-a83ac0f9a2e8, 5bfdf5eb-2947-4b21-9776-5f893f46e507 (mismo id en Tatana); carpetas ~/Factum/Evidencia/QA_ROBUSTEZ_001_*, dev-data/cases/<id>, 4 previews sueltas en agent-data.

## Pruebas físicas para el usuario
USB fuera a mitad de captura/grabación; pantalla bloqueada durante grabación 10+ min (duración real vs reloj); grabar con audio; iPhone real (DVT/AirPlay); Windows 11; PC sin GPU (3D).
